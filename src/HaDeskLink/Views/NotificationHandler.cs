// HA DeskLink - Home Assistant Companion App
// Copyright (C) 2026 Fabian Kirchweger
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License v3 as published
// by the Free Software Foundation.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
// GNU General Public License for more details.
#nullable enable
using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.Text.Json;
using HaDeskLink.Views;

namespace HaDeskLink;

/// <summary>
/// Handles notifications from Home Assistant with modern dark-themed toasts.
/// Avalonia port of the Windows NotificationHandler — parses the same notification
/// JSON schema (title/message/command/actions/command_on_action) and shows
/// NotificationPopup toasts on the UI thread.
/// </summary>
public static class NotificationHandler
{
    /// <summary>
    /// Try to parse a HA notification JSON and show a toast. Returns true when handled.
    /// </summary>
    public static bool TryHandleNotification(string jsonBody)
    {
        try
        {
            var doc = JsonDocument.Parse(jsonBody);
            var root = doc.RootElement;

            string title = "HA DeskLink";
            string message = "";
            string? command = null;
            List<NotificationActionInfo>? actions = null;
            string? commandOnAction = null;

            if (root.TryGetProperty("title", out var t1)) title = t1.GetString() ?? title;
            if (root.TryGetProperty("message", out var m1)) message = m1.GetString() ?? "";
            if (root.TryGetProperty("command", out var c1)) command = c1.GetString();

            if (root.TryGetProperty("data", out var data))
            {
                if (data.TryGetProperty("title", out var t2)) title = t2.GetString() ?? title;
                if (data.TryGetProperty("message", out var m2)) message = m2.GetString() ?? message;
                if (data.TryGetProperty("command", out var c2)) command = c2.GetString();
                if (data.TryGetProperty("command_on_action", out var coa)) commandOnAction = coa.GetString();
                if (data.TryGetProperty("actions", out var actionsArr))
                {
                    actions = new List<NotificationActionInfo>();
                    foreach (var a in actionsArr.EnumerateArray())
                    {
                        var act = a.GetProperty("action").GetString() ?? "";
                        var actTitle = a.TryGetProperty("title", out var at) ? at.GetString() ?? act : act;
                        var actCommand = a.TryGetProperty("command", out var ac) ? ac.GetString() : null;
                        var finalCommand = actCommand;
                        var fallbackCommand = commandOnAction;
                        actions.Add(new NotificationActionInfo(act, actTitle, () =>
                        {
                            if (!string.IsNullOrEmpty(finalCommand))
                            {
                                try { CommandHandler.Execute(finalCommand!); }
                                catch (Exception ex) { Console.WriteLine($"[Notification] Action command error: {ex.Message}"); }
                            }
                            else if (!string.IsNullOrEmpty(fallbackCommand))
                            {
                                try { CommandHandler.Execute(fallbackCommand!); }
                                catch (Exception ex) { Console.WriteLine($"[Notification] Fallback command error: {ex.Message}"); }
                            }
                        }));
                    }
                }
            }

            if (!string.IsNullOrEmpty(command))
            {
                try { CommandHandler.Execute(command!); }
                catch (Exception ex) { Console.WriteLine($"[Notification] Command error: {ex.Message}"); }
            }

            if (!string.IsNullOrEmpty(message))
            {
                ShowOnUiThread(() =>
                {
                    NotificationPopup.ShowNotification(title, message, actions);
                });
                return true;
            }

            if (!string.IsNullOrEmpty(command)) return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Notification] Parse error: {ex.Message}");
        }
        return false;
    }

    /// <summary>
    /// Show a plain notification toast (no action buttons).
    /// </summary>
    public static void ShowNotification(string title, string message)
    {
        ShowOnUiThread(() => NotificationPopup.ShowNotification(title, message));
    }

    /// <summary>
    /// Show a connection status toast (used for WebSocket events) with green accent.
    /// </summary>
    public static void ShowConnectionToast(string title, string message)
    {
        ShowOnUiThread(() => NotificationPopup.ShowConnectionToast(title, message));
    }

    /// <summary>
    /// Marshals toast creation to the UI thread to prevent cross-thread exceptions
    /// (equivalent of the Windows SynchronizationContext marshalling).
    /// </summary>
    private static void ShowOnUiThread(Action createAndShow)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            createAndShow();
        }
        else
        {
            Dispatcher.UIThread.Post(() => createAndShow(), DispatcherPriority.Normal);
        }
    }
}