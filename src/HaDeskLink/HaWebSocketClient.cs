
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
using System.Linq;
using System.Diagnostics;

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
                string title = "HA DeskLink";
                string text = "";
                string? command = null;
                List<NotificationAction>? actions = null;
                string? commandOnAction = null;

                if (data.TryGetProperty("title", out var t))
                    title = t.GetString() ?? "";
                if (data.TryGetProperty("message", out var msg))
                    text = msg.GetString() ?? "";
                if (data.TryGetProperty("command", out var cmd))
                    command = cmd.GetString();
                if (data.TryGetProperty("command_on_action", out var coa))
                    commandOnAction = coa.GetString();
                if (data.TryGetProperty("actions", out var actionsArr))
                {
                    actions = new List<NotificationAction>();
                    foreach (var a in actionsArr.EnumerateArray())
                    {
                        var act = a.GetProperty("action").GetString() ?? "";
                        var actTitle = a.TryGetProperty("title", out var at) ? at.GetString() ?? act : act;
                        var actCommand = a.TryGetProperty("command", out var ac) ? ac.GetString() : null;
                        actions.Add(new NotificationAction(act, actTitle, actCommand));
                    }
                }

                // Log notification
                if (!string.IsNullOrEmpty(text))
                    Console.WriteLine($"[Notification] {title}: {text}");

                // Execute command if present (no action buttons)
                if (!string.IsNullOrEmpty(command) && actions == null)
                {
                    CommandHandler.Execute(command);
                }

                // Handle actionable notifications - execute command_on_action for first action
                // (Linux daemon has no UI for buttons - auto-execute default action)
                if (actions != null && actions.Count > 0 && !string.IsNullOrEmpty(commandOnAction))
                {
                    Console.WriteLine($"[Action] Auto-executing: {commandOnAction}");
                    CommandHandler.Execute(commandOnAction);
                }

                // Send desktop notification via notify-send if available
                if (!string.IsNullOrEmpty(text))
                {
                    try
                    {
                        var actionHint = actions != null && actions.Count > 0
                            ? $"\nAktionen: {string.Join(", ", actions.Select(a => a.Title))}"
                            : "";
                        var psi = new ProcessStartInfo("notify-send", $"\"{title}\" \"{text}{actionHint}\"")
                        {
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };
                        Process.Start(psi)?.WaitForExit(3000);
                    }
                    catch { /* notify-send not available */ }
                }

                _onNotification?.Invoke($"{title}\n{text}");
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

public class NotificationAction
{
    public string ActionKey { get; }
    public string Title { get; }
    public string? Command { get; }

    public NotificationAction(string actionKey, string title, string? command = null)
    {
        ActionKey = actionKey;
        Title = title;
        Command = command;
    }
}