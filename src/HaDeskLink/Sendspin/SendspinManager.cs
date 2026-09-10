// HA DeskLink - Sendspin streaming client (Phase E)
// Lifecycle manager: start/stop/reconnect of the Sendspin player session
// driven by Config.SendspinEnabled / Config.MaHost / Config.SendspinPlayerName.
// Reconnects with exponential backoff; restarts when the user saves changed
// settings.
//
// This program is licensed under the GNU General Public License v3.
#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;

namespace HaDeskLink.Sendspin;

/// <summary>
/// Owns the SendspinClient instance and its background connection task.
/// Start/Stop are idempotent; changing settings while running restarts the
/// session.
/// </summary>
public static class SendspinManager
{
    private static readonly object Gate = new();
    private static SendspinClient? _client;
    private static CancellationTokenSource? _cts;
    private static Task? _runTask;
    private static bool _started;
    private static string _lastConfig = "";

    /// <summary>Latest status, for the Settings UI.</summary>
    public static SendspinStatus Status
    {
        get { lock (Gate) { return _client?.Status ?? SendspinStatus.Disabled; } }
    }

    public static string LastError
    {
        get { lock (Gate) { return _client?.LastError ?? ""; } }
    }

    public static bool TimeSynchronized
    {
        get { lock (Gate) { return _client?.TimeSynchronized ?? false; } }
    }

    public static long TimeErrorUs
    {
        get { lock (Gate) { return _client?.TimeErrorUs ?? 0; } }
    }

    /// <summary>
    /// Start the streaming client if enabled in config. Safe to call repeatedly
    /// (e.g. after settings changes): a running session with unchanged config
    /// is left alone; changed config restarts it.
    /// </summary>
    public static void Start(Config? config)
    {
        if (config == null) return;
        lock (Gate)
        {
            string configKey = $"{config.SendspinEnabled}|{config.MaHost}|{config.SendspinPlayerName}";
            if (_started && configKey == _lastConfig)
                return; // nothing changed
            _lastConfig = configKey;

            StopLocked();

            if (!config.SendspinEnabled || string.IsNullOrEmpty(config.MaHost))
                return;

            _started = true;
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;
            var client = new SendspinClient(config.MaHost, config.SendspinPort, config.SendspinPlayerName);
            _client = client;
            _runTask = Task.Run(async () =>
            {
                try
                {
                    await RunWithReconnectAsync(client, ct);
                }
                catch
                {
                    // Logged through the client status; never crash the app.
                }
            }, ct);
        }
    }

    /// <summary>
    /// Keep the session alive with exponential backoff until cancelled.
    /// </summary>
    private static async Task RunWithReconnectAsync(SendspinClient client, CancellationToken ct)
    {
        int backoffSeconds = 1;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await client.RunAsync(ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            if (ct.IsCancellationRequested) return;

            // Session ended (server drop / error): wait, then reconnect fresh.
            await Task.Delay(TimeSpan.FromSeconds(backoffSeconds), ct);
            backoffSeconds = Math.Min(backoffSeconds * 2, 60);
            if (client.Status != SendspinStatus.Error)
            {
                // Graceful stop (app shutdown) — don't reconnect.
                return;
            }
        }
    }

    /// <summary>
    /// Stop the streaming session. Called on app shutdown and on disable.
    /// </summary>
    public static void Stop()
    {
        lock (Gate)
        {
            StopLocked();
            _started = false;
            _lastConfig = "";
        }
    }

    private static void StopLocked()
    {
        try { _cts?.Cancel(); } catch { }
        try { _client?.Dispose(); } catch { }
        _cts?.Dispose();
        _cts = null;
        _client = null;
        _runTask = null;
    }
}