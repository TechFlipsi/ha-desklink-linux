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
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.LogicalTree;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using HaDeskLink.Views;

namespace HaDeskLink;

/// <summary>
/// Settings window — Avalonia port of the Windows v5.0.1 desktop UI redesign.
/// Layout: 200px sidebar (DockPanel.Dock=Left) + scrollable content area + bottom bar.
/// Uses Grid/DockPanel instead of SplitContainer (the Windows SplitContainer crashed —
/// v5.0.2/5.0.3 replaced it with Dock=Left + Dock=Fill panels; Avalonia has no
/// SplitContainer at all, so this is the native equivalent).
/// Every setting has a description label (desc_* i18n keys).
/// </summary>
public class SettingsWindow : Window
{
    // ═══ Config und API ═══
    private readonly Config _config;
    private readonly Action _onReconnect;
    private readonly HaApiClient? _api;

    // ═══ Steuerlemente — Verbindung ═══
    private TextBox _urlBox = null!;
    private TextBox _tokenBox = null!;
    private CheckBox _sslCheck = null!;

    // ═══ Steuerlemente — Allgemein ═══
    private CheckBox _autostartCheck = null!;
    private NumericUpDown _intervalBox = null!;
    private ComboBox _updateChannelBox = null!;

    // ═══ Steuerlemente — Erscheinungsbild ═══
    private ComboBox _languageBox = null!;
    private ComboBox _themeBox = null!;

    // ═══ Steuerlemente — Benachrichtigungen ═══
    private ComboBox _notifPosBox = null!;
    private ComboBox _notifMonitorBox = null!;

    // ═══ Steuerlemente — Tastenkombinationen ═══
    private ComboBox _hotkeyModBox = null!;
    private ComboBox _hotkeyKeyBox = null!;
    private ComboBox _hotkeyDashModBox = null!;
    private ComboBox _hotkeyDashKeyBox = null!;
    private ComboBox _hotkeySettingsModBox = null!;
    private ComboBox _hotkeySettingsKeyBox = null!;

    // ═══ Status und Quick Actions ═══
    private TextBlock _statusLabel = null!;
    private ListBox _qaList = null!;
    private List<(string entityId, string friendlyName)> _entities = new();

    // ═══ MQTT-Steuerlemente ═══
    private CheckBox _mqttEnabledCheck = null!;
    private TextBox _mqttBrokerBox = null!;
    private TextBox _mqttPortBox = null!;
    private TextBox _mqttUserBox = null!;
    private TextBox _mqttPassBox = null!;
    private CheckBox _mqttSslCheck = null!;
    private TextBox _mqttFallbackBox = null!;
    private TextBlock _mqttStatusLabel = null!;

    // ═══ Layout-Panels für Navigation und Theme ═══
    private Border _sidebarPanel = null!;
    private Border _contentPanel = null!;
    private Border _bottomPanel = null!;
    private readonly List<Button> _sidebarButtons = new();
    private readonly List<Control> _sectionPanels = new();
    private int _currentSection = 0;

    // ═══ Theme Brushes (Dark/Light) ═══
    private bool _isDark = true;
    private IBrush _sidebarNormalBg = null!;
    private IBrush _sidebarHoverBg = null!;

    private static readonly IBrush DarkBg = new SolidColorBrush(Color.FromArgb(255, 32, 32, 32));
    private static readonly IBrush DarkFg = new SolidColorBrush(Color.FromArgb(255, 230, 230, 230));
    private static readonly IBrush DarkInput = new SolidColorBrush(Color.FromArgb(255, 48, 48, 48));
    private static readonly IBrush DarkSectionBg = new SolidColorBrush(Color.FromArgb(255, 40, 40, 40));
    private static readonly IBrush AccentBlue = new SolidColorBrush(Color.FromArgb(255, 0, 120, 215));
    private static readonly IBrush SuccessGreen = new SolidColorBrush(Color.FromArgb(255, 0, 134, 100));
    private static readonly IBrush WarningOrange = new SolidColorBrush(Color.FromArgb(255, 180, 80, 0));
    private static readonly IBrush DangerRed = new SolidColorBrush(Color.FromArgb(255, 200, 50, 50));

    private static readonly IBrush LightBg = Brushes.White;
    private static readonly IBrush LightFg = new SolidColorBrush(Color.FromArgb(255, 32, 32, 32));
    private static readonly IBrush LightInput = new SolidColorBrush(Color.FromArgb(255, 248, 248, 248));
    private static readonly IBrush LightSidebarBg = new SolidColorBrush(Color.FromArgb(255, 240, 240, 240));
    private static readonly IBrush LightBottomBg = new SolidColorBrush(Color.FromArgb(255, 248, 248, 248));

    private static readonly IBrush DescGrayDark = new SolidColorBrush(Color.FromArgb(255, 140, 140, 140));
    private static readonly IBrush DescGrayLight = new SolidColorBrush(Color.FromArgb(255, 100, 100, 100));

    private static readonly JsonSerializerOptions _jsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = false };

    private static SettingsWindow? _instance;

    public SettingsWindow(Config config, Action onReconnect, HaApiClient? api = null)
    {
        _config = config;
        _onReconnect = onReconnect;
        _api = api;

        Title = $"HA DeskLink - {Localization.Get("settings_title")}";
        Width = 800;
        Height = 600;
        MinWidth = 600;
        MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        InitializeComponents();
        LoadSettings();
        LoadQuickActionsList();
        ApplyTheme(_config.Theme);
        ShowSection(0);

        Closed += (s, e) => _instance = null;
    }

    // ═══════════════════════════════════════════════════════
    // INITIALISIERUNG — Sidebar (Dock=Left) + Inhaltsbereich (füllt Rest)
    // ═══════════════════════════════════════════════════════

    private void InitializeComponents()
    {
        // ── Sidebar: DockPanel.Dock=Left, 200px breit ──
        BuildSidebar();
        DockPanel.SetDock(_sidebarPanel, Dock.Left);
        _sidebarPanel.Width = 200;

        // ── Bottom Bar: DockPanel.Dock=Bottom, immer sichtbar ──
        BuildBottomBar();

        // ── Content: füllt den restlichen Platz (DockPanel ohne Dock = Fill) ──
        _contentPanel = new Border { Padding = new Thickness(0) };
        var sectionHost = new Grid();
        _contentPanel.Child = sectionHost;

        // Sections erstellen — jede füllt den Content-Bereich, nur eine sichtbar
        _sectionPanels.Add(BuildConnectionSection());
        _sectionPanels.Add(BuildGeneralSection());
        _sectionPanels.Add(BuildAppearanceSection());
        _sectionPanels.Add(BuildNotificationsSection());
        _sectionPanels.Add(BuildHotkeysSection());
        _sectionPanels.Add(BuildMqttSection());
        _sectionPanels.Add(BuildQuickActionsSection());

        foreach (var section in _sectionPanels)
        {
            section.IsVisible = false;
            sectionHost.Children.Add(section);
        }

        // Root: DockPanel — Content zuerst (Fill), dann Sidebar/Bottom (docked)
        var root = new DockPanel();
        root.Children.Add(_contentPanel);   // Fill
        root.Children.Add(_sidebarPanel);    // Left
        root.Children.Add(_bottomPanel);     // Bottom

        Content = root;
    }

    // ═══════════════════════════════════════════════════════
    // SIDEBAR — Navigation mit 7 Items
    // ═══════════════════════════════════════════════════════

    private void BuildSidebar()
    {
        var navItems = new[]
        {
            (0, "🔌 " + Localization.Get("settings_connection", "Verbindung")),
            (1, "⚙️ " + Localization.Get("settings_general", "Allgemein")),
            (2, "🎨 " + Localization.Get("settings_appearance", "Erscheinungsbild")),
            (3, "🔔 " + Localization.Get("settings_notifications", "Benachrichtigungen")),
            (4, "⌨️ " + Localization.Get("settings_hotkeys", "Tastenkombinationen")),
            (5, "📡 " + Localization.Get("mqtt_settings")),
            (6, "⚡ " + Localization.Get("settings_quickactions")),
        };

        var navStack = new StackPanel { Margin = new Thickness(0, 48, 0, 0) };
        foreach (var (index, text) in navItems)
        {
            var btn = MakeSidebarButton(text, index);
            _sidebarButtons.Add(btn);
            navStack.Children.Add(btn);
        }

        var sidebarHeader = new TextBlock
        {
            Text = "HA DeskLink",
            FontSize = 14,
            FontWeight = FontWeight.Bold,
            Margin = new Thickness(16, 12, 16, 8),
            Foreground = DarkFg
        };
        DockPanel.SetDock(sidebarHeader, Dock.Top);

        _sidebarPanel = new Border
        {
            Child = new DockPanel { Children = { sidebarHeader, navStack } }
        };
    }

    private Button MakeSidebarButton(string text, int index)
    {
        var btn = new Button
        {
            Content = "  " + text,
            Height = 40,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            FontSize = 14,
            Foreground = DarkFg,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(16, 0, 8, 0),
            Tag = index,
        };
        btn.Click += (s, e) => ShowSection(index);
        // Hover-Effekt: leicht hellerer Hintergrund (nur bei nicht ausgewähltem Item)
        btn.PointerEntered += (s, e) =>
        {
            if (index != _currentSection)
                btn.Background = _sidebarHoverBg;
        };
        btn.PointerExited += (s, e) =>
        {
            if (index != _currentSection)
                btn.Background = _sidebarNormalBg;
        };
        return btn;
    }

    private void ShowSection(int index)
    {
        // Alle Sections ausblenden
        foreach (var section in _sectionPanels)
            section.IsVisible = false;

        // Ausgewählte Section anzeigen
        if (index >= 0 && index < _sectionPanels.Count)
            _sectionPanels[index].IsVisible = true;

        // Sidebar-Buttons aktualisieren (ausgewählter = AccentBlue, weiße Schrift)
        for (int i = 0; i < _sidebarButtons.Count; i++)
        {
            if (i == index)
            {
                _sidebarButtons[i].Background = AccentBlue;
                _sidebarButtons[i].Foreground = Brushes.White;
            }
            else
            {
                _sidebarButtons[i].Background = _sidebarNormalBg;
                _sidebarButtons[i].Foreground = _isDark ? DarkFg : LightFg;
            }
        }

        _currentSection = index;
    }

    // ═══════════════════════════════════════════════════════
    // SECTION-BUILDER — Jede Section: ScrollViewer füllt den Content-Bereich
    // ═══════════════════════════════════════════════════════

    // ─── Helper: Section (ScrollViewer, 16px Padding) ───
    private static ScrollViewer MakeSectionPanel(Control content)
    {
        return new ScrollViewer
        {
            Content = new Border { Padding = new Thickness(16), Child = content },
        };
    }

    // ─── Helper: Grid für 2-Spalten Layout (Label 200px + Rest) ───
    private static Grid MakeFieldGrid()
    {
        var g = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        return g;
    }

    // ─── Helper: Row-Factory (Label + Control, oder ColumnSpan-2 Beschreibung) ───
    private static (TextBlock label, Control input) MakeFieldRow(Grid grid, int row, string labelText, Control input)
    {
        var label = new TextBlock
        {
            Text = labelText,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.NoWrap,
            Margin = new Thickness(0, 8, 8, 0),
        };
        Grid.SetRow(label, row);
        Grid.SetColumn(label, 0);
        grid.Children.Add(label);

        Grid.SetRow(input, row);
        Grid.SetColumn(input, 1);
        grid.Children.Add(input);
        return (label, input);
    }

    // ─── Helper: Beschreibungs-Label (klein, grau, unter Eingabefeldern) ───
    private static TextBlock MakeDescriptionLabel(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = 11,
            Foreground = DescGrayDark,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 6),
            Tag = "desc",
        };
    }

    // ─── Helper: Beschreibung als Grid-Row mit ColumnSpan=2 ───
    private static TextBlock AddDescriptionRow(Grid grid, int row, string key)
    {
        var desc = MakeDescriptionLabel(Localization.Get(key));
        Grid.SetRow(desc, row);
        Grid.SetColumn(desc, 0);
        Grid.SetColumnSpan(desc, 2);
        grid.Children.Add(desc);
        return desc;
    }

    // ─── Helper: Section Header (größer, fett) ───
    private static TextBlock MakeSectionHeader(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = 16,
            FontWeight = FontWeight.Bold,
            Margin = new Thickness(0, 0, 0, 4),
        };
    }

    // ─── Helper: Button mit Farbe ───
    private static Button MakeButton(string text, IBrush color, EventHandler<RoutedEventArgs> onClick, string? tooltip = null)
    {
        var btn = new Button
        {
            Content = text,
            MinWidth = 140,
            MinHeight = 36,
            Background = color,
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 6, 12, 6),
            Margin = new Thickness(2),
        };
        if (tooltip != null) btn.SetValue(Avalonia.Controls.ToolTip.TipProperty, tooltip);
        btn.Click += onClick;
        return btn;
    }

    // ─── Section 1: 🔌 Verbindung ───
    private Control BuildConnectionSection()
    {
        var stack = new StackPanel { Spacing = 0 };
        stack.Children.Add(MakeSectionHeader("🔌 " + Localization.Get("settings_connection", "Verbindung")));

        var table = MakeFieldGrid();

        _urlBox = new TextBox { Text = "https://homeassistant.local:8123", MinHeight = 32 };
        _urlBox.SetValue(Avalonia.Controls.ToolTip.TipProperty, Localization.Get("tooltip_ha_url"));
        MakeFieldRow(table, 0, Localization.Get("settings_ha_url"), _urlBox);
        AddDescriptionRow(table, 1, "desc_ha_url");

        _tokenBox = new TextBox { PasswordChar = '•', MinHeight = 32 };
        _tokenBox.SetValue(Avalonia.Controls.ToolTip.TipProperty, Localization.Get("tooltip_token"));
        MakeFieldRow(table, 2, Localization.Get("settings_token"), _tokenBox);
        AddDescriptionRow(table, 3, "desc_token");

        _sslCheck = new CheckBox
        {
            Content = Localization.Get("settings_verify_ssl"),
            Margin = new Thickness(0, 8, 0, 0),
        };
        _sslCheck.SetValue(Avalonia.Controls.ToolTip.TipProperty, Localization.Get("tooltip_ssl"));
        Grid.SetRow(_sslCheck, 4);
        Grid.SetColumn(_sslCheck, 0);
        Grid.SetColumnSpan(_sslCheck, 2);
        table.Children.Add(_sslCheck);
        AddDescriptionRow(table, 5, "desc_ssl");

        stack.Children.Add(table);

        // Neu verbinden Button
        var actionPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 12, 0, 0),
        };
        actionPanel.Children.Add(MakeButton("🔄 " + Localization.Get("settings_reconnect", "Neu verbinden"),
            new SolidColorBrush(Color.FromArgb(255, 0, 100, 180)), OnReconnectClicked,
            Localization.Get("tooltip_reconnect")));
        stack.Children.Add(actionPanel);

        return MakeSectionPanel(stack);
    }

    // ─── Section 2: ⚙️ Allgemein (Autostart, Sensor-Intervall, Update-Kanal, Reset/Reregister) ───
    private Control BuildGeneralSection()
    {
        var stack = new StackPanel { Spacing = 0 };
        stack.Children.Add(MakeSectionHeader("⚙️ " + Localization.Get("settings_general", "Allgemein")));

        var table = MakeFieldGrid();

        // Autostart (XDG autostart on Linux)
        _autostartCheck = new CheckBox
        {
            Content = Localization.Get("settings_autostart"),
            Margin = new Thickness(0, 8, 0, 0),
        };
        _autostartCheck.SetValue(Avalonia.Controls.ToolTip.TipProperty, Localization.Get("tooltip_autostart"));
        Grid.SetRow(_autostartCheck, 0);
        Grid.SetColumn(_autostartCheck, 0);
        Grid.SetColumnSpan(_autostartCheck, 2);
        table.Children.Add(_autostartCheck);
        AddDescriptionRow(table, 1, "desc_autostart");

        // Sensor-Intervall
        _intervalBox = new NumericUpDown { Minimum = 10, Maximum = 300, Value = 30, MinHeight = 32 };
        _intervalBox.SetValue(Avalonia.Controls.ToolTip.TipProperty, Localization.Get("tooltip_sensor_interval"));
        MakeFieldRow(table, 2, Localization.Get("settings_sensor_interval"), _intervalBox);
        var intervalHint = new TextBlock
        {
            Text = Localization.Get("sensors_interval_hint"),
            FontSize = 10,
            Foreground = DescGrayDark,
            Margin = new Thickness(0, 2, 0, 0),
        };
        Grid.SetRow(intervalHint, 3);
        Grid.SetColumn(intervalHint, 0);
        Grid.SetColumnSpan(intervalHint, 2);
        table.Children.Add(intervalHint);
        AddDescriptionRow(table, 4, "desc_sensor_interval");

        // Update-Kanal
        _updateChannelBox = new ComboBox { MinHeight = 32, HorizontalAlignment = HorizontalAlignment.Stretch };
        _updateChannelBox.SetValue(Avalonia.Controls.ToolTip.TipProperty, Localization.Get("tooltip_update_channel"));
        _updateChannelBox.Items.Add(Localization.Get("settings_channel_stable"));
        _updateChannelBox.Items.Add(Localization.Get("settings_channel_prerelease"));
        MakeFieldRow(table, 5, Localization.Get("settings_update_channel"), _updateChannelBox);
        AddDescriptionRow(table, 6, "desc_update_channel");

        // Beschreibungen für Reset Device ID + Re-register
        AddDescriptionRow(table, 7, "desc_reset_device");
        AddDescriptionRow(table, 8, "desc_reregister");

        stack.Children.Add(table);

        // Reset Device ID und Re-register Sensors Buttons
        var actionPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 12, 0, 0),
        };
        actionPanel.Children.Add(MakeButton("🔑 " + Localization.Get("settings_reset_device", "Geräte-ID zurücksetzen"),
            WarningOrange, OnResetDeviceId, Localization.Get("tooltip_reset_device")));
        actionPanel.Children.Add(MakeButton("📊 " + Localization.Get("settings_reregister_sensors", "Sensoren neu registrieren"),
            SuccessGreen, OnReRegisterSensors, Localization.Get("tooltip_reregister")));
        stack.Children.Add(actionPanel);

        return MakeSectionPanel(stack);
    }

    // ─── Section 3: 🎨 Erscheinungsbild (Sprache, Theme) ───
    private Control BuildAppearanceSection()
    {
        var stack = new StackPanel { Spacing = 0 };
        stack.Children.Add(MakeSectionHeader("🎨 " + Localization.Get("settings_appearance", "Erscheinungsbild")));

        var table = MakeFieldGrid();

        _languageBox = new ComboBox { MinHeight = 32, HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var lang in Localization.AvailableLanguages)
            _languageBox.Items.Add($"{Localization.GetLanguageName(lang)} ({lang})");
        MakeFieldRow(table, 0, Localization.Get("settings_language"), _languageBox);
        AddDescriptionRow(table, 1, "desc_language");

        _themeBox = new ComboBox { MinHeight = 32, HorizontalAlignment = HorizontalAlignment.Stretch };
        _themeBox.SetValue(Avalonia.Controls.ToolTip.TipProperty, Localization.Get("tooltip_theme"));
        _themeBox.Items.Add(Localization.Get("settings_theme_system"));
        _themeBox.Items.Add(Localization.Get("settings_theme_light"));
        _themeBox.Items.Add(Localization.Get("settings_theme_dark"));
        MakeFieldRow(table, 2, Localization.Get("settings_theme"), _themeBox);
        AddDescriptionRow(table, 3, "desc_theme");

        stack.Children.Add(table);
        return MakeSectionPanel(stack);
    }

    // ─── Section 4: 🔔 Benachrichtigungen (Position, Monitor) ───
    private Control BuildNotificationsSection()
    {
        var stack = new StackPanel { Spacing = 0 };
        stack.Children.Add(MakeSectionHeader("🔔 " + Localization.Get("settings_notifications", "Benachrichtigungen")));

        var table = MakeFieldGrid();

        // Position
        _notifPosBox = new ComboBox { MinHeight = 32, HorizontalAlignment = HorizontalAlignment.Stretch };
        _notifPosBox.SetValue(Avalonia.Controls.ToolTip.TipProperty, Localization.Get("tooltip_notif_position"));
        _notifPosBox.Items.Add(Localization.Get("settings_notif_bottom_left"));
        _notifPosBox.Items.Add(Localization.Get("settings_notif_bottom_right"));
        _notifPosBox.Items.Add(Localization.Get("settings_notif_top_left"));
        _notifPosBox.Items.Add(Localization.Get("settings_notif_top_right"));
        MakeFieldRow(table, 0, Localization.Get("settings_notif_position"), _notifPosBox);
        AddDescriptionRow(table, 1, "desc_notif_position");

        // Monitor (alle Screens)
        _notifMonitorBox = new ComboBox { MinHeight = 32, HorizontalAlignment = HorizontalAlignment.Stretch };
        _notifMonitorBox.SetValue(Avalonia.Controls.ToolTip.TipProperty, Localization.Get("tooltip_notif_monitor"));
        var screens = Screens.All.ToList();
        for (int i = 0; i < screens.Count; i++)
        {
            var label = i == 0
                ? $"{Localization.Get("settings_notif_primary_monitor")} ({screens[i].DisplayName ?? (i + 1).ToString()})"
                : $"Monitor {i + 1} ({screens[i].DisplayName ?? (i + 1).ToString()})";
            _notifMonitorBox.Items.Add(label);
        }
        if (_notifMonitorBox.Items.Count == 0)
            _notifMonitorBox.Items.Add(Localization.Get("settings_notif_primary_monitor"));
        MakeFieldRow(table, 2, Localization.Get("settings_notif_monitor"), _notifMonitorBox);
        AddDescriptionRow(table, 3, "desc_notif_monitor");

        stack.Children.Add(table);
        return MakeSectionPanel(stack);
    }

    // ─── Section 5: ⌨️ Tastenkombinationen (3 Hotkey Rows) ───
    private Control BuildHotkeysSection()
    {
        var stack = new StackPanel { Spacing = 0 };
        stack.Children.Add(MakeSectionHeader("⌨️ " + Localization.Get("settings_hotkeys", "Tastenkombinationen")));

        var table = MakeFieldGrid();

        // Quick Actions Hotkey
        MakeFieldRow(table, 0, Localization.Get("settings_hotkey_qa"), CreateHotkeyRow(out _hotkeyModBox, out _hotkeyKeyBox));
        AddDescriptionRow(table, 1, "desc_hotkey_qa");

        // Dashboard Hotkey
        MakeFieldRow(table, 2, Localization.Get("settings_hotkey_dashboard"), CreateHotkeyRow(out _hotkeyDashModBox, out _hotkeyDashKeyBox));
        AddDescriptionRow(table, 3, "desc_hotkey_dashboard");

        // Settings Hotkey
        MakeFieldRow(table, 4, Localization.Get("settings_hotkey_settings"), CreateHotkeyRow(out _hotkeySettingsModBox, out _hotkeySettingsKeyBox));
        AddDescriptionRow(table, 5, "desc_hotkey_settings");

        // Hinweis: Unter Linux/GNOME gibt es systemweite Hotkeys über die GNOME-Einstellungen
        stack.Children.Add(table);

        var hint = new TextBlock
        {
            Text = "ℹ️ " + (Localization.CurrentLanguage == "de"
                ? "Hinweis: Die Tastenkombinationen gelten innerhalb der HA DeskLink Fenster (fokussiert). Für systemweite Hotkeys unter Linux die Desktop-Umgebung nutzen (z.B. GNOME Tastenkombinationen)."
                : "Note: These hotkeys work while an HA DeskLink window has keyboard focus. For system-wide hotkeys on Linux, use your desktop environment (e.g. GNOME custom shortcuts)."),
            FontSize = 11,
            Foreground = DescGrayDark,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 12, 0, 0),
        };
        stack.Children.Add(hint);

        return MakeSectionPanel(stack);
    }

    private static Control CreateHotkeyRow(out ComboBox modBox, out ComboBox keyBox)
    {
        modBox = new ComboBox { MinWidth = 120, MinHeight = 32 };
        modBox.Items.Add("Ctrl+Shift");
        modBox.Items.Add("Ctrl+Alt");
        modBox.Items.Add("Ctrl");
        modBox.Items.Add("Alt");
        modBox.Items.Add("Shift");
        modBox.Items.Add(Localization.Get("settings_hotkey_none"));

        keyBox = new ComboBox { MinWidth = 80, MinHeight = 32 };
        foreach (var k in new[] { "H", "Q", "A", "S", "D", "F", "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12", "Space" })
            keyBox.Items.Add(k);

        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { modBox, keyBox } };
        return panel;
    }

    // ─── Section 6: 📡 MQTT ───
    private Control BuildMqttSection()
    {
        var stack = new StackPanel { Spacing = 0 };
        stack.Children.Add(MakeSectionHeader("📡 " + Localization.Get("mqtt_settings")));

        var table = MakeFieldGrid();

        // MQTT aktivieren
        _mqttEnabledCheck = new CheckBox
        {
            Content = Localization.Get("mqtt_enabled"),
            Margin = new Thickness(0, 8, 0, 0),
        };
        _mqttEnabledCheck.SetValue(Avalonia.Controls.ToolTip.TipProperty, "MQTT für Echtzeit-Mediensteuerung und schnelle Sensor-Updates aktivieren");
        Grid.SetRow(_mqttEnabledCheck, 0);
        Grid.SetColumn(_mqttEnabledCheck, 0);
        Grid.SetColumnSpan(_mqttEnabledCheck, 2);
        table.Children.Add(_mqttEnabledCheck);
        AddDescriptionRow(table, 1, "desc_mqtt_enabled");

        // Broker
        _mqttBrokerBox = new TextBox { MinHeight = 32 };
        _mqttBrokerBox.SetValue(Avalonia.Controls.ToolTip.TipProperty, "MQTT-Broker Hostname (z.B. homeassistant.local)");
        MakeFieldRow(table, 2, Localization.Get("mqtt_broker"), _mqttBrokerBox);
        AddDescriptionRow(table, 3, "desc_mqtt_broker");

        // Port
        _mqttPortBox = new TextBox { Text = "1883", MinHeight = 32 };
        _mqttPortBox.SetValue(Avalonia.Controls.ToolTip.TipProperty, "MQTT-Broker Port (Standard: 1883, SSL: 8883)");
        MakeFieldRow(table, 4, Localization.Get("mqtt_port"), _mqttPortBox);
        AddDescriptionRow(table, 5, "desc_mqtt_port");

        // Username
        _mqttUserBox = new TextBox { MinHeight = 32 };
        _mqttUserBox.SetValue(Avalonia.Controls.ToolTip.TipProperty, "MQTT-Benutzername (optional, leer lassen bei anonymem Zugang)");
        MakeFieldRow(table, 6, Localization.Get("mqtt_username"), _mqttUserBox);
        AddDescriptionRow(table, 7, "desc_mqtt_username");

        // Password
        _mqttPassBox = new TextBox { PasswordChar = '•', MinHeight = 32 };
        _mqttPassBox.SetValue(Avalonia.Controls.ToolTip.TipProperty, "MQTT-Passwort (optional)");
        MakeFieldRow(table, 8, Localization.Get("mqtt_password"), _mqttPassBox);
        AddDescriptionRow(table, 9, "desc_mqtt_password");

        // SSL
        _mqttSslCheck = new CheckBox
        {
            Content = Localization.Get("mqtt_use_ssl"),
            Margin = new Thickness(0, 8, 0, 0),
        };
        _mqttSslCheck.SetValue(Avalonia.Controls.ToolTip.TipProperty, "SSL/TLS für MQTT-Verbindung aktivieren");
        Grid.SetRow(_mqttSslCheck, 10);
        Grid.SetColumn(_mqttSslCheck, 0);
        Grid.SetColumnSpan(_mqttSslCheck, 2);
        table.Children.Add(_mqttSslCheck);
        AddDescriptionRow(table, 11, "desc_mqtt_ssl");

        // Fallback-Adresse
        _mqttFallbackBox = new TextBox { MinHeight = 32 };
        _mqttFallbackBox.SetValue(Avalonia.Controls.ToolTip.TipProperty, "Alternative MQTT-Broker-Adresse (z.B. lokale IP), falls die Hauptadresse nicht erreichbar ist. Leer lassen für keinen Fallback.");
        MakeFieldRow(table, 12, Localization.Get("mqtt_fallback_address", "Fallback-Adresse"), _mqttFallbackBox);
        AddDescriptionRow(table, 13, "desc_mqtt_fallback");

        // Verbindung testen Button
        var testWrap = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
        var mqttTestBtn = MakeButton("🔌 " + Localization.Get("mqtt_test_connection", "Verbindung testen"),
            SuccessGreen, OnMqttTestConnection, "Verbindung zum MQTT-Broker testen, bevor gespeichert wird");
        testWrap.Children.Add(mqttTestBtn);
        Grid.SetRow(testWrap, 14);
        Grid.SetColumn(testWrap, 1);
        table.Children.Add(testWrap);

        // Status Label
        _mqttStatusLabel = new TextBlock
        {
            Text = "",
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0),
        };
        Grid.SetRow(_mqttStatusLabel, 15);
        Grid.SetColumn(_mqttStatusLabel, 0);
        Grid.SetColumnSpan(_mqttStatusLabel, 2);
        table.Children.Add(_mqttStatusLabel);

        _mqttEnabledCheck.IsCheckedChanged += (s, e) => UpdateMqttStatusLabel();

        stack.Children.Add(table);
        return MakeSectionPanel(stack);
    }

    // ─── Section 7: ⚡ Quick Actions ───
    private Control BuildQuickActionsSection()
    {
        var stack = new StackPanel { Spacing = 0 };
        stack.Children.Add(MakeSectionHeader("⚡ " + Localization.Get("settings_quickactions")));

        // Detaillierte Beschreibung oben in der Section
        stack.Children.Add(new TextBlock
        {
            Text = Localization.Get("desc_quickactions_intro"),
            FontSize = 11,
            Foreground = DescGrayDark,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 8),
            Tag = "desc",
        });

        // Load Entities Button + Beschreibung
        stack.Children.Add(MakeButton("📥 " + Localization.Get("settings_load_entities", "Entities laden"),
            SuccessGreen, OnLoadEntities, Localization.Get("tooltip_load_entities")));
        stack.Children.Add(new TextBlock
        {
            Text = Localization.Get("desc_load_entities"),
            FontSize = 11,
            Foreground = DescGrayDark,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 8),
            Tag = "desc",
        });

        // Entity ListBox (volle Breite)
        _qaList = new ListBox
        {
            MinHeight = 200,
            MaxHeight = 280,
            Margin = new Thickness(0, 0, 0, 8),
        };
        stack.Children.Add(_qaList);

        // Add / Edit / Remove Buttons
        var editPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        editPanel.Children.Add(MakeButton("➕ " + Localization.Get("settings_qa_add", "Hinzufügen"), SuccessGreen, OnAddQuickAction, Localization.Get("tooltip_add_qa")));
        editPanel.Children.Add(MakeButton("✏️ " + Localization.Get("settings_qa_edit", "Bearbeiten"), new SolidColorBrush(Color.FromArgb(255, 100, 100, 100)), OnEditQuickAction, Localization.Get("tooltip_edit_qa")));
        editPanel.Children.Add(MakeButton("🗑️ " + Localization.Get("settings_qa_remove", "Entfernen"), WarningOrange, OnRemoveQuickAction, Localization.Get("tooltip_remove_qa")));
        stack.Children.Add(editPanel);

        return MakeSectionPanel(stack);
    }

    // ═══════════════════════════════════════════════════════
    // BOTTOM BAR — Status (links), Reconnect + Save (rechts), 56px hoch
    // ═══════════════════════════════════════════════════════

    private void BuildBottomBar()
    {
        _statusLabel = new TextBlock
        {
            Text = "",
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        };

        var reconnectBtn = MakeButton("🔄 " + Localization.Get("settings_reconnect", "Neu verbinden"),
            new SolidColorBrush(Color.FromArgb(255, 0, 100, 180)), OnReconnectClicked,
            Localization.Get("tooltip_reconnect"));
        var saveBtn = MakeButton("💾 " + Localization.Get("settings_save"), AccentBlue, OnSave,
            Localization.Get("tooltip_save"));

        var layout = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), Margin = new Thickness(16, 0) };
        Grid.SetColumn(_statusLabel, 0);
        _statusLabel.Margin = new Thickness(0, 0, 16, 0);
        Grid.SetColumn(reconnectBtn, 1);
        Grid.SetColumn(saveBtn, 2);
        layout.Children.Add(_statusLabel);
        layout.Children.Add(reconnectBtn);
        layout.Children.Add(saveBtn);

        _bottomPanel = new Border
        {
            MinHeight = 56,
            Padding = new Thickness(0, 8, 0, 8),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = layout,
        };
        DockPanel.SetDock(_bottomPanel, Dock.Bottom);
    }

    // ═══════════════════════════════════════════════════════
    // QUICK ACTIONS LOGIK
    // ═══════════════════════════════════════════════════════

    private List<QuickAction> GetCurrentQuickActions()
    {
        var actions = new List<QuickAction>();
        try
        {
            var arr = JsonDocument.Parse(_config.QuickActions).RootElement;
            foreach (var item in arr.EnumerateArray())
            {
                var entityId = item.TryGetProperty("entityId", out var eid) ? eid.GetString() ?? "" : "";
                var name = item.TryGetProperty("name", out var n) ? n.GetString() ?? entityId : entityId;
                if (!string.IsNullOrEmpty(entityId))
                    actions.Add(new QuickAction(entityId, name));
            }
        }
        catch { }
        return actions;
    }

    private void LoadQuickActionsList()
    {
        _qaList.Items.Clear();
        var actions = GetCurrentQuickActions();
        foreach (var a in actions)
            _qaList.Items.Add($"{a.Name} ({a.EntityId})");
    }

    // ═══════════════════════════════════════════════════════
    // BUTTON HANDLERS
    // ═══════════════════════════════════════════════════════

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        // URL validieren
        var url = _urlBox.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(url))
        {
            ShowValidationMessage(Localization.Get("validation_url_empty"), Localization.Get("validation_title"));
            _urlBox.Focus();
            return;
        }
        if (!url.StartsWith("http://") && !url.StartsWith("https://"))
        {
            ShowValidationMessage(Localization.Get("validation_url_invalid"), Localization.Get("validation_title"));
            _urlBox.Focus();
            return;
        }

        // Token validieren
        if (string.IsNullOrWhiteSpace(_tokenBox.Text?.Trim()))
        {
            ShowValidationMessage(Localization.Get("validation_token_empty"), Localization.Get("validation_title"));
            _tokenBox.Focus();
            return;
        }

        _config.HaUrl = _urlBox.Text!.Trim();
        _config.HaToken = _tokenBox.Text!.Trim();
        _config.VerifySsl = _sslCheck.IsChecked ?? false;
        _config.Autostart = _autostartCheck.IsChecked ?? false;
        _config.SensorInterval = Math.Max(10, (int)(_intervalBox.Value ?? 30));  // Minimum 10s erzwingen
        _config.UpdateChannel = _updateChannelBox.SelectedIndex == 1 ? "prerelease" : "stable";

        if (_languageBox.SelectedIndex >= 0 && _languageBox.SelectedIndex < Localization.AvailableLanguages.Count)
            _config.Language = Localization.AvailableLanguages[_languageBox.SelectedIndex];

        _config.Theme = _themeBox.SelectedIndex switch { 1 => "light", 2 => "dark", _ => "system" };

        // Benachrichtigungs-Position
        _config.NotificationPosition = _notifPosBox.SelectedIndex switch
        {
            1 => "bottom_right", 2 => "top_left", 3 => "top_right", _ => "bottom_left"
        };
        _config.NotificationMonitor = Math.Max(0, _notifMonitorBox.SelectedIndex);

        // Hotkeys
        _config.HotkeyModifiers = _hotkeyModBox.SelectedIndex switch
        {
            0 => "ctrl_shift", 1 => "ctrl_alt", 2 => "ctrl", 3 => "alt", 4 => "shift", 5 => "none", _ => "ctrl_shift"
        };
        _config.HotkeyKey = _hotkeyKeyBox.SelectedItem?.ToString() ?? "H";

        _config.HotkeyDashboardModifiers = _hotkeyDashModBox.SelectedIndex switch
        {
            0 => "ctrl_shift", 1 => "ctrl_alt", 2 => "ctrl", 3 => "alt", 4 => "shift", 5 => "none", _ => "ctrl_shift"
        };
        _config.HotkeyDashboardKey = _hotkeyDashKeyBox.SelectedItem?.ToString() ?? "D";

        _config.HotkeySettingsModifiers = _hotkeySettingsModBox.SelectedIndex switch
        {
            0 => "ctrl_shift", 1 => "ctrl_alt", 2 => "ctrl", 3 => "alt", 4 => "shift", 5 => "none", _ => "ctrl_shift"
        };
        _config.HotkeySettingsKey = _hotkeySettingsKeyBox.SelectedItem?.ToString() ?? "S";

        // MQTT-Einstellungen
        _config.MqttEnabled = _mqttEnabledCheck.IsChecked ?? false;
        _config.MqttBroker = _mqttBrokerBox.Text?.Trim() ?? "";
        if (int.TryParse(_mqttPortBox.Text?.Trim(), out var mqttPort))
            _config.MqttPort = mqttPort;
        _config.MqttUsername = _mqttUserBox.Text?.Trim() ?? "";
        _config.MqttPassword = _mqttPassBox.Text ?? "";
        _config.MqttUseSsl = _mqttSslCheck.IsChecked ?? false;
        _config.MqttAutoConfigured = false; // manuelles Speichern
        _config.MqttBrokerFallback = _mqttFallbackBox.Text?.Trim() ?? "";

        _config.Save();
        if (_config.Autostart) Autostart.Enable(); else Autostart.Disable();
        ApplyTheme(_config.Theme);
        // Sprache neu laden, falls sie geändert wurde
        Localization.LoadLanguage(_config.Language);
        _statusLabel.Text = $"✓ {Localization.Get("settings_saved")}";
    }

    /// <summary>
    /// Neu verbinden mit HA — setzt auch Login-Block zurück falls blockiert.
    /// Das ist der EINZIGE Reconnect-Button — keine Duplikate.
    /// </summary>
    private void OnReconnectClicked(object? sender, RoutedEventArgs e)
    {
        _statusLabel.Text = Localization.Get("status_reconnecting");
        Task.Run(() =>
        {
            try { _onReconnect.Invoke(); }
            catch { }
        });
        Dispatcher.UIThread.Post(() => _statusLabel.Text = Localization.Get("status_reconnect_done"));
    }

    private async void OnResetDeviceId(object? sender, RoutedEventArgs e)
    {
        var confirmed = await ShowConfirmDialog(
            Localization.Get("settings_reset_device_confirm") + Localization.Get("settings_extra_note_device_reset"),
            Localization.Get("settings_reset_device"));
        if (confirmed)
        {
            (_api ?? new HaApiClient(Config.GetConfigDir())).ResetDeviceId();
            _statusLabel.Text = $"✓ {Localization.Get("settings_reset_device_done")}";
        }
    }

    private async void OnReRegisterSensors(object? sender, RoutedEventArgs e)
    {
        var confirmed = await ShowConfirmDialog(
            Localization.Get("settings_reregister_confirm") + Localization.Get("settings_extra_note_reregister"),
            Localization.Get("settings_reregister_sensors"));
        if (confirmed)
        {
            Task.Run(() =>
            {
                try { DeskLinkApp.ReRegisterSensors(); }
                catch { }
            });
            _statusLabel.Text = $"✓ {Localization.Get("settings_reregister_done")}";
        }
    }

    private async void OnLoadEntities(object? sender, RoutedEventArgs e)
    {
        var api = _api ?? new HaApiClient(Config.GetConfigDir(), _config.VerifySsl);
        try { api.LoadRegistration(); }
        catch { }

        _statusLabel.Text = Localization.Get("status_loading_entities");
        try
        {
            _entities = await api.GetEntitiesAsync();
            _entities = _entities.OrderBy(x => x.entityId).ToList();
            _statusLabel.Text = Localization.Get("status_entities_loaded", _entities.Count);
        }
        catch (Exception ex)
        {
            _statusLabel.Text = Localization.Get("status_error", ex.Message);
        }
    }

    private void OnAddQuickAction(object? sender, RoutedEventArgs e)
    {
        if (_entities.Count == 0)
        {
            ShowValidationMessage(Localization.Get("settings_load_entities_first"), "HA DeskLink");
            return;
        }
        _ = ShowEditQuickActionDialogAsync(null);
    }

    private void OnEditQuickAction(object? sender, RoutedEventArgs e)
    {
        if (_qaList.SelectedIndex < 0)
        {
            ShowValidationMessage(Localization.Get("settings_qa_select_first"), "HA DeskLink");
            return;
        }

        var actions = GetCurrentQuickActions();
        var idx = _qaList.SelectedIndex;
        if (idx >= actions.Count) return;
        _ = ShowEditQuickActionDialogAsync(idx);
    }

    private void OnRemoveQuickAction(object? sender, RoutedEventArgs e)
    {
        if (_qaList.SelectedIndex < 0)
        {
            ShowValidationMessage(Localization.Get("settings_qa_select_first"), "HA DeskLink");
            return;
        }

        var actions = GetCurrentQuickActions();
        var idx = _qaList.SelectedIndex;
        if (idx < actions.Count)
        {
            actions.RemoveAt(idx);
            _config.QuickActions = JsonSerializer.Serialize(actions, _jsonOpts);
            _config.Save();
            LoadQuickActionsList();
        }
    }

    /// <summary>
    /// Add (idx == null) or edit (idx != null) a Quick Action in a modal dialog.
    /// </summary>
    private async Task ShowEditQuickActionDialogAsync(int? idx)
    {
        var actions = GetCurrentQuickActions();
        var action = idx != null && idx < actions.Count ? actions[idx!.Value] : null;

        var entityCombo = new ComboBox { MinHeight = 32, HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var (entityId, friendlyName) in _entities)
            entityCombo.Items.Add(new EntityItem(entityId, friendlyName));
        if (entityCombo.Items.Count > 0)
            entityCombo.SelectedIndex = 0;

        var nameBox = new TextBox { MinHeight = 32, Text = action?.Name ?? "" };
        entityCombo.SelectionChanged += (s, e2) =>
        {
            if (entityCombo.SelectedItem is EntityItem item)
                nameBox.Text = item.FriendlyName;
        };

        var table = new Grid { Margin = new Thickness(16), RowDefinitions = new RowDefinitions("Auto,8,Auto,8,Auto,12,Auto") };
        table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var lblEntity = new TextBlock { Text = Localization.Get("settings_qa_entity", "Entity:"), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetRow(lblEntity, 0); Grid.SetColumn(lblEntity, 0);
        Grid.SetRow(entityCombo, 0); Grid.SetColumn(entityCombo, 1);

        var lblName = new TextBlock { Text = Localization.Get("settings_qa_name", "Name:"), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetRow(lblName, 2); Grid.SetColumn(lblName, 0);
        Grid.SetRow(nameBox, 2); Grid.SetColumn(nameBox, 1);

        var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        var saveBtn = MakeButton("💾 " + Localization.Get("settings_save", "Speichern"), AccentBlue, (s, e2) => { });
        saveBtn.MinWidth = 100;
        var cancelBtn = MakeButton(Localization.Get("setup_cancel", "Abbrechen"), new SolidColorBrush(Color.FromArgb(255, 100, 100, 100)), (s, e2) => { });
        cancelBtn.MinWidth = 100;
        btnPanel.Children.Add(saveBtn);
        btnPanel.Children.Add(cancelBtn);
        Grid.SetRow(btnPanel, 6); Grid.SetColumnSpan(btnPanel, 2);

        table.Children.Add(lblEntity);
        table.Children.Add(entityCombo);
        table.Children.Add(lblName);
        table.Children.Add(nameBox);
        table.Children.Add(btnPanel);

        // Select current entity when editing
        if (action != null)
        {
            for (int i = 0; i < entityCombo.Items.Count; i++)
            {
                if (entityCombo.Items[i] is EntityItem ei && ei.EntityId == action.EntityId)
                {
                    entityCombo.SelectedIndex = i;
                    break;
                }
            }
        }

        var dialog = new Window
        {
            Title = action == null
                ? Localization.Get("settings_qa_add", "Quick Action hinzufügen")
                : Localization.Get("settings_qa_edit", "Quick Action bearbeiten"),
            Width = 450,
            Height = 240,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            Content = table,
        };
        ApplyThemeToWindow(dialog, _config.Theme);

        var closed = new TaskCompletionSource<bool>();
        saveBtn.Click += (s, e2) =>
        {
            if (entityCombo.SelectedItem is EntityItem item)
            {
                var name = string.IsNullOrEmpty(nameBox.Text) ? item.FriendlyName : nameBox.Text;
                if (idx != null && idx < actions.Count)
                    actions[idx!.Value] = new QuickAction(item.EntityId, name);
                else
                    actions.Add(new QuickAction(item.EntityId, name));
                _config.QuickActions = JsonSerializer.Serialize(actions, _jsonOpts);
                _config.Save();
                LoadQuickActionsList();
                closed.TrySetResult(true);
            }
        };
        cancelBtn.Click += (s, e2) => closed.TrySetResult(false);
        dialog.Closed += (s, e2) => closed.TrySetResult(false);

        await dialog.ShowDialog(this);
        await closed.Task;
    }

    private async void ShowValidationMessage(string message, string title)
    {
        var dialog = new Window
        {
            Title = title,
            Width = 380,
            Height = 160,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            Content = new Border
            {
                Padding = new Thickness(16),
                Child = new StackPanel
                {
                    Spacing = 12,
                    Children =
                    {
                        new TextBlock { Text = "⚠️ " + message, TextWrapping = TextWrapping.Wrap },
                        new Button { Content = "OK", HorizontalAlignment = HorizontalAlignment.Right, MinWidth = 90, Name = "BtnOk" }
                    }
                }
            }
        };
        ApplyThemeToWindow(dialog, _config.Theme);
        var okBtn = (Button)((StackPanel)((Border)dialog.Content).Child).Children.OfType<Button>().First();
        okBtn.Click += (s, e) => dialog.Close();
        await dialog.ShowDialog(this);
    }

    private async Task<bool> ShowConfirmDialog(string message, string title)
    {
        var result = false;
        var okBtn = new Button { Content = Localization.Get("setup_connect", "OK"), MinWidth = 90 };
        var cancelBtn = new Button { Content = Localization.Get("setup_cancel", "Abbrechen"), MinWidth = 90 };

        var dialog = new Window
        {
            Title = title,
            Width = 420,
            Height = 200,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            Content = new Border
            {
                Padding = new Thickness(16),
                Child = new StackPanel
                {
                    Spacing = 14,
                    Children =
                    {
                        new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                        new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            Spacing = 8,
                            HorizontalAlignment = HorizontalAlignment.Right,
                            Children = { okBtn, cancelBtn }
                        }
                    }
                }
            }
        };
        ApplyThemeToWindow(dialog, _config.Theme);

        var closed = new TaskCompletionSource<bool>();
        okBtn.Click += (s, e) => { result = true; dialog.Close(); };
        cancelBtn.Click += (s, e) => dialog.Close();
        dialog.Closed += (s, e) => closed.TrySetResult(result);

        await dialog.ShowDialog(this);
        return await closed.Task;
    }

    // ═══════════════════════════════════════════════════════
    // THEME
    // ═══════════════════════════════════════════════════════

    private class EntityItem
    {
        public string EntityId { get; }
        public string FriendlyName { get; }
        public EntityItem(string entityId, string friendlyName) { EntityId = entityId; FriendlyName = friendlyName; }
        public override string ToString() => $"{FriendlyName} ({EntityId})";
    }

    private void ApplyThemeToWindow(Window dialog, string theme)
    {
        bool dark = theme == "dark" || (theme == "system" && IsSystemDark());
        var bg = dark ? DarkBg : LightBg;
        var fg = dark ? DarkFg : LightFg;
        var inputBg = dark ? DarkInput : LightInput;

        dialog.Background = bg;
        dialog.Foreground = fg;

        foreach (var c in GetAllControls(dialog))
        {
            if (c is TextBox || c is ComboBox || c is NumericUpDown || c is ListBox)
            {
                if (c is TemplatedControl tc)
                {
                    tc.Background = inputBg;
                    tc.Foreground = fg;
                }
            }
            else if (c is TextBlock tb)
            {
                tb.Foreground = IsDescLabel(tb) ? (dark ? DescGrayDark : DescGrayLight) : fg;
            }
        }
    }

    private void ApplyTheme(string theme)
    {
        _isDark = theme == "dark" || (theme == "system" && IsSystemDark());
        var bg = _isDark ? DarkBg : LightBg;
        var fg = _isDark ? DarkFg : LightFg;
        var inputBg = _isDark ? DarkInput : LightInput;
        var sidebarBg = _isDark ? DarkBg : LightSidebarBg;
        var bottomBg = _isDark ? DarkSectionBg : LightBottomBg;

        // Sidebar Hover/Normal Farben für PointerEnter/Leave
        _sidebarNormalBg = sidebarBg;
        _sidebarHoverBg = _isDark ? new SolidColorBrush(Color.FromArgb(255, 48, 48, 48)) : new SolidColorBrush(Color.FromArgb(255, 220, 220, 220));

        // Window
        Background = bg;
        Foreground = fg;

        // Sidebar
        _sidebarPanel.Background = sidebarBg;

        // Sidebar-Header
        if (_sidebarPanel.Child is DockPanel dp && dp.Children.OfType<TextBlock>().FirstOrDefault() is TextBlock header)
            header.Foreground = fg;

        // Inhaltsbereich
        _contentPanel.Background = bg;

        // Bottom Bar
        _bottomPanel.Background = bottomBg;
        _bottomPanel.BorderBrush = _isDark ? new SolidColorBrush(Color.FromArgb(255, 60, 60, 60)) : new SolidColorBrush(Color.FromArgb(255, 200, 200, 200));

        // Sidebar-Buttons aktualisieren (ausgewählter = AccentBlue, weiße Schrift)
        for (int i = 0; i < _sidebarButtons.Count; i++)
        {
            if (i == _currentSection)
            {
                _sidebarButtons[i].Background = AccentBlue;
                _sidebarButtons[i].Foreground = Brushes.White;
            }
            else
            {
                _sidebarButtons[i].Background = sidebarBg;
                _sidebarButtons[i].Foreground = fg;
            }
        }

        // Alle Controls durchlaufen und einfärben
        foreach (var c in GetAllControls(this))
        {
            if (c is TextBox || c is ComboBox || c is NumericUpDown || c is ListBox)
            {
                if (c is TemplatedControl tc)
                {
                    tc.Background = inputBg;
                    tc.Foreground = fg;
                }
            }
            else if (c is TextBlock tb)
            {
                // Beschreibungs-Labels: Theme-abhängige graue Farbe
                if (IsDescLabel(tb))
                    tb.Foreground = _isDark ? DescGrayDark : DescGrayLight;
                else
                    tb.Foreground = fg;
            }
            else if (c is CheckBox cb)
            {
                cb.Foreground = fg;
            }
            // Farbige Buttons behalten weiße Schrift
        }
    }

    private static bool IsDescLabel(TextBlock tb) =>
        (tb.Tag as string) == "desc";

    private static bool IsSystemDark()
    {
        // Linux: check the GTK/LibreOffice/flatpak dark-mode convention via gsettings,
        // fall back to false (light) when not determinable.
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "gsettings",
                Arguments = "get org.gnome.desktop.interface color-scheme",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc != null)
            {
                var output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit(1500);
                return output.Contains("dark", StringComparison.OrdinalIgnoreCase);
            }
        }
        catch { }
        return false;
    }

    private static IEnumerable<Control> GetAllControls(Control container)
    {
        var logical = container.GetLogicalDescendants().OfType<Control>();
        foreach (var c in logical)
            yield return c;
    }

    private void LoadSettings()
    {
        _urlBox.Text = _config.HaUrl;
        _tokenBox.Text = _config.HaToken;
        _sslCheck.IsChecked = _config.VerifySsl;
        _autostartCheck.IsChecked = _config.Autostart || Autostart.IsEnabled();
        _intervalBox.Value = Math.Max(10, _config.SensorInterval);  // Minimum 10s erzwingen
        _updateChannelBox.SelectedIndex = _config.UpdateChannel == "prerelease" ? 1 : 0;

        var currentLangIndex = Localization.AvailableLanguages.IndexOf(_config.Language);
        if (currentLangIndex < 0) currentLangIndex = 0;
        _languageBox.SelectedIndex = currentLangIndex;

        _themeBox.SelectedIndex = _config.Theme switch { "light" => 1, "dark" => 2, _ => 0 };

        // Benachrichtigungs-Position
        _notifPosBox.SelectedIndex = _config.NotificationPosition switch
        {
            "bottom_right" => 1, "top_left" => 2, "top_right" => 3, _ => 0
        };

        // Benachrichtigungs-Monitor
        if (_notifMonitorBox.Items.Count > 0)
            _notifMonitorBox.SelectedIndex = Math.Min(_config.NotificationMonitor, _notifMonitorBox.Items.Count - 1);

        // Hotkeys laden
        _hotkeyModBox.SelectedIndex = _config.HotkeyModifiers switch
        {
            "ctrl_shift" => 0, "ctrl_alt" => 1, "ctrl" => 2, "alt" => 3, "shift" => 4, "none" => 5, _ => 0
        };
        var keyIndex = _hotkeyKeyBox.Items.IndexOf(_config.HotkeyKey.ToUpper());
        _hotkeyKeyBox.SelectedIndex = keyIndex >= 0 ? keyIndex : 0;

        _hotkeyDashModBox.SelectedIndex = _config.HotkeyDashboardModifiers switch
        {
            "ctrl_shift" => 0, "ctrl_alt" => 1, "ctrl" => 2, "alt" => 3, "shift" => 4, "none" => 5, _ => 0
        };
        var dashKeyIndex = _hotkeyDashKeyBox.Items.IndexOf(_config.HotkeyDashboardKey.ToUpper());
        _hotkeyDashKeyBox.SelectedIndex = dashKeyIndex >= 0 ? dashKeyIndex : 0;

        _hotkeySettingsModBox.SelectedIndex = _config.HotkeySettingsModifiers switch
        {
            "ctrl_shift" => 0, "ctrl_alt" => 1, "ctrl" => 2, "alt" => 3, "shift" => 4, "none" => 5, _ => 0
        };
        var settingsKeyIndex = _hotkeySettingsKeyBox.Items.IndexOf(_config.HotkeySettingsKey.ToUpper());
        _hotkeySettingsKeyBox.SelectedIndex = settingsKeyIndex >= 0 ? settingsKeyIndex : 0;

        // MQTT-Einstellungen laden
        _mqttEnabledCheck.IsChecked = _config.MqttEnabled;
        _mqttBrokerBox.Text = _config.MqttBroker;
        _mqttPortBox.Text = _config.MqttPort.ToString();
        _mqttUserBox.Text = _config.MqttUsername;
        _mqttPassBox.Text = _config.MqttPassword;
        _mqttSslCheck.IsChecked = _config.MqttUseSsl;
        _mqttFallbackBox.Text = _config.MqttBrokerFallback ?? "";
        UpdateMqttStatusLabel();
    }

    private void UpdateMqttStatusLabel()
    {
        if (_mqttEnabledCheck.IsChecked != true)
        {
            _mqttStatusLabel.Text = "○ " + Localization.Get("mqtt_disabled");
            _mqttStatusLabel.Foreground = DescGrayDark;
        }
        else if (_config.MqttBroker.Length > 0)
        {
            _mqttStatusLabel.Text = "● " + Localization.Get("mqtt_connected") + $" ({_config.MqttBroker}:{_config.MqttPort})";
            _mqttStatusLabel.Foreground = SuccessGreen;
        }
        else
        {
            _mqttStatusLabel.Text = "● " + Localization.Get("mqtt_disconnected");
            _mqttStatusLabel.Foreground = WarningOrange;
        }
    }

    private async void OnMqttTestConnection(object? sender, RoutedEventArgs e)
    {
        var broker = _mqttBrokerBox.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(broker))
        {
            ShowValidationMessage(Localization.Get("validation_mqtt_broker_empty"), Localization.Get("validation_title"));
            return;
        }

        if (!int.TryParse(_mqttPortBox.Text?.Trim(), out var port) || port < 1 || port > 65535)
        {
            ShowValidationMessage(Localization.Get("validation_port_range"), Localization.Get("validation_port_title"));
            return;
        }

        var btn = sender as Button;
        if (btn != null) btn.IsEnabled = false;
        _mqttStatusLabel.Text = "⏳ " + Localization.Get("mqtt_testing_status");
        _mqttStatusLabel.Foreground = DescGrayDark;

        try
        {
            var result = await MqttSetupHelper.TestConnectionAsync(broker, port,
                _mqttUserBox.Text?.Trim(), _mqttPassBox.Text, _mqttSslCheck.IsChecked ?? false);

            if (result)
            {
                _mqttStatusLabel.Text = $"✓ {Localization.Get("mqtt_test_success")} ({broker}:{port})";
                _mqttStatusLabel.Foreground = SuccessGreen;
            }
            else
            {
                _mqttStatusLabel.Text = $"✗ {Localization.Get("mqtt_test_failed")} ({broker}:{port})";
                _mqttStatusLabel.Foreground = DangerRed;
            }
        }
        catch (Exception ex)
        {
            _mqttStatusLabel.Text = $"✗ {Localization.Get("status_error", ex.Message)}";
            _mqttStatusLabel.Foreground = DangerRed;
        }
        finally
        {
            if (btn != null) btn.IsEnabled = true;
        }
    }

    /// <summary>
    /// Opens the settings window. If already open, activates it.
    /// </summary>
    public static void Open(Config config, Action onReconnect, HaApiClient? api = null)
    {
        if (_instance != null)
        {
            _instance.Activate();
            return;
        }
        _instance = new SettingsWindow(config, onReconnect, api);
        _instance.Show();
    }
}


/// <summary>
/// Quick Action model (entityId + display name) — shared with the Quick Action popup.
/// </summary>
public class QuickAction
{
    public string EntityId { get; set; } = "";
    public string Name { get; set; } = "";

    public QuickAction() { }

    public QuickAction(string entityId, string name)
    {
        EntityId = entityId;
        Name = name;
    }
}