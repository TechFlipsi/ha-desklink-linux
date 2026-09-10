// HA DeskLink - Sendspin streaming client (Phase E)
// Noise KKpsk2 (25519_ChaChaPoly_SHA256) handshake, client = responder.
// The server is the initiator: it sends Noise message 1; we resolve the
// PSK from the psk_id carried inside message 1 (Sentinel fallback), mix it
// in, and answer with message 2. Ports aiosendspin's noise/session.py +
// noise/driver.py (client side) onto Noise.NET.
//
// KKpsk2 message flow (responder view):
//   msg 1 (server → us): e, es, ss  + AEAD body {"psk_id": ...} — no PSK used
//   msg 2 (us → server): e, ee, se, psk + AEAD body {} — PSK mixed here
// Because message 1 never touches the PSK, we can decrypt it with any
// 32-byte placeholder to learn psk_id, then re-create the handshake state
// with the resolved PSK and re-read message 1 — equivalent to Python's
// session.mix_psk() between reading message 1 and writing message 2.
//
// This program is licensed under the GNU General Public License v3.
#nullable enable
using System;

using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Noise;

namespace HaDeskLink.Sendspin;

/// <summary>Thrown when the Noise handshake cannot complete.</summary>
public sealed class HandshakeAbortedException : Exception
{
    public HandshakeAbortedException(string message) : base(message) { }
}

/// <summary>
/// X25519 static identity — the Sendspin client_id is the base64url public key.
/// Noise.NET generates random keypairs (KeyPair.Generate) and derives the
/// public key from the private key internally when a handshake state is created
/// with <c>s</c>, so both keys are carried together.
/// </summary>
public sealed class SendspinIdentity
{
    public byte[] PrivateKey { get; }
    public byte[] PublicKey { get; }

    private SendspinIdentity(byte[] privateKey, byte[] publicKey)
    {
        PrivateKey = privateKey;
        PublicKey = publicKey;
    }

    /// <summary>Generate a fresh random identity.</summary>
    public static SendspinIdentity Generate()
    {
        using var kp = KeyPair.Generate();
        return new SendspinIdentity((byte[])kp.PrivateKey.Clone(), (byte[])kp.PublicKey.Clone());
    }

    /// <summary>
    /// Reconstruct an identity from stored keys (both halves persisted; the
    /// public key cannot be derived through Noise.NET's public API).
    /// </summary>
    public static SendspinIdentity FromKeys(byte[] privateKey, byte[] publicKey)
    {
        if (privateKey.Length != NoiseConstants.X25519KeySize)
            throw new ArgumentException($"X25519 private key must be {NoiseConstants.X25519KeySize} bytes");
        if (publicKey.Length != NoiseConstants.X25519KeySize)
            throw new ArgumentException($"X25519 public key must be {NoiseConstants.X25519KeySize} bytes");
        return new SendspinIdentity((byte[])privateKey.Clone(), (byte[])publicKey.Clone());
    }

    /// <summary>The base64url public key — the Sendspin client_id (43 chars, no padding).</summary>
    public string PeerId => Base64Url.Encode(PublicKey);
}


/// <summary>base64url helpers matching aiosendspin's noise/keys.py.</summary>
internal static class Base64Url
{
    public static string Encode(byte[] data)
    {
        return Convert.ToBase64String(data)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    public static byte[] Decode(string value)
    {
        string b64 = value.Replace('-', '+').Replace('_', '/');
        int pad = (4 - b64.Length % 4) % 4;
        return Convert.FromBase64String(b64 + new string('=', pad));
    }
}

/// <summary>
/// Noise KKpsk2 constants and the Sentinel PSK fallback (aiosendspin noise/constants.py).
/// </summary>
internal static class NoiseConstants
{
    public const int ProtocolVersion = 1;
    public const int PskSize = 32;
    public const int X25519KeySize = 32;
    public const int PeerIdSize = 43;

    /// <summary>Published constant PSK used when no other PSK applies (authenticates nothing).</summary>
    public static readonly byte[] SentinelPsk =
        SHA256.HashData(Encoding.UTF8.GetBytes("sendspin-sentinel-psk-v1"));

    private static readonly byte[] PskIdLabel = Encoding.UTF8.GetBytes("sendspin-psk-id-v1");

    /// <summary>psk_id = base64url(SHA256("sendspin-psk-id-v1" || psk)).</summary>
    public static string PskIdFor(byte[] psk)
    {
        if (psk.Length != PskSize)
            throw new ArgumentException($"PSK must be {PskSize} bytes, got {psk.Length}");
        var input = new byte[PskIdLabel.Length + psk.Length];
        PskIdLabel.CopyTo(input, 0);
        psk.CopyTo(input, PskIdLabel.Length);
        return Base64Url.Encode(SHA256.HashData(input));
    }

    public static readonly string SentinelPskId = PskIdFor(SentinelPsk);

    // Transport-mode plaintext framing (aiosendspin noise/wire.py).
    public const byte MsgTypeJsonBody = 0;
    public const byte MsgTypeFragmentMore = 2;
    public const byte MsgTypeFragmentEnd = 3;
    public const int MaxTransportPlaintext = 65535 - 16;
    public const int MaxReassembledMessageBytes = 64 * 1024 * 1024;
}

/// <summary>Result of a successful Noise handshake.</summary>
public sealed class HandshakeResult
{
    public required NoiseTransport Transport { get; init; }
    public required string ServerId { get; init; }
    public required byte[] HandshakeHash { get; init; }
}

/// <summary>
/// Post-handshake Noise transport: encrypts/decrypts WS binary frames with the
/// Sendspin plaintext framing (type byte 0 = JSON; fragments reassembled).
/// </summary>
public sealed class NoiseTransport : IDisposable
{
    private readonly Transport _transport;
    private readonly byte[] _buffer = new byte[Protocol.MaxMessageLength];
    // Fragment reassembly state (server → client oversized messages).
    private System.Collections.Generic.List<byte>? _reasmBuf;
    private byte _reasmType;
    private bool _disposed;

    internal NoiseTransport(Transport transport) => _transport = transport;

    /// <summary>Encrypt a JSON control body into a WS binary frame (type byte 0).</summary>
    public byte[] EncryptJson(string json)
    {
        var plaintext = new byte[1 + Encoding.UTF8.GetByteCount(json)];
        plaintext[0] = NoiseConstants.MsgTypeJsonBody;
        Encoding.UTF8.GetBytes(json, plaintext.AsSpan(1));
        return Encrypt(plaintext);
    }

    /// <summary>Encrypt a single transport frame from type-prefixed plaintext.</summary>
    private byte[] Encrypt(byte[] plaintext)
    {
        if (plaintext.Length > NoiseConstants.MaxTransportPlaintext)
            throw new InvalidOperationException(
                "plaintext exceeds single-frame Noise transport limit");
        int written = _transport.WriteMessage(plaintext, _buffer);
        return _buffer.AsSpan(0, written).ToArray();
    }

    /// <summary>
    /// Decrypt one WS binary frame. Returns (isJson, plaintext) for complete
    /// messages, or null while fragments of an oversized message are still in
    /// flight. Throws <see cref="HandshakeAbortedException"/> on protocol errors.
    /// </summary>
    public (bool IsJson, byte[] Plaintext)? Decrypt(byte[] ciphertext)
    {
        int read;
        try
        {
            read = _transport.ReadMessage(ciphertext, _buffer);
        }
        catch (CryptographicException)
        {
            throw new HandshakeAbortedException("Noise transport frame failed authentication");
        }
        if (read <= 0)
            throw new HandshakeAbortedException("empty plaintext after Noise decrypt");

        byte typeByte = _buffer[0];
        switch (typeByte)
        {
            case NoiseConstants.MsgTypeJsonBody:
                FailIfFragmentInFlight("non-fragment frame");
                return (true, _buffer.AsSpan(1, read - 1).ToArray());

            case NoiseConstants.MsgTypeFragmentMore:
                if (_reasmBuf == null)
                {
                    if (read < 2)
                        throw new HandshakeAbortedException("fragment-more start frame missing orig_type");
                    _reasmType = _buffer[1];
                    _reasmBuf = new System.Collections.Generic.List<byte>(_buffer.AsSpan(2, read - 2).ToArray());
                }
                else
                {
                    AppendFragment(_buffer.AsSpan(1, read - 1));
                }
                return null;

            case NoiseConstants.MsgTypeFragmentEnd:
                if (_reasmBuf == null)
                    throw new HandshakeAbortedException("fragment-end frame with no fragmented message in flight");
                AppendFragment(_buffer.AsSpan(1, read - 1));
                var reassembled = new byte[1 + _reasmBuf.Count];
                reassembled[0] = _reasmType;
                _reasmBuf.CopyTo(reassembled, 1);
                bool isJson = _reasmType == NoiseConstants.MsgTypeJsonBody;
                _reasmBuf = null;
                return isJson
                    ? (true, reassembled.AsSpan(1).ToArray())
                    : (false, reassembled);

            default:
                FailIfFragmentInFlight("non-fragment frame");
                return (false, _buffer.AsSpan(0, read).ToArray());
        }
    }

    private void AppendFragment(ReadOnlySpan<byte> data)
    {
        if (_reasmBuf!.Count + data.Length > NoiseConstants.MaxReassembledMessageBytes)
        {
            _reasmBuf = null;
            throw new HandshakeAbortedException("fragmented message exceeds maximum reassembly size");
        }
        _reasmBuf.AddRange(data.ToArray());
    }

    private void FailIfFragmentInFlight(string what)
    {
        if (_reasmBuf != null)
        {
            _reasmBuf = null;
            throw new HandshakeAbortedException($"{what} while a fragmented message is in flight");
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _transport.Dispose();
            _disposed = true;
        }
    }
}

/// <summary>
/// Drives the client-side (responder) Noise KKpsk2 handshake over the raw
/// WebSocket text-frame exchange, exactly as aiosendspin's driver.py does:
/// client/init → server/init → noise/handshake (msg 1 from server carries
/// psk_id) → noise/handshake (msg 2 from us).
/// </summary>
public sealed class NoiseHandshake
{
    private static readonly Protocol Protocol = new(
        HandshakePattern.KK,
        CipherFunction.ChaChaPoly,
        HashFunction.Sha256,
        PatternModifiers.Psk2);

    /// <summary>
    /// Run the responder handshake. <paramref name="sendText"/> sends a WS text
    /// frame; <paramref name="receiveText"/> awaits the next WS text frame.
    /// </summary>
    public static HandshakeResult RunResponder(
        SendspinIdentity identity,
        Func<string, Task> sendText,
        Func<Task<string>> receiveText)
    {
        // ── cleartext client/init ──
        string clientInit = "{\"payload\":{\"client_id\":\"" + identity.PeerId + "\","
            + "\"version\":" + NoiseConstants.ProtocolVersion + ","
            + "\"suite\":\"25519_ChaChaPoly_SHA256\"},\"type\":\"client/init\"}";
        sendText(clientInit).GetAwaiter().GetResult();

        // ── cleartext server/init ──
        string serverInitText = receiveText().GetAwaiter().GetResult();
        string serverId = ParseServerInit(serverInitText);
        byte[] serverStaticPub = PeerPubBytes(serverId, "server_id");

        // Prologue binds the handshake to the exact init frames on both sides.
        byte[] prologue = Encoding.UTF8.GetBytes(clientInit + serverInitText);
        var buffer = new byte[Protocol.MaxMessageLength];

        // ── Noise message 1 (server → client), carried in noise/handshake ──
        string hs1Text = receiveText().GetAwaiter().GetResult();
        byte[] hs1Ciphertext = ParseHandshakeFrame(hs1Text, "Noise message 1");

        // Pass 1: message 1's AEAD does not involve the PSK (psk2 applies to
        // message 2), so a placeholder-PSK state decrypts it — enough to read
        // the psk_id from its plaintext body.
        string pskId;
        using (var probe = Protocol.Create(
            initiator: false,
            prologue: prologue,
            s: identity.PrivateKey,
            rs: serverStaticPub,
            psks: new[] { new byte[NoiseConstants.PskSize] }))
        {
            var (probeRead, _, probeTransport) = probe.ReadMessage(hs1Ciphertext, buffer);
            if (probeTransport != null)
                throw new HandshakeAbortedException("unexpected handshake completion at message 1");
            pskId = ParseMsg1PskId(buffer.AsSpan(0, probeRead));
        }

        // Resolve the PSK. V1: Sentinel PSK only (MA auto-approves unpaired access).
        if (pskId != NoiseConstants.SentinelPskId)
            throw new HandshakeAbortedException($"no PSK matches psk_id={pskId}");
        byte[] psk = NoiseConstants.SentinelPsk;

        // Pass 2: re-create the state with the real PSK, re-read message 1 to
        // advance the symmetric state, then write message 2 — the psk token is
        // consumed during WriteMessage, mixing the real PSK (mix_psk equivalent).
        using var handshake = Protocol.Create(
            initiator: false,
            prologue: prologue,
            s: identity.PrivateKey,
            rs: serverStaticPub,
            psks: new[] { psk });
        var (_, _, transport1) = handshake.ReadMessage(hs1Ciphertext, buffer);
        if (transport1 != null)
            throw new HandshakeAbortedException("unexpected handshake completion at message 1");

        // ── Noise message 2 (client → server): plaintext body is {} ──
        var (bytesWritten, handshakeHash, transport) =
            handshake.WriteMessage(Encoding.UTF8.GetBytes("{}"), buffer);
        if (transport == null)
            throw new HandshakeAbortedException("Noise handshake did not complete after message 2");
        string hs2Text = PackHandshakeFrame(buffer.AsSpan(0, bytesWritten).ToArray());
        sendText(hs2Text).GetAwaiter().GetResult();

        return new HandshakeResult
        {
            Transport = new NoiseTransport(transport),
            ServerId = serverId,
            HandshakeHash = handshakeHash,
        };
    }

    private static string ParseMsg1PskId(ReadOnlySpan<byte> plaintext)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(plaintext.ToArray());
            var pskId = doc.RootElement.GetProperty("psk_id").GetString();
            if (string.IsNullOrEmpty(pskId))
                throw new HandshakeAbortedException("Noise message 1 payload has empty psk_id");
            return pskId;
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new HandshakeAbortedException($"malformed Noise message 1 payload: {ex.Message}");
        }
    }

    private static string ParseServerInit(string text)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(text);
            var root = doc.RootElement;
            if (root.TryGetProperty("type", out var type)
                && type.GetString() != "server/init")
                throw new HandshakeAbortedException(
                    $"expected server/init, got '{type.GetString()}'");
            if (!root.TryGetProperty("payload", out var payload))
                throw new HandshakeAbortedException("malformed server/init: missing payload");
            if (!payload.TryGetProperty("server_id", out var serverIdEl))
                throw new HandshakeAbortedException("malformed server/init: missing server_id");
            if (payload.TryGetProperty("version", out var version)
                && version.GetInt32() != NoiseConstants.ProtocolVersion)
                throw new HandshakeAbortedException(
                    $"unsupported protocol version {version.GetInt32()}");
            return serverIdEl.GetString()
                ?? throw new HandshakeAbortedException("empty server_id");
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new HandshakeAbortedException($"malformed server/init: {ex.Message}");
        }
    }

    private static byte[] ParseHandshakeFrame(string text, string what)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(text);
            var root = doc.RootElement;
            if (root.TryGetProperty("type", out var type)
                && type.GetString() != "noise/handshake")
                throw new HandshakeAbortedException(
                    $"expected noise/handshake, got '{type.GetString()}'");
            if (!root.TryGetProperty("payload", out var payload)
                || !payload.TryGetProperty("data", out var data))
                throw new HandshakeAbortedException($"malformed noise/handshake ({what})");
            return Base64Url.Decode(data.GetString()
                ?? throw new HandshakeAbortedException($"malformed {what} payload encoding"));
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new HandshakeAbortedException($"malformed noise/handshake ({what}): {ex.Message}");
        }
    }

    private static string PackHandshakeFrame(byte[] noiseBytes)
        => "{\"payload\":{\"data\":\"" + Base64Url.Encode(noiseBytes)
            + "\"},\"type\":\"noise/handshake\"}";

    private static byte[] PeerPubBytes(string peerId, string what)
    {
        if (peerId.Length != NoiseConstants.PeerIdSize)
            throw new HandshakeAbortedException(
                $"invalid {what} length: {peerId.Length} (expected {NoiseConstants.PeerIdSize})");
        byte[] decoded;
        try
        {
            decoded = Base64Url.Decode(peerId);
        }
        catch (Exception)
        {
            throw new HandshakeAbortedException($"invalid {what} encoding");
        }
        if (decoded.Length != NoiseConstants.X25519KeySize)
            throw new HandshakeAbortedException(
                $"invalid {what}: decoded to {decoded.Length} bytes "
                + $"(expected {NoiseConstants.X25519KeySize})");
        return decoded;
    }
}