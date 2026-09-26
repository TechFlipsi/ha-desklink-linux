
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
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
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

    /// <summary>Lädt das Tray-Icon (weißes Haus, gut sichtbar auf dunklen Leisten) aus den Ressourcen.</summary>
    private static WindowIcon? LoadTrayIcon()
    {
        try
        {
            using var stream = AssetLoader.Open(new Uri("avares://HaDeskLink/Assets/icon-tray.png"));
            using var bitmap = new Bitmap(stream);
            return new WindowIcon(bitmap);
        }
        catch
        {
            try
            {
                using var stream = AssetLoader.Open(new Uri("avares://HaDeskLink/Assets/icon.png"));
                using var bitmap = new Bitmap(stream);
                return new WindowIcon(bitmap);
            }
            catch { return null; }
        }
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

            // Tray-Icon in der Systemleiste (Windows-Vision, wie Nextcloud/die Windows-Version):
            // Klick = Fenster zeigen; Menü: Öffnen / Beenden
            var mainWindow = (MainWindow)desktop.MainWindow;
            try
            {
            var trayIcon = new Avalonia.Controls.TrayIcon
            {
                Icon = LoadTrayIcon(),
                ToolTipText = "HA DeskLink",
                IsVisible = true
            };
            var trayMenu = new NativeMenu();
            var miOpen = new NativeMenuItem("Öffnen");
            miOpen.Click += (s, e) =>
            {
                mainWindow.Show();
                mainWindow.Activate();
            };
            var miDashboard = new NativeMenuItem("Dashboard anzeigen");
            miDashboard.Click += (s, e) =>
            {
                mainWindow.Show();
                mainWindow.Activate();
                mainWindow.ShowDashboard(CurrentConfig?.HaUrl ?? "");
            };
            var miExit = new NativeMenuItem("Beenden");
            miExit.Click += (s, e) => mainWindow.ReallyExit();
            trayMenu.Items.Add(miOpen);
            trayMenu.Items.Add(miDashboard);
            trayMenu.Items.Add(new NativeMenuItemSeparator());
            trayMenu.Items.Add(miExit);
            trayIcon.Menu = trayMenu;
            trayIcon.Clicked += (s, e) =>
            {
                mainWindow.Show();
                mainWindow.Activate();
            };
            // TrayIcons via property auf dem App-Objekt (nicht Window)
            if (Avalonia.Controls.TrayIcon.GetIcons(this) is TrayIcons icons)
                icons.Add(trayIcon);
            else
                TrayIcon.SetIcons(this, new TrayIcons { trayIcon });
            }
            catch (Exception exTray)
            {
                Console.WriteLine($"[HA DeskLink] Tray-Icon nicht verfügbar: {exTray.Message}");
            }

            // Desktop-Widgets starten (falls konfiguriert)
            try { WidgetManager.Start(CurrentConfig); } catch { }
            // Sendspin-Streaming-Client starten (falls konfiguriert)
            try { Sendspin.SendspinManager.Start(CurrentConfig); } catch { }
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