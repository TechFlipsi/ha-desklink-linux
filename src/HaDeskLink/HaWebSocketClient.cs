
// HA DeskLink - Home Assistant Companion App
// Copyright (C) 2026 Fabian Kirchweger
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License v3 as published by
// the Free Software Foundation.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
// GNU General Public License for more details.
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace HaDeskLink;

/// <summary>
/// WebSocket client for HA push notifications (same protocol as Windows version).
/// </summary>
public class HaWebSocketClient : IDisposable
{
    private readonly string _haUrl;
    private readonly string _token;
    private readonly string _webhookId;
    private readonly Action<string>? _onNotification;
    private ClientWebSocket? _ws;
    private CancellationTokenSource? _cts;

    public HaWebSocketClient(string haUrl, string token, string webhookId, Action<string>? onNotification = null)
    {
        _haUrl = haUrl;
        _token = token;
        _webhookId = webhookId;
        _onNotification = onNotification;
    }

    public async Task ConnectAsync()
    {
        _cts = new CancellationTokenSource();
        await ConnectWithRetryAsync(_cts.Token);
    }

    private async Task ConnectWithRetryAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                _ws = new ClientWebSocket();
                var wsUrl = _haUrl.Replace("https://", "wss://").Replace("http://", "ws://") + "/api/websocket";
                await _ws.ConnectAsync(new Uri(wsUrl), ct);

                // Auth handshake
                var buffer = new byte[8192];
                var result = await _ws.ReceiveAsync(buffer, ct);
                var authMsg = Encoding.UTF8.GetString(buffer, 0, result.Count);

                var authPayload = JsonSerializer.Serialize(new { type = "auth", access_token = _token });
                var authBytes = Encoding.UTF8.GetBytes(authPayload);
                await _ws.SendAsync(authBytes, WebSocketMessageType.Text, true, ct);

                // Read auth result
                result = await _ws.ReceiveAsync(buffer, ct);

                // Subscribe to push notifications
                var subPayload = JsonSerializer.Serialize(new
                {
                    type = "mobile_app/push_notification_channel",
                    webhook_id = _webhookId
                });
                var subBytes = Encoding.UTF8.GetBytes(subPayload);
                await _ws.SendAsync(subBytes, WebSocketMessageType.Text, true, ct);

                Console.WriteLine("[WebSocket] Connected to Home Assistant");

                // Listen for messages
                while (_ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
                {
                    try
                    {
                        result = await _ws.ReceiveAsync(buffer, ct);
                        if (result.MessageType == WebSocketMessageType.Close) break;

                        var msg = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        HandleMessage(msg);
                    }
                    catch { break; }
                }
            }
            catch
            {
                // Connection failed, retry after delay
            }

            if (!ct.IsCancellationRequested)
            {
                Console.WriteLine("[WebSocket] Reconnecting in 30 seconds...");
                await Task.Delay(TimeSpan.FromSeconds(30), ct);
            }
        }
    }

    private void HandleMessage(string message)
    {
        try
        {
            var doc = JsonDocument.Parse(message);
            var type = doc.RootElement.GetProperty("type").GetString();

            if (type == "event")
            {
                var data = doc.RootElement.GetProperty("data");
                if (data.TryGetProperty("message", out var msg))
                {
                    var title = data.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                    var text = msg.GetString() ?? "";
                    _onNotification?.Invoke($"{title}\n{text}");

                    // Handle commands
                    if (data.TryGetProperty("command", out var cmd))
                    {
                        CommandHandler.Execute(cmd.GetString() ?? "");
                    }
                }
            }
        }
        catch { }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _ws?.Dispose();
    }
}