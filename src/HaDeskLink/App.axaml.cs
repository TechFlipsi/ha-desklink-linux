
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
using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using AvaloniaWebView;
using HaDeskLink.Views;

namespace HaDeskLink;

public class App : Application
{
    public static Config? CurrentConfig { get; private set; }
    private HaWebSocketClient? _wsClient;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void RegisterServices()
    {
        base.RegisterServices();
        AvaloniaWebViewBuilder.Initialize(default);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        CurrentConfig = Config.Load();

        // Sprache beim App-Start laden
        if (CurrentConfig != null)
            Localization.LoadLanguage(CurrentConfig.Language);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
            desktop.MainWindow.Title = $"HA DeskLink Linux v{HaApiClient.GetVersion()}";
            if (CurrentConfig != null)
            {
                ((MainWindow)desktop.MainWindow).HaUrl = CurrentConfig.HaUrl;
            }

            // WebSocket für Push-Benachrichtigungen (nur wenn eine Registrierung existiert).
            // GUI-Modus zeigt echte Avalonia-Toasts mit Aktions-Buttons (NotificationHandler).
            try
            {
                var config = CurrentConfig;
                if (config != null && !string.IsNullOrEmpty(config.HaToken))
                {
                    var api = new HaApiClient(Config.GetConfigDir(), config.VerifySsl);
                    var webhookId = api.GetWebhookId();
                    if (!string.IsNullOrEmpty(webhookId))
                    {
                        _wsClient = new HaWebSocketClient(config.HaUrl, config.HaToken, webhookId,
                            msg => Console.WriteLine($"[HA DeskLink] Notification: {msg}"),
                            isBlocked: () => api.IsBlocked,
                            verifySsl: config.VerifySsl,
                            onRawNotification: json =>
                            {
                                // Auf dem UI-Thread als Toast anzeigen
                                Dispatcher.UIThread.Post(() => NotificationHandler.TryHandleNotification(json));
                            });
                        _ = _wsClient.ConnectAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HA DeskLink] WebSocket init failed: {ex.Message}");
            }

            desktop.Exit += (s, e) => _wsClient?.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }
}