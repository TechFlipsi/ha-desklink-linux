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
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace HaDeskLink.Views;

/// <summary>
/// Main window — Avalonia port of the Windows v5.0.1 desktop UI redesign.
/// Sidebar navigation (200px, DockPanel.Dock=Left) + content area, mirroring the
/// Windows DashboardWindow layout. The content area hosts the HA dashboard
/// (embedded WebView), Quick Actions and Settings are opened as separate windows.
/// </summary>
public partial class MainWindow : Window
{
    private string _haUrl = "";
    private TextBlock? _statusLabel;
    private TextBlock? _versionLabel;
    private readonly List<QuickActionHandler> _hotkeyHandlers = new();
    private QuickActionHandler? _hotkeyQa;
    private QuickActionHandler? _hotkeyDashboard;
    private QuickActionHandler? _hotkeySettings;

    // ── Dark theme brushes (matching the app-wide dark UI) ──
    private static readonly IBrush BgBrush = new SolidColorBrush(Color.FromArgb(255, 26, 26, 46));
    private static readonly IBrush SidebarBrush = new SolidColorBrush(Color.FromArgb(255, 22, 33, 62));
    private static readonly IBrush SidebarHoverBrush = new SolidColorBrush(Color.FromArgb(255, 30, 46, 84));
    private static readonly IBrush AccentBrush = new SolidColorBrush(Color.FromArgb(255, 66, 133, 244));
    private static readonly IBrush FgBrush = new SolidColorBrush(Color.FromArgb(255, 224, 224, 224));
    private static readonly IBrush GrayBrush = new SolidColorBrush(Color.FromArgb(255, 140, 140, 160));
    private static readonly IBrush SuccessBrush = new SolidColorBrush(Color.FromArgb(255, 76, 175, 80));

    public string HaUrl
    {
        get => _haUrl;
        set { _haUrl = value; }
    }

    public MainWindow()
    {
        InitializeComponent();
        _statusLabel = this.FindControl<TextBlock>("LblStatus");

        // Sidebar navigation buttons
        WireSidebarButton("BtnNavDashboard", OnNavDashboard);
        WireSidebarButton("BtnNavQuickActions", OnNavQuickActions);
        WireSidebarButton("BtnNavMusic", OnNavMusic);
        WireSidebarButton("BtnNavWidgets", OnNavWidgets);
        WireSidebarButton("BtnNavSettings", OnNavSettings);
        WireSidebarButton("BtnNavRefresh", OnNavRefresh);
        WireSidebarButton("BtnNavDiscord", (s, e) => OpenUrl("https://discord.com/invite/zHPhQ7EaqH"));
        WireSidebarButton("BtnNavGitHub", (s, e) => OpenUrl("https://github.com/TechFlipsi/ha-desklink-linux"));

        // Legacy buttons (still present for FindControl compatibility)
        var btnDashboard = this.FindControl<Button>("BtnDashboard");
        if (btnDashboard != null) btnDashboard.Click += OnNavDashboard;

        var btnRefresh = this.FindControl<Button>("BtnRefresh");
        if (btnRefresh != null) btnRefresh.Click += OnNavRefresh;

        var btnSetup = this.FindControl<Button>("BtnSetup");
        if (btnSetup != null) btnSetup.Click += OnNavSettings;

        var btnResetDevice = this.FindControl<Button>("BtnResetDevice");
        if (btnResetDevice != null) btnResetDevice.Click += OnResetDevice;

        var btnQuickActions = this.FindControl<Button>("BtnQuickActions");
        if (btnQuickActions != null) btnQuickActions.Click += OnNavQuickActions;

        var btnDiscord = this.FindControl<Button>("BtnDiscord");
        if (btnDiscord != null) btnDiscord.Click += (s, e) => OpenUrl("https://discord.com/invite/zHPhQ7EaqH");

        var btnGitHub = this.FindControl<Button>("BtnGitHub");
        if (btnGitHub != null) btnGitHub.Click += (s, e) => OpenUrl("https://github.com/TechFlipsi/ha-desklink-linux");

        // MQTT settings buttons
        var btnMqttTest = this.FindControl<Button>("BtnMqttTest");
        if (btnMqttTest != null) btnMqttTest.Click += OnMqttTestConnection;

        var _mqttFallbackBox = this.FindControl<TextBox>("TxtMqttFallback");

        var btnMqttSave = this.FindControl<Button>("BtnMqttSave");
        if (btnMqttSave != null) btnMqttSave.Click += OnMqttSave;

        Loaded += OnLoaded;
        Closing += OnClosing;

        // Hotkeys (handled while a HA DeskLink window has focus — Linux has no global hotkeys)
        KeyDown += OnKeyDownGlobal;
    }

    private void WireSidebarButton(string name, EventHandler<RoutedEventArgs> handler)
    {
        var btn = this.FindControl<Button>(name);
        if (btn == null) return;
        btn.Click += handler;
        btn.PointerEntered += (s, e) => { if (btn.Background != AccentBrush) btn.Background = SidebarHoverBrush; };
        btn.PointerExited += (s, e) => { if (btn.Background != AccentBrush) btn.Background = Brushes.Transparent; };
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        var config = Config.Load();
        _haUrl = config.HaUrl;

        _versionLabel = this.FindControl<TextBlock>("LblVersion");
        if (_versionLabel != null)
            _versionLabel.Text = $"v{HaApiClient.GetVersion()}";

        if (_statusLabel != null)
            _statusLabel.Text = string.IsNullOrEmpty(_haUrl)
                ? "⚠️ Nicht verbunden"
                : $"✓ Verbunden: {_haUrl}";

        LoadMqttSettings(config);
        LoadLanguageSettings(config);
        RegisterHotkeys(config);

        // Auto-open the embedded dashboard (main content of the window)
        if (!string.IsNullOrEmpty(_haUrl))
            OnNavDashboard(this, e);
    }

    private void RegisterHotkeys(Config config)
    {
        _hotkeyQa = new QuickActionHandler(() =>
        {
            var actions = QuickActionWindow.LoadFromConfig(config);
            var api = new HaApiClient(Config.GetConfigDir(), config.VerifySsl);
            try { api.LoadRegistration(); } catch { }
            QuickActionWindow.ShowActions(actions, api);
        }, config.HotkeyModifiers, config.HotkeyKey);

        _hotkeyDashboard = new QuickActionHandler(() =>
        {
            if (!string.IsNullOrEmpty(config.HaUrl))
                DashboardWindow.Open(config.HaUrl);
        }, config.HotkeyDashboardModifiers, config.HotkeyDashboardKey);

        _hotkeySettings = new QuickActionHandler(() =>
        {
            SettingsWindow.Open(config, Reconnect, new HaApiClient(Config.GetConfigDir(), config.VerifySsl));
        }, config.HotkeySettingsModifiers, config.HotkeySettingsKey);

        _hotkeyHandlers.Clear();
        _hotkeyHandlers.Add(_hotkeyQa);
        _hotkeyHandlers.Add(_hotkeyDashboard);
        _hotkeyHandlers.Add(_hotkeySettings);
        foreach (var h in _hotkeyHandlers) h.Start();
    }

    /// <summary>
    /// Global key-down routing: passes the event to all configured hotkey handlers.
    /// </summary>
    private void OnKeyDownGlobal(object? sender, KeyEventArgs e)
    {
        foreach (var h in _hotkeyHandlers)
        {
            if (h.HandleKey(e.Key, e.KeyModifiers))
            {
                e.Handled = true;
                return;
            }
        }
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        foreach (var h in _hotkeyHandlers) h.Stop();
    }

    /// <summary>
    /// Reconnect to HA — invoked by the settings window reconnect button.
    /// </summary>
    private void Reconnect()
    {
        var config = Config.Load();
        _haUrl = config.HaUrl;
        if (_statusLabel != null)
            _statusLabel.Text = string.IsNullOrEmpty(_haUrl)
                ? "⚠️ Nicht verbunden"
                : $"✓ Verbunden: {_haUrl}";
    }

    private void LoadMqttSettings(Config config)
    {
        var chkMqtt = this.FindControl<CheckBox>("ChkMqttEnabled");
        var txtBroker = this.FindControl<TextBox>("TxtMqttBroker");
        var txtPort = this.FindControl<TextBox>("TxtMqttPort");
        var txtUser = this.FindControl<TextBox>("TxtMqttUser");
        var txtPass = this.FindControl<TextBox>("TxtMqttPass");
        var chkSsl = this.FindControl<CheckBox>("ChkMqttSsl");
        var lblStatus = this.FindControl<TextBlock>("LblMqttStatus");

        if (chkMqtt != null) chkMqtt.IsChecked = config.MqttEnabled;
        if (txtBroker != null) txtBroker.Text = config.MqttBroker;
        if (txtPort != null) txtPort.Text = config.MqttPort.ToString();
        if (txtUser != null) txtUser.Text = config.MqttUsername;
        if (txtPass != null) txtPass.Text = config.MqttPassword;
        if (chkSsl != null) chkSsl.IsChecked = config.MqttUseSsl;
        var fallback = this.FindControl<TextBox>("TxtMqttFallback");
        if (fallback != null) fallback.Text = config.MqttBrokerFallback;

        if (lblStatus != null)
        {
            if (!config.MqttEnabled)
                lblStatus.Text = "○ Deaktiviert";
            else if (!string.IsNullOrEmpty(config.MqttBroker))
                lblStatus.Text = $"● Verbunden ({config.MqttBroker}:{config.MqttPort})";
            else
                lblStatus.Text = "● Getrennt";
        }
    }

    private void LoadLanguageSettings(Config config)
    {
        var cbLanguage = this.FindControl<ComboBox>("CbLanguage");
        var btnSaveLanguage = this.FindControl<Button>("BtnSaveLanguage");

        if (cbLanguage != null)
        {
            // Sprach-Dropdown füllen: "Name (code)" für jede verfügbare Sprache
            cbLanguage.Items.Clear();
            foreach (var lang in Localization.AvailableLanguages)
                cbLanguage.Items.Add($"{Localization.GetLanguageName(lang)} ({lang})");

            // Aktuell ausgewählte Sprache setzen
            var idx = Localization.AvailableLanguages.IndexOf(config.Language);
            cbLanguage.SelectedIndex = idx >= 0 ? idx : 0;
        }

        if (btnSaveLanguage != null)
            btnSaveLanguage.Click += OnSaveLanguage;
    }

    private void OnSaveLanguage(object? sender, RoutedEventArgs e)
    {
        var cbLanguage = this.FindControl<ComboBox>("CbLanguage");
        if (cbLanguage == null || cbLanguage.SelectedIndex < 0) return;

        var config = Config.Load();
        var idx = cbLanguage.SelectedIndex;

        if (idx >= 0 && idx < Localization.AvailableLanguages.Count)
        {
            config.Language = Localization.AvailableLanguages[idx];
            config.Save();
            // Sprache neu laden
            Localization.LoadLanguage(config.Language);
        }

        if (_statusLabel != null)
            _statusLabel.Text = $"✓ {Localization.Get("settings_saved")}";
    }

    private void OnMqttSave(object? sender, RoutedEventArgs e)
    {
        var config = Config.Load();

        var chkMqtt = this.FindControl<CheckBox>("ChkMqttEnabled");
        var txtBroker = this.FindControl<TextBox>("TxtMqttBroker");
        var txtPort = this.FindControl<TextBox>("TxtMqttPort");
        var txtUser = this.FindControl<TextBox>("TxtMqttUser");
        var txtPass = this.FindControl<TextBox>("TxtMqttPass");
        var chkSsl = this.FindControl<CheckBox>("ChkMqttSsl");
        var lblStatus = this.FindControl<TextBlock>("LblMqttStatus");

        config.MqttEnabled = chkMqtt?.IsChecked ?? false;
        config.MqttBroker = txtBroker?.Text?.Trim() ?? "";
        if (int.TryParse(txtPort?.Text?.Trim(), out var port))
            config.MqttPort = port;
        config.MqttUsername = txtUser?.Text?.Trim() ?? "";
        config.MqttPassword = txtPass?.Text ?? "";
        config.MqttUseSsl = chkSsl?.IsChecked ?? false;
        var fallback = this.FindControl<TextBox>("TxtMqttFallback");
        config.MqttBrokerFallback = fallback?.Text?.Trim() ?? "";
        config.MqttAutoConfigured = false;
        config.Save();

        if (lblStatus != null) lblStatus.Text = "✓ MQTT-Einstellungen gespeichert";
    }

    private async void OnMqttTestConnection(object? sender, RoutedEventArgs e)
    {
        var btn = sender as Button;
        var lblStatus = this.FindControl<TextBlock>("LblMqttStatus");
        if (btn != null) btn.IsEnabled = false;
        if (lblStatus != null) lblStatus.Text = "⏳ Teste MQTT-Verbindung...";

        try
        {
            var txtBroker = this.FindControl<TextBox>("TxtMqttBroker");
            var txtPort = this.FindControl<TextBox>("TxtMqttPort");
            var txtUser = this.FindControl<TextBox>("TxtMqttUser");
            var txtPass = this.FindControl<TextBox>("TxtMqttPass");
            var chkSsl = this.FindControl<CheckBox>("ChkMqttSsl");

            var broker = txtBroker?.Text?.Trim() ?? "";
            if (string.IsNullOrEmpty(broker))
            {
                if (lblStatus != null) lblStatus.Text = "⚠️ Bitte Broker-Adresse eingeben";
                if (btn != null) btn.IsEnabled = true;
                return;
            }

            if (!int.TryParse(txtPort?.Text?.Trim(), out var port) || port <= 0)
                port = 1883;

            var user = string.IsNullOrEmpty(txtUser?.Text?.Trim()) ? null : txtUser.Text.Trim();
            var pass = string.IsNullOrEmpty(txtPass?.Text) ? null : txtPass.Text;
            var ssl = chkSsl?.IsChecked ?? false;

            var ok = await MqttSetupHelper.TestConnectionAsync(broker, port, user, pass, ssl);

            if (ok)
                lblStatus!.Text = $"✓ MQTT-Verbindung erfolgreich ({broker}:{port})";
            else
                lblStatus!.Text = $"✗ Verbindung zu {broker}:{port} fehlgeschlagen";
        }
        catch (Exception ex)
        {
            if (lblStatus != null) lblStatus.Text = $"✗ Fehler: {ex.Message}";
        }
        finally
        {
            if (btn != null) btn.IsEnabled = true;
        }
    }

    // ── Navigation ──────────────────────────────────────────────

    private void OnNavDashboard(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_haUrl))
        {
            if (_statusLabel != null) _statusLabel.Text = "⚠️ Keine HA-URL konfiguriert — erst Settings öffnen";
            return;
        }
        DashboardWindow.Open(_haUrl);
    }

    private void OnNavQuickActions(object? sender, RoutedEventArgs e)
    {
        var config = Config.Load();
        var actions = QuickActionWindow.LoadFromConfig(config);

        if (actions.Count == 0)
        {
            // Noch keine Quick Actions — direkt in die Settings-Sektion zeigen
            SettingsWindow.Open(config, Reconnect, new HaApiClient(Config.GetConfigDir(), config.VerifySsl));
            return;
        }

        var api = new HaApiClient(Config.GetConfigDir(), config.VerifySsl);
        try { api.LoadRegistration(); } catch { }
        QuickActionWindow.ShowActions(actions, api);
    }

    private void OnNavSettings(object? sender, RoutedEventArgs e)
    {
        var config = Config.Load();
        SettingsWindow.Open(config, Reconnect, new HaApiClient(Config.GetConfigDir(), config.VerifySsl));
    }

    private void OnNavMusic(object? sender, RoutedEventArgs e)
    {
        var config = Config.Load();
        if (string.IsNullOrWhiteSpace(config.MaHost))
        {
            // MA nicht konfiguriert → Settings öffnen (dort ist der MA-Abschnitt)
            SettingsWindow.Open(config, Reconnect, new HaApiClient(Config.GetConfigDir(), config.VerifySsl));
            return;
        }
        var musicWin = new MusicWindow(config);
        musicWin.Show();
    }

    private void OnNavWidgets(object? sender, RoutedEventArgs e)
    {
        var config = Config.Load();
        SettingsWindow.Open(config, Reconnect, new HaApiClient(Config.GetConfigDir(), config.VerifySsl));
    }

    private void OnNavRefresh(object? sender, RoutedEventArgs e)
    {
        // Sensoren neu registrieren (gleiche Funktion wie Settings-Button)
        System.Threading.Tasks.Task.Run(() =>
        {
            try { DeskLinkApp.ReRegisterSensors(); }
            catch { }
        });
        if (_statusLabel != null)
            _statusLabel.Text = "🔄 Sensoren werden aktualisiert...";
    }

    private void OnResetDevice(object? sender, RoutedEventArgs e)
    {
        var api = new HaApiClient(Config.GetConfigDir());
        api.ResetDeviceId();
        if (_statusLabel != null)
            _statusLabel.Text = "Neue ID erstellt – App bitte neustarten!";
    }

    private static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { }
    }

    private void OnSetup(object? sender, RoutedEventArgs e)
    {
        OnNavSettings(sender, e);
    }
}