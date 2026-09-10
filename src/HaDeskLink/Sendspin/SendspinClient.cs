// HA DeskLink - Sendspin streaming client (Phase E)
// WebSocket Sendspin player client: connect → Noise KKpsk2 responder
// handshake → server/hello → client/hello (RAW JSON, verified wire format)
// → server/activate → client/state → time sync (server/time + 10s bursts)
// → stream/start → type-4 audio chunks → PulseAudio output.
//
// Scheduling: local play time = timeFilter.ComputeClientTime(timestamp)
//   - OutputDelayMs * 1000 µs; a chunk whose play time already passed is
//   dropped (late drop). A 100 ms jitter buffer paces writes against the
//   local monotonic clock.
//
// This program is licensed under the GNU General Public License v3.
#nullable enable
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace HaDeskLink.Sendspin;

/// <summary>
/// Operational status surfaced to the UI.
/// </summary>
public enum SendspinStatus
{
    Disabled,
    Connecting,
    Handshaking,
    Synchronizing,
    Streaming,
    Disconnected,
    Error,
}

/// <summary>
/// One scheduled audio chunk (decoded PCM 48k/16/2) with its local play time.
/// </summary>
internal sealed class ScheduledChunk
{
    public required long PlayTimeUs { get; init; }
    public required byte[] Pcm { get; init; }
}

/// <summary>
/// Sendspin player client. One instance owns one WebSocket connection to a
/// Sendspin server (MA host), the Noise session, the time filter, the audio
/// jitter buffer and the PulseAudio sink.
/// </summary>
public sealed class SendspinClient : IDisposable
{
    // ── Wire constants ──
    private const int BinaryHeaderSize = 13;  // type(1) + ts(8) + send_ahead(4)
    private const byte AudioChunkType = 4;

    // ── Scheduling ──
    private const int JitterBufferMs = 100;
    private const int OutputDelayMs = 100;     // reported via client/state
    private const int RequiredLeadTimeMs = 250;
    private const int MinBufferMs = 250;
    private const int InitialVolume = 100;
    private const int UnsyncedPlayLeadUs = 500_000;
    private const int TimeSyncBurstCount = 8;  // 8 exchanges back-to-back
    private static readonly TimeSpan TimeSyncBurstInterval = TimeSpan.FromSeconds(10);

    private readonly string _url;
    private readonly string _playerName;
    private readonly SendspinTimeFilter _timeFilter = new();
    private readonly Stopwatch _monotonic = Stopwatch.StartNew();
    private readonly object _sendLock = new();

    private ClientWebSocket? _ws;
    private NoiseTransport? _transport;
    private CancellationTokenSource? _cts;
    private Task? _receiveTask;
    private Task? _timeTask;
    private Task? _playbackTask;

    // Jitter buffer keyed by play time.
    private readonly ConcurrentDictionary<long, ScheduledChunk> _jitterBuffer = new();
    private readonly ConcurrentQueue<long> _playTimes = new();
    private AudioSinkLinux? _sink;
    private int _streamSampleRate;
    private int _streamChannels;
    private int _streamBitDepth;
    private volatile bool _streamActive;

    private volatile SendspinStatus _status = SendspinStatus.Disabled;
    private string _lastError = "";
    private bool _disposed;

    public event Action<SendspinStatus>? StatusChanged;

    public SendspinStatus Status => _status;
    public string LastError => _lastError;
    public bool TimeSynchronized => _timeFilter.IsSynchronized;
    public long TimeErrorUs => _timeFilter.Error;
    public bool IsStreaming => _streamActive;
    public string ServerId { get; private set; } = "";

    public SendspinClient(string host, int port, string playerName)
    {
        _url = $"ws://{host}:{port}/sendspin";
        _playerName = playerName;
    }

    private void SetStatus(SendspinStatus status)
    {
        _status = status;
        StatusChanged?.Invoke(status);
    }

    /// <summary>Local monotonic clock in microseconds (SteadyClock domain).</summary>
    private long NowUs => _monotonic.Elapsed.Ticks * (1_000_000L / TimeSpan.TicksPerSecond);

    // ═══════════════════════════════════════════════════════
    // Connection lifecycle
    // ═══════════════════════════════════════════════════════

    /// <summary>
    /// Connect and run the session until cancelled or the socket drops.
    /// </summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        SetStatus(SendspinStatus.Connecting);
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var ct = _cts.Token;

        _ws = new ClientWebSocket();
        try
        {
            await _ws.ConnectAsync(new Uri(_url), ct);
        }
        catch (Exception ex)
        {
            Fail($"WebSocket connect failed: {ex.Message}");
            return;
        }

        try
        {
            SetStatus(SendspinStatus.Handshaking);
            var handshake = NoiseHandshake.RunResponder(
                SendspinIdentity.Generate(),
                async text => await SendTextRaw(text, ct),
                async () => await ReceiveTextRaw(ct));
            _transport = handshake.Transport;
            ServerId = handshake.ServerId;

            // ── server/hello ──
            string serverHello = await ReceiveJson(ct);
            if (MessageType(serverHello) != "server/hello")
                throw new InvalidOperationException($"expected server/hello, got {MessageType(serverHello)}");

            // ── client/hello (RAW JSON — verified wire format with alias key
            //    'player@v1_support' and supported_pair_methods = {}) ──
            await SendJson(BuildClientHello(), ct);

            // ── server/activate ──
            string activate = await ReceiveJson(ct);
            if (MessageType(activate) != "server/activate")
                throw new InvalidOperationException($"expected server/activate, got {MessageType(activate)}");
            ApplyActivation(activate);

            SetStatus(SendspinStatus.Synchronizing);

            // ── client/state (initial full state) ──
            await SendJson(BuildClientState(), ct);

            // Steady state: receiver loop + time-sync burst loop + playback loop.
            _receiveTask = Task.Run(() => ReceiveLoop(ct), ct);
            _timeTask = Task.Run(() => TimeSyncLoop(ct), ct);
            _playbackTask = Task.Run(() => PlaybackLoop(ct), ct);

            await Task.WhenAll(_receiveTask, _timeTask).ConfigureAwait(ConfigureAwaitOptions.None);
        }
        catch (OperationCanceledException)
        {
            SetStatus(SendspinStatus.Disconnected);
        }
        catch (Exception ex)
        {
            Fail(ex.Message);
        }
    }

    private void Fail(string message)
    {
        _lastError = message;
        SetStatus(SendspinStatus.Error);
        try { _cts?.Cancel(); } catch { }
    }

    // ═══════════════════════════════════════════════════════
    // Cleartext WS I/O (handshake phase)
    // ═══════════════════════════════════════════════════════

    private async Task SendTextRaw(string text, CancellationToken ct)
    {
        var segment = new ArraySegment<byte>(Encoding.UTF8.GetBytes(text));
        await _ws!.SendAsync(segment, WebSocketMessageType.Text, true, ct);
    }

    private async Task<string> ReceiveTextRaw(CancellationToken ct)
    {
        var buffer = new byte[16 * 1024];
        using var sb = new MemoryStream();
        WebSocketReceiveResult result;
        do
        {
            result = await _ws!.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
            if (result.MessageType == WebSocketMessageType.Close)
                throw new InvalidOperationException("WebSocket closed during handshake");
            sb.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);
        return Encoding.UTF8.GetString(sb.ToArray());
    }

    // ═══════════════════════════════════════════════════════
    // Encrypted JSON I/O
    // ═══════════════════════════════════════════════════════

    private async Task SendJson(string json, CancellationToken ct)
    {
        if (_transport == null) return;
        byte[] frame = _transport.EncryptJson(json);
        lock (_sendLock)
        {
            try
            {
                _ws!.SendAsync(new ArraySegment<byte>(frame), WebSocketMessageType.Binary, true, ct)
                    .GetAwaiter().GetResult();
            }
            catch (OperationCanceledException) { }
            catch (WebSocketException) { }
        }
    }

    private async Task<string> ReceiveJson(CancellationToken ct)
    {
        while (true)
        {
            var (isJson, plaintext) = await ReceiveFrame(ct);
            if (isJson) return Encoding.UTF8.GetString(plaintext);
        }
    }

    private async Task<(bool IsJson, byte[] Plaintext)> ReceiveFrame(CancellationToken ct)
    {
        var buffer = new byte[16 * 1024];
        using var ms = new MemoryStream();
        WebSocketReceiveResult result;
        do
        {
            result = await _ws!.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
            if (result.MessageType == WebSocketMessageType.Close)
                throw new WebSocketException("WebSocket closed by server");
            ms.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);
        return _transport!.Decrypt(ms.ToArray())
            ?? throw new InvalidOperationException("fragment reassembly not supported on receive");
    }

    // ═══════════════════════════════════════════════════════
    // Wire messages (raw JSON builders — the verified client/hello format)
    // ═══════════════════════════════════════════════════════

    private string BuildClientHello()
    {
        // Exact verified wire format: alias key 'player@v1_support' (NOT
        // 'player_support'), supported_pair_methods = {} (object map).
        return "{\"payload\":{"
            + "\"name\":\"" + JsonEscape(_playerName) + "\","
            + "\"supported_roles\":[\"player@v1\"],"
            + "\"trust_level\":\"none\","
            + "\"device_info\":{\"product_name\":\"HA DeskLink\",\"software_version\":\"" 
            + JsonEscape(HaApiClient.GetVersion()) + "\"},"
            + "\"player@v1_support\":{"
            + "\"supported_formats\":["
            + "{\"codec\":\"pcm\",\"sample_rate\":48000,\"bit_depth\":16,\"channels\":2},"
            + "{\"codec\":\"flac\",\"sample_rate\":48000,\"bit_depth\":16,\"channels\":2}],"
            + "\"buffer_capacity\":4194304,"
            + "\"supported_commands\":[\"volume\",\"mute\"]},"
            + "\"supported_pair_methods\":{},"
            + "\"unpaired_access\":{\"enabled\":true}"
            + "},\"type\":\"client/hello\"}";
    }

    private string BuildClientState()
    {
        return "{\"payload\":{"
            + "\"available\":true,"
            + "\"player\":{"
            + "\"volume\":" + InitialVolume + ","
            + "\"muted\":false,"
            + "\"static_delay_ms\":0,"
            + "\"required_lead_time_ms\":" + RequiredLeadTimeMs + ","
            + "\"min_buffer_ms\":" + MinBufferMs
            + "}"
            + "},\"type\":\"client/state\"}";
    }

    private string BuildClientTime(long nowUs)
        => "{\"payload\":{\"client_transmitted\":" + nowUs + "},\"type\":\"client/time\"}";

    private static string MessageType(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";
    }

    private static string JsonEscape(string s) => JsonSerializer.Serialize(s);

    // ═══════════════════════════════════════════════════════
    // Activation / stream lifecycle
    // ═══════════════════════════════════════════════════════

    private void ApplyActivation(string activateJson)
    {
        // V1 client: accept the activation as-is (sentinel PSK + unpaired
        // access is admissible per spec). active_roles player@v1 → player mode.
        using var doc = JsonDocument.Parse(activateJson);
        if (doc.RootElement.TryGetProperty("payload", out var payload))
        {
            // Record initial active_roles; later activations persist them.
            if (payload.TryGetProperty("active_roles", out var roles))
            {
                bool playerActive = false;
                foreach (var role in roles.EnumerateArray())
                    if (role.GetString() == "player@v1") playerActive = true;
                _streamActive = _streamActive || playerActive;
            }
        }
    }

    private void HandleServerMessage(string json)
    {
        switch (MessageType(json))
        {
            case "server/time":
                HandleServerTime(json);
                break;
            case "stream/start":
                HandleStreamStart(json);
                break;
            case "stream/clear":
                _jitterBuffer.Clear();
                _playTimes.Clear();
                break;
            case "stream/end":
                _streamActive = false;
                _jitterBuffer.Clear();
                _playTimes.Clear();
                break;
            case "server/activate":
                ApplyActivation(json);
                break;
            case "group/update":
            case "server/state":
                break; // metadata for future UI phases
            default:
                break; // unknown types ignored
        }
    }

    private void HandleServerTime(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var payload = doc.RootElement.GetProperty("payload");
        long clientTransmitted = payload.GetProperty("client_transmitted").GetInt64();
        long serverReceived = payload.GetProperty("server_received").GetInt64();
        long serverTransmitted = payload.GetProperty("server_transmitted").GetInt64();
        long nowUs = NowUs;

        long offset = ((serverReceived - clientTransmitted)
            + (serverTransmitted - nowUs)) / 2;
        long delay = ((nowUs - clientTransmitted)
            - (serverTransmitted - serverReceived)) / 2;
        _timeFilter.Update(offset, delay, nowUs);

        if (_timeFilter.IsSynchronized && _status == SendspinStatus.Synchronizing)
            SetStatus(SendspinStatus.Streaming);
    }

    private void HandleStreamStart(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var payload = doc.RootElement.GetProperty("payload");
        if (!payload.TryGetProperty("player", out var player))
            return; // artwork/visualizer-only stream — no audio for us
        string codec = player.GetProperty("codec").GetString() ?? "pcm";
        if (codec != "pcm")
        {
            _lastError = $"unsupported codec '{codec}' (PCM only in v1)";
            _streamActive = false;
            return;
        }
        _streamSampleRate = player.GetProperty("sample_rate").GetInt32();
        _streamChannels = player.GetProperty("channels").GetInt32();
        _streamBitDepth = player.GetProperty("bit_depth").GetInt32();
        _streamActive = true;

        // Reset or reconfigure the audio sink to the negotiated format.
        _sink?.Dispose();
        _sink = new AudioSinkLinux(_streamSampleRate, _streamChannels, _streamBitDepth);
        lock (_playTimes)
        {
            _jitterBuffer.Clear();
            _playTimes.Clear();
        }
    }

    // ═══════════════════════════════════════════════════════
    // Audio chunk handling (type-4 binary frames)
    // ═══════════════════════════════════════════════════════

    private void HandleAudioChunk(byte[] plaintext)
    {
        if (!_streamActive || plaintext.Length < BinaryHeaderSize) return;
        if (plaintext[0] != AudioChunkType) return;

        long timestamp = BinaryPrimitivesReadInt64BE(plaintext, 1);
        // send_ahead (bytes 9-12, uint32 BE) — delay measurement only; not
        // used for scheduling (spec: MUST NOT affect play time).

        long playTime = ComputePlayTime(timestamp);
        long now = NowUs;
        // Late drop: the chunk's start already passed the output window.
        if (playTime < now - JitterBufferMs * 1000L)
        {
            _jitterBuffer.TryRemove(playTime, out _);
            return;
        }

        var chunk = new ScheduledChunk
        {
            PlayTimeUs = playTime,
            Pcm = plaintext[BinaryHeaderSize..],
        };
        lock (_playTimes)
        {
            _jitterBuffer[playTime] = chunk;
            _playTimes.Enqueue(playTime);
        }
    }

    /// <summary>
    /// Server timestamp → local play time, with the output delay applied.
    /// Unsynced fallback adds a safe lead so the first chunks buffer.
    /// </summary>
    private long ComputePlayTime(long serverTimestampUs)
    {
        if (_timeFilter.IsSynchronized)
            return _timeFilter.ComputeClientTime(serverTimestampUs) - OutputDelayMs * 1000L;
        return NowUs + UnsyncedPlayLeadUs - OutputDelayMs * 1000L;
    }

    private static long BinaryPrimitivesReadInt64BE(byte[] data, int offset)
    {
        long value = 0;
        for (int i = 0; i < 8; i++)
            value = (value << 8) | data[offset + i];
        return value;
    }

    // ═══════════════════════════════════════════════════════
    // Background loops
    // ═══════════════════════════════════════════════════════

    private async Task ReceiveLoop(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && _ws?.State == WebSocketState.Open)
            {
                var (isJson, plaintext) = await ReceiveFrame(ct);
                if (isJson)
                    HandleServerMessage(Encoding.UTF8.GetString(plaintext));
                else if (plaintext.Length > 0 && plaintext[0] == AudioChunkType)
                    HandleAudioChunk(plaintext);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Fail($"receive loop: {ex.Message}");
        }
    }

    private async Task TimeSyncLoop(CancellationToken ct)
    {
        // Burst strategy: 8 exchanges back-to-back, then repeat every 10 s.
        // (MA also sends server/time roughly every 450 ms on its own.)
        try
        {
            while (!ct.IsCancellationRequested)
            {
                for (int i = 0; i < TimeSyncBurstCount && !ct.IsCancellationRequested; i++)
                {
                    await SendJson(BuildClientTime(NowUs), ct);
                    try { await Task.Delay(150, ct); }
                    catch (OperationCanceledException) { }
                }
                try { await Task.Delay(TimeSyncBurstInterval, ct); }
                catch (OperationCanceledException) { }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Fail($"time sync loop: {ex.Message}");
        }
    }

    private async Task PlaybackLoop(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                long? next = null;
                lock (_playTimes)
                {
                    // Peek earliest play time (queue is ordered by arrival, but
                    // play times from the filter are near-ordered; sort to be safe).
                    long earliest = long.MaxValue;
                    foreach (var t in _playTimes)
                        if (t < earliest) earliest = t;
                    if (earliest != long.MaxValue && _jitterBuffer.ContainsKey(earliest))
                        next = earliest;
                }

                if (next == null)
                {
                    await Task.Delay(5, ct);
                    continue;
                }

                long playTime = next.Value;
                long now = NowUs;
                long waitUs = playTime - now;
                if (waitUs > 2_000)
                {
                    int waitMs = (int)(waitUs / 1000);
                    await Task.Delay(Math.Min(waitMs, 50), ct);
                    continue;
                }

                // Due (or late within tolerance): play now.
                if (_jitterBuffer.TryRemove(playTime, out var chunk))
                {
                    DrainQueueExcept(playTime);
                    _sink?.Write(chunk.Pcm, 0, chunk.Pcm.Length);
                }
                else
                {
                    // Stale queue entry no longer in the buffer — drop.
                    lock (_playTimes) { _playTimes.Clear(); }
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Fail($"playback loop: {ex.Message}");
        }
    }

    private void DrainQueueExcept(long keep)
    {
        // Rebuild the queue from live buffer entries to avoid unbounded growth.
        lock (_playTimes)
        {
            _playTimes.Clear();
            foreach (var key in _jitterBuffer.Keys)
                _playTimes.Enqueue(key);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _cts?.Cancel(); } catch { }
        try { _ws?.Dispose(); } catch { }
        _transport?.Dispose();
        _sink?.Dispose();
        _sink = null;
    }
}