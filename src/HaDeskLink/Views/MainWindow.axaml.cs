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
using System.Diagnostics;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace HaDeskLink.Views;

/// <summary>
/// KOMPLETT-REDESIGN (v5.1.0): Eine Sidebar, ein Content-Host (ViewHost).
/// Jede Ansicht (Dashboard/QuickActions/Musik/Widgets/Einstellungen) ist ein
/// eigenes Control im selben Content-Bereich — kein Menü-im-Menü, keine
/// Sub-Sidebar in den Einstellungen. Einstellungen sind flache Karten auf
/// EINER scrollbaren Seite.
/// Öffentliche API (App.axaml.cs/Tray): HaUrl, ShowDashboard(url), ReallyExit().
/// </summary>
public partial class MainWindow : Window
{
    private string _haUrl = "";
    private ContentControl? _viewHost;
    private Control? _currentView;
    private string _currentNav = "Dashboard";
    private TextBlock? _toastLabel;
    private readonly List<QuickActionHandler> _hotkeyHandlers = new();
    private bool _reallyExit;

    // ── Neue Palette (dunkel, ruhig, flach) ──
    private static readonly IBrush FgMain = new SolidColorBrush(Color.FromRgb(242, 244, 248));
    private static readonly IBrush Accent = new SolidColorBrush(Color.FromRgb(66, 133, 244));
    private static readonly IBrush Success = new SolidColorBrush(Color.FromRgb(76, 175, 80));
    private static readonly IBrush Warn = new SolidColorBrush(Color.FromRgb(255, 152, 0));
    private static readonly IBrush Danger = new SolidColorBrush(Color.FromRgb(229, 83, 83));

    public string HaUrl
    {
        get => _haUrl;
        set => _haUrl = value;
    }

    public MainWindow()
    {
        InitializeComponent();
        _viewHost = this.FindControl<ContentControl>("ViewHost");

        Wire("NavDashboard", () => OnNavDashboard());
        Wire("NavQuickActions", () => ShowQuickActionsView());
        Wire("NavMusic", () => ShowMusicView());
        Wire("NavWidgets", () => ShowWidgetsView());
        Wire("NavSettings", () => ShowSettingsView());
        Wire("NavRefresh", () => OnRefreshSensors());
        Wire("NavDiscord", () => OpenUrl("https://discord.com/invite/zHPhQ7EaqH"));
        Wire("NavGitHub", () => OpenUrl("https://github.com/TechFlipsi/ha-desklink-linux"));

        Loaded += OnLoaded;
        Closing += OnClosing;
        KeyDown += OnKeyDownGlobal;
    }

    private void Wire(string name, Action activate)
    {
        var btn = this.FindControl<Button>(name);
        if (btn != null) btn.Click += (s, e) => activate();
    }

    // ══════════════════════════════════════════════════════════
    // ANSICHTS-WECHSLER
    // ══════════════════════════════════════════════════════════
    private void ShowView(Control view, string navName)
    {
        _currentNav = navName;
        _viewHost = this.FindControl<ContentControl>("ViewHost");
        if (_viewHost != null) _viewHost.Content = view;
        _currentView = view;
        MarkNav(navName);
    }

    private void MarkNav(string active)
    {
        var map = new (string Btn, string Key)[]
        {
            ("NavDashboard", "Dashboard"),
            ("NavQuickActions", "QuickActions"),
            ("NavMusic", "Music"),
            ("NavWidgets", "Widgets"),
            ("NavSettings", "Settings"),
        };
        foreach (var (btnName, key) in map)
        {
            var btn = this.FindControl<Button>(btnName);
            if (btn == null) continue;
            var isActive = key == active;
            // active-Klasse steuert Hintergrund (Template-Style) + Vordergrund;
            // Hover überschreibt nur über pointerover-Selektor, active bleibt.
            if (isActive) btn.Classes.Add("active");
            else btn.Classes.Remove("active");
        }
    }

    // ══════════════════════════════════════════════════════════
    // DASHBOARD — eigenes Fenster (KEIN WebView im Hauptfenster:
    // verhindert Durchscheinen, Ghost-Fenster und GTK-Crashes)
    // ══════════════════════════════════════════════════════════
    private void OnNavDashboard()
    {
        if (string.IsNullOrWhiteSpace(_haUrl))
        {
            ShowMessageView("🌐 Dashboard", "Noch keine Verbindung eingerichtet",
                "Trage in den Einstellungen die Server-URL und deinen Access-Token ein — das Dashboard öffnet sich dann in einem eigenen Fenster.");
            return;
        }
        DashboardWindow.Open(_haUrl);
    }

    /// <summary>Aufruf vom Tray ("Dashboard anzeigen") und Dashboard-Hotkey.</summary>
    public void ShowDashboard(string haUrl)
    {
        _haUrl = haUrl;
        if (!IsVisible) { Show(); Activate(); }
        OnNavDashboard();
    }

    // ══════════════════════════════════════════════════════════
    // QUICK ACTIONS
    // ══════════════════════════════════════════════════════════
    private void ShowQuickActionsView()
    {
        var config = Config.Load();
        var actions = QuickActionWindow.LoadFromConfig(config);

        var stack = new StackPanel { Spacing = 0 };
        stack.Children.Add(Head("⚡ Quick Actions"));

        if (actions.Count == 0)
        {
            stack.Children.Add(HeadSub("Schalte deine wichtigsten Home-Assistant-Geräte mit einem Klick."));
            stack.Children.Add(EmptyCard("Noch keine Quick Actions",
                "Konfiguriere sie in der config.json (Abschnitt quickActions) — Format: [{\"entityId\": \"light.wohnzimmer\", \"name\": \"Wohnzimmerlicht\"}]."));
            ShowView(Wrap(stack), "QuickActions");
            return;
        }

        stack.Children.Add(HeadSub("Schalte deine wichtigsten Home-Assistant-Geräte mit einem Klick."));

        var api = new HaApiClient(Config.GetConfigDir(), config.VerifySsl);
        try { api.LoadRegistration(); } catch { }

        // Ist-Zustände laden (best effort)
        Dictionary<string, string> states = new();
        try
        {
            var snap = api.GetEntityStatesAsync().ConfigureAwait(false).GetAwaiter().GetResult();
            if (snap != null)
                foreach (var kv in snap) states[kv.Key] = kv.Value.state;
        }
        catch { }

        foreach (var action in actions)
        {
            var card = new Border { Classes = { "card" } };
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };

            var label = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            label.Children.Add(new TextBlock { Classes = { "h2" }, Text = string.IsNullOrEmpty(action.Name) ? action.EntityId : action.Name });
            label.Children.Add(new TextBlock { Classes = { "desc" }, Text = action.EntityId });
            Grid.SetColumn(label, 0);

            var isOn = states.TryGetValue(action.EntityId, out var st) && st == "on";
            var toggle = new ToggleSwitch
            {
                IsChecked = isOn,
                OnContent = "An",
                OffContent = "Aus",
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            var entityId = action.EntityId;
            var busy = false;
            toggle.IsCheckedChanged += async (s, e) =>
            {
                if (busy) return;
                busy = true;
                try { await api.ToggleEntityAsync(entityId); }
                catch (Exception ex)
                {
                    ShowToast("Fehler: " + ex.Message, true);
                }
                busy = false;
            };
            Grid.SetColumn(toggle, 1);

            row.Children.Add(label);
            row.Children.Add(toggle);
            card.Child = row;
            stack.Children.Add(card);
        }
        ShowView(Wrap(stack), "QuickActions");
    }

    // ══════════════════════════════════════════════════════════
    // MUSIK
    // ══════════════════════════════════════════════════════════
    private void ShowMusicView()
    {
        var config = Config.Load();
        if (string.IsNullOrWhiteSpace(config.MaHost))
        {
            ShowMessageView("🎵 Musik", "Music Assistant nicht verbunden",
                "Trage in den Einstellungen (Abschnitt Music Assistant) die Adresse deiner Music-Assistant-Instanz ein.");
            return;
        }

        try
        {
            var view = MusicWindow.CreateEmbedded(config);
            ShowView(view, "Music");
        }
        catch (Exception ex)
        {
            ShowMessageView("🎵 Musik", "Music Assistant Fehler", ex.Message);
        }
    }

    // ══════════════════════════════════════════════════════════
    // WIDGETS
    // ══════════════════════════════════════════════════════════
    private void ShowWidgetsView()
    {
        var config = Config.Load();
        var widgets = WidgetManager.LoadWidgets(config);

        var stack = new StackPanel { Spacing = 0 };
        stack.Children.Add(Head("🧩 Widgets"));
        stack.Children.Add(HeadSub("Desktop-Widgets liegen immer ÜBER dem Hintergrund, aber UNTER allen Fenstern."));

        if (widgets.Count == 0)
        {
            stack.Children.Add(EmptyCard("Keine Widgets eingerichtet",
                "Widgets konfigurierst du in der config.json (Abschnitt widgets) — z.B. Sensoren oder Schalter als dauerhafte Desktop-Karten."));
        }
        else
        {
            foreach (var w in widgets)
            {
                var sub = $"{w.Type} · {w.EntityId}" + (w.Entities.Count > 0 ? $" (+{w.Entities.Count - 1} weitere)" : "");
                var card = new Border { Classes = { "card" } };
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
                var label = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                label.Children.Add(new TextBlock { Classes = { "h2" }, Text = string.IsNullOrEmpty(w.Name) ? w.EntityId : w.Name });
                label.Children.Add(new TextBlock { Classes = { "desc" }, Text = sub });
                Grid.SetColumn(label, 0);
                var pos = new TextBlock
                {
                    Classes = { "desc" },
                    Text = $"Monitor {w.Monitor} · {w.OffsetX}/{w.OffsetY}",
                    VerticalAlignment = VerticalAlignment.Center,
                };
                Grid.SetColumn(pos, 1);
                row.Children.Add(label);
                row.Children.Add(pos);
                card.Child = row;
                stack.Children.Add(card);
            }
            var hint = new TextBlock
            {
                Classes = { "desc" },
                Text = "Änderungen an den Widgets werden beim nächsten App-Start übernommen.",
                Margin = new Thickness(0, 4, 0, 0),
            };
            stack.Children.Add(hint);
        }
        ShowView(Wrap(stack), "Widgets");
    }

    // ══════════════════════════════════════════════════════════
    // EINSTELLUNGEN — flache Karten auf EINER Seite (kein Sub-Menü!)
    // ══════════════════════════════════════════════════════════
    private void ShowSettingsView()
    {
        var config = Config.Load();
        var stack = new StackPanel { Spacing = 0 };
        stack.Children.Add(Head("⚙️ Einstellungen"));
        stack.Children.Add(HeadSub("Alle Einstellungen auf einer Seite — abspeichern pro Karte."));

        // ── 1. Verbindung ──
        {
            var g = Form(2);
            var urlBox = Field(config.HaUrl, "http://homeassistant.local:8123");
            var tokenBox = Field(config.HaToken, "Lang-lebender Access Token", isPassword: true);
            Row(g, 0, "Server-URL", urlBox);
            Row(g, 1, "Access-Token", tokenBox);

            var save = AccentButton("💾 Verbindung speichern");
            save.Click += (s, e) =>
            {
                var cfg = Config.Load();
                cfg.HaUrl = urlBox.Text?.Trim() ?? "";
                cfg.HaToken = tokenBox.Text ?? "";
                cfg.Save();
                _haUrl = cfg.HaUrl;
                ShowToast("Verbindung gespeichert");
            };
            var row2 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            row2.Children.Add(save);
            Card(stack, "Verbindung", "Adresse und Token deiner Home-Assistant-Instanz. Nach dem Speichern lädt das Dashboard-Icon die Verbindung.", g, row2);
        }

        // ── 2. Allgemein ──
        {
            var g = Form(3);
            var auto = new CheckBox { Content = "Bei Anmeldung automatisch starten", IsChecked = Autostart.IsEnabled(), Foreground = FgMain };
            Grid.SetRow(auto, 0); Grid.SetColumnSpan(auto, 2);

            var interval = Field(config.SensorInterval.ToString());
            Row(g, 1, "Sensor-Intervall (Sek.)", interval);

            var channel = new ComboBox { MinWidth = 220 };
            channel.Items.Add("stable");
            channel.Items.Add("beta");
            channel.SelectedIndex = config.UpdateChannel == "beta" ? 1 : 0;
            Row(g, 2, "Update-Kanal", channel);

            var save = AccentButton("💾 Speichern");
            save.Click += (s, e) =>
            {
                var cfg = Config.Load();
                var wantAuto = auto.IsChecked == true;
                if (wantAuto) Autostart.Enable(); else Autostart.Disable();
                cfg.Autostart = wantAuto;
                if (int.TryParse(interval.Text?.Trim(), out var i) && i > 0) cfg.SensorInterval = i;
                cfg.UpdateChannel = channel.SelectedIndex == 1 ? "beta" : "stable";
                cfg.Save();
                ShowToast("Allgemein gespeichert");
            };
            var row3 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            row3.Children.Add(save);
            Card(stack, "Allgemein", "Autostart, Sensoren-Update-Rhythmus und Update-Kanal.", g, row3);
        }

        // ── 3. Sprache ──
        {
            var g = Form(1);
            var cb = new ComboBox { MinWidth = 220 };
            foreach (var lang in Localization.AvailableLanguages)
                cb.Items.Add($"{Localization.GetLanguageName(lang)} ({lang})");
            var idx = Localization.AvailableLanguages.IndexOf(config.Language);
            cb.SelectedIndex = idx >= 0 ? idx : 0;
            Row(g, 0, "Oberflächensprache", cb);

            var save = AccentButton("💾 Speichern");
            save.Click += (s, e) =>
            {
                if (cb.SelectedIndex is int i && i >= 0 && i < Localization.AvailableLanguages.Count)
                {
                    var cfg = Config.Load();
                    cfg.Language = Localization.AvailableLanguages[i];
                    cfg.Save();
                    Localization.LoadLanguage(cfg.Language);
                }
                ShowToast("Sprache gespeichert");
            };
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            row.Children.Add(save);
            Card(stack, "Sprache", "Sprache der App-Oberfläche.", g, row);
        }

        // ── 4. MQTT ──
        {
            var g = Form(6);
            var chk = new CheckBox { Content = "MQTT aktivieren", IsChecked = config.MqttEnabled, Foreground = FgMain };
            Grid.SetRow(chk, 0); Grid.SetColumnSpan(chk, 2);
            var broker = Field(config.MqttBroker, "homeassistant.local");
            Row(g, 1, "Broker", broker);
            var port = Field(config.MqttPort.ToString());
            Row(g, 2, "Port", port);
            var user = Field(config.MqttUsername);
            Row(g, 3, "Benutzername", user);
            var pass = Field(config.MqttPassword, isPassword: true);
            Row(g, 4, "Passwort", pass);
            var ssl = new CheckBox { Content = "SSL/TLS verwenden", IsChecked = config.MqttUseSsl, Foreground = FgMain };
            Grid.SetRow(ssl, 5); Grid.SetColumnSpan(ssl, 2);

            var test = GhostButton("🔌 Verbindung testen");
            var save = AccentButton("💾 Speichern");
            var status = SubLabel("");
            status.Margin = new Thickness(0, 8, 0, 0);

            test.Click += async (s, e) =>
            {
                var b = broker.Text?.Trim() ?? "";
                if (b.Length == 0) { status.Text = "⚠️ Bitte Broker-Adresse eingeben"; return; }
                if (!int.TryParse(port.Text?.Trim(), out var p) || p <= 0) p = 1883;
                status.Text = "⏳ Teste Verbindung...";
                try
                {
                    var ok = await MqttSetupHelper.TestConnectionAsync(
                        b, p,
                        string.IsNullOrWhiteSpace(user.Text) ? null : user.Text.Trim(),
                        pass.Text, ssl.IsChecked == true);
                    status.Text = ok ? $"✓ Verbindung erfolgreich ({b}:{p})" : $"✗ Verbindung zu {b}:{p} fehlgeschlagen";
                    status.Foreground = ok ? Success : Danger;
                }
                catch (Exception ex)
                {
                    status.Text = "✗ Fehler: " + ex.Message;
                    status.Foreground = Danger;
                }
            };
            save.Click += (s, e) =>
            {
                var cfg = Config.Load();
                cfg.MqttEnabled = chk.IsChecked == true;
                cfg.MqttBroker = broker.Text?.Trim() ?? "";
                if (int.TryParse(port.Text?.Trim(), out var p) && p > 0) cfg.MqttPort = p;
                cfg.MqttUsername = user.Text?.Trim() ?? "";
                cfg.MqttPassword = pass.Text ?? "";
                cfg.MqttUseSsl = ssl.IsChecked == true;
                cfg.MqttAutoConfigured = false;
                cfg.Save();
                ShowToast("MQTT gespeichert");
            };
            var btns = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(0, 10, 0, 0) };
            btns.Children.Add(test);
            btns.Children.Add(save);
            Card(stack, "MQTT", "Zweiter, robusterer Kanal zu Home Assistant. Zugangsdaten findest du in HA unter 'Benutzer' (MQTT-Integrations-Benutzer).", g, btns);
            stack.Children.Add(status);
        }

        // ── 5. Music Assistant ──
        {
            var g = Form(3);
            var host = Field(config.MaHost, "192.168.1.x");
            Row(g, 0, "Host", host);
            var port = Field(config.MaPort.ToString());
            Row(g, 1, "Port", port);
            var token = Field(config.MaToken, isPassword: true);
            Row(g, 2, "API-Token", token);

            var save = AccentButton("💾 Speichern");
            save.Click += (s, e) =>
            {
                var cfg = Config.Load();
                cfg.MaHost = host.Text?.Trim() ?? "";
                if (int.TryParse(port.Text?.Trim(), out var p) && p > 0) cfg.MaPort = p;
                cfg.MaToken = token.Text ?? "";
                cfg.Save();
                ShowToast("Music Assistant gespeichert");
            };
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            row.Children.Add(save);
            Card(stack, "Music Assistant", "Verbindung für die Musik-Ansicht. Standard-Port ist 8095.", g, row);
        }

        // ── 6. Tastenkombinationen ──
        {
            var g = Form(3);
            var qa = Field(config.HotkeyModifiers + " + " + config.HotkeyKey);
            qa.IsReadOnly = true;
            Row(g, 0, "Quick Actions", qa);
            var dash = Field(config.HotkeyDashboardModifiers + " + " + config.HotkeyDashboardKey);
            dash.IsReadOnly = true;
            Row(g, 1, "Dashboard", dash);
            var set = Field(config.HotkeySettingsModifiers + " + " + config.HotkeySettingsKey);
            set.IsReadOnly = true;
            Row(g, 2, "Einstellungen", set);
            Card(stack, "Tastenkombinationen", "Aktive Kurzbefehle (wirken, wenn ein HA-DeskLink-Fenster fokussiert ist). Änderbar in der config.json.",
                 g, null);
        }

        // ── 7. Gerät ──
        {
            var g = Form(1);
            var reset = GhostButton("Geräte-ID zurücksetzen");
            var rereg = GhostButton("🔄 Sensoren neu registrieren");
            reset.Click += (s, e) =>
            {
                new HaApiClient(Config.GetConfigDir()).ResetDeviceId();
                ShowToast("Neue Geräte-ID erstellt — App bitte neu starten");
            };
            rereg.Click += (s, e) =>
            {
                System.Threading.Tasks.Task.Run(() => { try { DeskLinkApp.ReRegisterSensors(); } catch { } });
                ShowToast("Sensoren werden neu registriert");
            };
            var btns = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            btns.Children.Add(reset);
            btns.Children.Add(rereg);
            Grid.SetColumnSpan(btns, 2);
            g.Children.Add(btns);
            Card(stack, "Gerät", "Fehlerbehebung: neue Geräte-ID erzeugen (dann App neu starten) oder alle Sensoren neu bei HA registrieren.",
                 g, null);
        }

        ShowView(Wrap(stack), "Settings");
    }

    // ══════════════════════════════════════════════════════════
    // UI-BAUSTEINE (neues Design)
    // ══════════════════════════════════════════════════════════
    private static ScrollViewer Wrap(StackPanel stack)
        => new ScrollViewer { Content = stack, Padding = new Thickness(0, 0, 6, 0) };

    private static TextBlock Head(string text)
        => new TextBlock { Classes = { "h1" }, Text = text, Margin = new Thickness(0, 0, 0, 4) };

    private static TextBlock HeadSub(string text)
        => new TextBlock { Classes = { "desc" }, Text = text, Margin = new Thickness(0, 0, 0, 16) };

    private static TextBlock SubLabel(string text)
        => new TextBlock { Classes = { "desc" }, Text = text };

    private static Border EmptyCard(string title, string body)
    {
        var inner = new StackPanel { Spacing = 4 };
        inner.Children.Add(new TextBlock { Classes = { "h2" }, Text = title });
        inner.Children.Add(new TextBlock { Classes = { "desc" }, Text = body });
        return new Border { Classes = { "card" }, Child = inner };
    }

    private static Border Card(StackPanel parent, string title, string desc, Grid? form, StackPanel? buttons)
    {
        var inner = new StackPanel { Spacing = 0 };
        inner.Children.Add(new TextBlock { Classes = { "h2" }, Text = title });
        inner.Children.Add(new TextBlock { Classes = { "desc" }, Text = desc, Margin = new Thickness(0, 0, 0, 6) });
        if (form != null) inner.Children.Add(form);
        if (buttons != null) inner.Children.Add(buttons);
        var border = new Border { Classes = { "card" }, Child = inner };
        parent.Children.Add(border);
        return border;
    }

    private static Grid Form(int rows)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("210,*") };
        for (int i = 0; i < rows; i++)
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        return g;
    }

    private static void Row(Grid g, int row, string label, Control input)
    {
        var lbl = new TextBlock { Classes = { "lbl" }, Text = label, Margin = new Thickness(0, 9, 10, 0) };
        Grid.SetRow(lbl, row); Grid.SetColumn(lbl, 0);
        input.Margin = new Thickness(0, 4);
        Grid.SetRow(input, row); Grid.SetColumn(input, 1);
        g.Children.Add(lbl);
        g.Children.Add(input);
    }

    private static TextBox Field(string text, string? watermark = null, bool isPassword = false)
        => new TextBox
        {
            Text = text,
            Watermark = watermark,
            PasswordChar = isPassword ? '•' : default,
        };

    private static Button AccentButton(string text)
        => new Button
        {
            Content = text,
            Background = Accent,
            Foreground = Brushes.White,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16, 9),
            BorderThickness = new Thickness(0),
            FontWeight = FontWeight.SemiBold,
        };

    private static Button GhostButton(string text)
        => new Button
        {
            Content = text,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16, 9),
            BorderThickness = new Thickness(1),
        };

    // ── Toast ──
    private void ShowToast(string message, bool isError = false)
    {
        if (_toastLabel == null)
        {
            _toastLabel = new TextBlock
            {
                Foreground = Brushes.White,
                FontWeight = FontWeight.SemiBold,
                Padding = new Thickness(18, 10),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, 34),
                IsHitTestVisible = false,
                IsVisible = false,
            };
            if (this.Content is Grid root)
            {
                root.Children.Add(_toastLabel);
                Grid.SetColumnSpan(_toastLabel, 2);
            }
        }
        _toastLabel.Background = isError
            ? new SolidColorBrush(Color.FromArgb(235, 160, 42, 42))
            : new SolidColorBrush(Color.FromArgb(235, 36, 48, 74));
        _toastLabel.Text = (isError ? "✗ " : "✓ ") + message;
        _toastLabel.IsVisible = true;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.6) };
        timer.Tick += (s, e) => { _toastLabel.IsVisible = false; timer.Stop(); };
        timer.Start();
    }

    private void ShowMessageView(string title, string headline, string detail)
    {
        var stack = new StackPanel { Spacing = 0 };
        stack.Children.Add(Head(title));
        var inner = new StackPanel { Spacing = 4 };
        inner.Children.Add(new TextBlock { Classes = { "h2" }, Text = headline });
        inner.Children.Add(new TextBlock { Classes = { "desc" }, Text = detail });
        stack.Children.Add(new Border { Classes = { "card" }, Child = inner });
        ShowView(Wrap(stack), _currentNav);
    }

    // ══════════════════════════════════════════════════════════
    // LEBENSZYKLUS
    // ══════════════════════════════════════════════════════════
    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        var config = Config.Load();
        _haUrl = config.HaUrl;

        var version = this.FindControl<TextBlock>("LblVersion");
        if (version != null) version.Text = $"v{HaApiClient.GetVersion()}";

        RegisterHotkeys(config);

        // Startansicht: EINSTELLUNGEN (Sirs Vorgabe) — dort steht auch die Verbindungskonfiguration
        ShowSettingsView();
    }

    private void RegisterHotkeys(Config config)
    {
        _hotkeyHandlers.Clear();
        _hotkeyHandlers.Add(new QuickActionHandler(() =>
            Dispatcher.UIThread.Post(() => ShowQuickActionsView()),
            config.HotkeyModifiers, config.HotkeyKey));
        _hotkeyHandlers.Add(new QuickActionHandler(() =>
            Dispatcher.UIThread.Post(() => ShowDashboard(config.HaUrl)),
            config.HotkeyDashboardModifiers, config.HotkeyDashboardKey));
        _hotkeyHandlers.Add(new QuickActionHandler(() =>
            Dispatcher.UIThread.Post(() => ShowSettingsView()),
            config.HotkeySettingsModifiers, config.HotkeySettingsKey));
        foreach (var h in _hotkeyHandlers) h.Start();
    }

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

    private void OnRefreshSensors()
    {
        System.Threading.Tasks.Task.Run(() =>
        {
            try { DeskLinkApp.ReRegisterSensors(); } catch { }
        });
        ShowToast("Sensoren werden aktualisiert");
    }

    private static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { }
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        // Windows-Vision: Kreuz = Fenster verstecken, App läuft im Tray weiter.
        if (!_reallyExit)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        foreach (var h in _hotkeyHandlers) h.Stop();
    }

    /// <summary>Beendet die App wirklich (Tray-Menü "Beenden").</summary>
    public void ReallyExit()
    {
        _reallyExit = true;
        Close();
    }
}