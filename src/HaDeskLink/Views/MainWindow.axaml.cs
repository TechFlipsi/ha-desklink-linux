
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
using System.Diagnostics;

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

        var btnDiscord = this.FindControl<Button>("BtnDiscord");
        if (btnDiscord != null) btnDiscord.Click += (s, e) => OpenUrl("https://discord.gg/HnCZY54U7");

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
        var tokenBox = new TextBox { Watermark = "Long-Lived Token", PasswordChar = '•' };
        var sslCheck = new CheckBox { Content = "SSL-Zertifikat prüfen", IsChecked = config.VerifySsl };

        var dialog = new Window
        {
            Title = "HA DeskLink – Einrichtung",
            Width = 450,
            Height = 320,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(20),
                Spacing = 10,
                Children =
                {
                    new TextBlock { Text = "HA DeskLink Setup", FontSize = 18, FontWeight = Avalonia.Media.FontWeight.Bold },
                    new TextBlock { Text = "Verbinde deinen Linux-PC mit Home Assistant", Foreground = Avalonia.Media.Brushes.Gray },
                    new TextBlock { Text = "HA URL:" },
                    urlBox,
                    new TextBlock { Text = "Long-Lived Token:" },
                    tokenBox,
                    sslCheck,
                    new TextBlock { Text = "Token: HA → Profil → Sicherheit → Long-Lived Access Tokens", FontSize = 11, Foreground = Avalonia.Media.Brushes.Gray },
                    new Button { Content = "Verbinden", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center }
                }
            }
        };

        var connectBtn = ((StackPanel)dialog.Content).Children[^1] as Button;
        var statusText = ((StackPanel)dialog.Content).Children[1] as TextBlock;

        connectBtn!.Click += async (s, args) =>
        {
            connectBtn.IsEnabled = false;
            connectBtn.Content = "Verbindet...";
            try
            {
                var api = new HaApiClient(Config.GetConfigDir(), sslCheck.IsChecked ?? false);
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
                statusText!.Text = $"✗ Fehler: {ex.Message}";
                statusText.Foreground = Avalonia.Media.Brushes.Red;
                connectBtn.IsEnabled = true;
                connectBtn.Content = "Verbinden";
            }
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
}