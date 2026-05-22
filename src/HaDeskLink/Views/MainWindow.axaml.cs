
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
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

namespace HaDeskLink.Views;

public partial class MainWindow : Window
{
    private string _haUrl = "";
    private TextBlock? _statusLabel;

    public string HaUrl
    {
        get => _haUrl;
        set { _haUrl = value; }
    }

    public MainWindow()
    {
        InitializeComponent();
        _statusLabel = this.FindControl<TextBlock>("LblStatus");

        var btnDashboard = this.FindControl<Button>("BtnDashboard");
        if (btnDashboard != null) btnDashboard.Click += OnOpenDashboard;

        var btnRefresh = this.FindControl<Button>("BtnRefresh");
        if (btnRefresh != null) btnRefresh.Click += OnRefresh;

        var btnSetup = this.FindControl<Button>("BtnSetup");
        if (btnSetup != null) btnSetup.Click += OnSetup;

        var btnReset = this.FindControl<Button>("BtnResetDevice");
        if (btnReset != null) btnReset.Click += OnResetDevice;

        var btnQuickActions = this.FindControl<Button>("BtnQuickActions");
        if (btnQuickActions != null) btnQuickActions.Click += OnQuickActions;

        var btnDiscord = this.FindControl<Button>("BtnDiscord");
        if (btnDiscord != null) btnDiscord.Click += (s, e) => OpenUrl("https://discord.gg/7G2SqpXpsC");

        var btnGitHub = this.FindControl<Button>("BtnGitHub");
        if (btnGitHub != null) btnGitHub.Click += (s, e) => OpenUrl("https://github.com/TechFlipsi/ha-desklink-linux");

        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        var config = Config.Load();
        _haUrl = config.HaUrl;
        if (_statusLabel != null)
            _statusLabel.Text = string.IsNullOrEmpty(_haUrl) ? "⚠️ Nicht verbunden" : $"✓ Verbunden: {_haUrl}";
    }

    private void OnOpenDashboard(object? sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_haUrl))
        {
            try { Process.Start(new ProcessStartInfo(_haUrl) { UseShellExecute = true }); }
            catch { }
        }
    }

    private static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { }
    }

    private void OnRefresh(object? sender, RoutedEventArgs e)
    {
        if (_statusLabel != null)
            _statusLabel.Text = "Sensoren aktualisiert ✓";
    }

    private async void OnSetup(object? sender, RoutedEventArgs e)
    {
        var config = Config.Load();
        var urlBox = new TextBox { Text = config.HaUrl, Watermark = "https://homeassistant.local:8123" };
        var tokenBox = new TextBox { Text = config.HaToken, Watermark = "Long-Lived Token", PasswordChar = '•' };
        var sslCheck = new CheckBox { Content = "SSL-Zertifikat prüfen", IsChecked = config.VerifySsl };

        var statusText = new TextBlock
        {
            Text = "Verbinde deinen Linux-PC mit Home Assistant",
            Foreground = Avalonia.Media.Brushes.Gray,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        };

        var connectBtn = new Button { Content = "Verbinden", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center };
        var retryBtn = new Button { Content = "🔄 Erneut versuchen", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, IsVisible = false };
        var buttonPanel = new StackPanel { Spacing = 8, Children = { connectBtn, retryBtn } };

        var dialog = new Window
        {
            Title = "HA DeskLink – Einrichtung",
            Width = 450,
            Height = 380,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(20),
                Spacing = 10,
                Children =
                {
                    new TextBlock { Text = "HA DeskLink Setup", FontSize = 18, FontWeight = Avalonia.Media.FontWeight.Bold },
                    statusText,
                    new TextBlock { Text = "HA URL:" },
                    urlBox,
                    new TextBlock { Text = "Long-Lived Token:" },
                    tokenBox,
                    sslCheck,
                    new TextBlock { Text = "Token: HA → Profil → Sicherheit → Long-Lived Access Tokens", FontSize = 11, Foreground = Avalonia.Media.Brushes.Gray },
                    buttonPanel
                }
            }
        };

        // Shared API client to track retry count across attempts
        var api = new HaApiClient(Config.GetConfigDir(), sslCheck.IsChecked ?? false);
        api.LoadRegistration();

        connectBtn.Click += async (s, args) =>
        {
            connectBtn.IsEnabled = false;
            retryBtn.IsVisible = false;
            connectBtn.Content = "Verbindet...";
            try
            {
                await api.RegisterAsync(urlBox.Text?.Trim() ?? "", tokenBox.Text?.Trim() ?? "");
                config.HaUrl = urlBox.Text?.Trim() ?? "";
                config.HaToken = tokenBox.Text?.Trim() ?? "";
                config.VerifySsl = sslCheck.IsChecked ?? false;
                config.Save();
                _haUrl = config.HaUrl;
                if (_statusLabel != null) _statusLabel.Text = $"✓ Verbunden: {_haUrl}";
                dialog.Close();
            }
            catch (Exception ex)
            {
                var message = ex.Message;
                if (ex is InvalidOperationException && message.Contains("Login fehlgeschlagen"))
                {
                    statusText.Text = message;
                    statusText.Foreground = Avalonia.Media.Brushes.Red;
                    connectBtn.IsVisible = false;
                    retryBtn.IsVisible = true;
                }
                else if (api.IsBlocked)
                {
                    statusText.Text = "Login fehlgeschlagen. Token ungültig. Bitte überprüfe deinen Home Assistant Token in den Einstellungen.";
                    statusText.Foreground = Avalonia.Media.Brushes.Red;
                    connectBtn.IsVisible = false;
                    retryBtn.IsVisible = true;
                }
                else
                {
                    statusText.Text = $"✗ Fehler ({api.FailedLoginAttempts}/{HaApiClient.MaxFailedLoginAttempts}): {ex.Message}";
                    statusText.Foreground = Avalonia.Media.Brushes.Red;
                    connectBtn.IsEnabled = true;
                    connectBtn.Content = "Verbinden";
                }
            }
        };

        retryBtn.Click += (s, args) =>
        {
            // Reset block state so user can try again
            api.ResetBlockState();
            statusText.Text = "Verbinde deinen Linux-PC mit Home Assistant";
            statusText.Foreground = Avalonia.Media.Brushes.Gray;
            connectBtn.IsVisible = true;
            retryBtn.IsVisible = false;
            connectBtn.IsEnabled = true;
            connectBtn.Content = "Verbinden";
        };

        await dialog.ShowDialog(this);
    }

    private void OnResetDevice(object? sender, RoutedEventArgs e)
    {
        var api = new HaApiClient(Config.GetConfigDir());
        api.ResetDeviceId();
        if (_statusLabel != null)
            _statusLabel.Text = "Neue ID erstellt – App bitte neustarten!";
    }

    private async void OnQuickActions(object? sender, RoutedEventArgs e)
    {
        var config = Config.Load();
        var actions = LoadQuickActions(config);

        if (actions.Count == 0)
        {
            var emptyDialog = new Window
            {
                Title = "Quick Actions",
                Width = 350, Height = 150,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Content = new StackPanel
                {
                    Margin = new Avalonia.Thickness(20),
                    Spacing = 10,
                    Children =
                    {
                        new TextBlock { Text = "Keine Quick Actions konfiguriert.", FontSize = 14 },
                        new TextBlock { Text = "In den Einstellungen hinzufügen:\nFormat: entity_id,name", Foreground = Avalonia.Media.Brushes.Gray, FontSize = 12 },
                        new Button { Content = "OK", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center }
                    }
                }
            };
            ((Button)((StackPanel)emptyDialog.Content).Children[2]).Click += (s, args) => emptyDialog.Close();
            await emptyDialog.ShowDialog(this);
            return;
        }

        var panel = new StackPanel { Margin = new Avalonia.Thickness(20), Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = "⚡ Quick Actions", FontSize = 18, FontWeight = Avalonia.Media.FontWeight.Bold });

        var api = new HaApiClient(Config.GetConfigDir(), config.VerifySsl);
        api.LoadRegistration();

        foreach (var action in actions)
        {
            var btn = new Button
            {
                Content = action.Name,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                Tag = action
            };
            btn.Click += async (s, args) =>
            {
                var a = (s as Button)?.Tag as QuickActionItem;
                if (a == null) return;
                try
                {
                    await api.ToggleEntityAsync(a.EntityId);
                    btn.Content = $"✓ {a.Name}";
                }
                catch { btn.Content = $"✗ {a.Name}"; }
                await Task.Delay(1000);
                btn.Content = a.Name;
            };
            panel.Children.Add(btn);
        }

        var dialog = new Window
        {
            Title = "Quick Actions",
            Width = 350,
            Height = Math.Max(150, 80 + actions.Count * 50),
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = panel
        };
        await dialog.ShowDialog(this);
    }

    private static List<QuickActionItem> LoadQuickActions(Config config)
    {
        var result = new List<QuickActionItem>();
        try
        {
            var arr = System.Text.Json.JsonDocument.Parse(config.QuickActions).RootElement;
            foreach (var item in arr.EnumerateArray())
            {
                var entityId = item.TryGetProperty("entityId", out var eid) ? eid.GetString() ?? "" : "";
                var name = item.TryGetProperty("name", out var n) ? n.GetString() ?? entityId : entityId;
                if (!string.IsNullOrEmpty(entityId))
                    result.Add(new QuickActionItem(entityId, name));
            }
        }
        catch { }
        return result;
    }
}

public class QuickActionItem
{
    public string EntityId { get; }
    public string Name { get; }
    public QuickActionItem(string entityId, string name) { EntityId = entityId; Name = name; }
}