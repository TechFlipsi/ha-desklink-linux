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
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace HaDeskLink;

/// <summary>
/// Desktop widget window — a frameless card that lives on the desktop layer,
/// below all normal windows (WidgetManager applies the EWMH hints via X11).
/// Three card types (WidgetConfig.Type):
///  - "sensor"       : shows one entity state as a large value
///  - "toggle"       : one entity with a toggle button
///  - "multi_toggle" : 2-4 entities, each with its own toggle button
/// Code-only UI matching MusicWindow/NotificationPopup styling (Phase D).
/// </summary>
public class WidgetWindow : Window
{
    // ── Dark navy palette (matching NotificationPopup) ──
    private static readonly IBrush BgBrush = SolidColorBrush.Parse("#16213E");
    private static readonly IBrush RowBrush = SolidColorBrush.Parse("#1B2B4D");
    private static readonly IBrush AccentBlueBrush = SolidColorBrush.Parse("#4285F4");
    private static readonly IBrush SuccessBrush = SolidColorBrush.Parse("#4CAF50");
    private static readonly IBrush GrayBrush = SolidColorBrush.Parse("#8C8CA0");
    private static readonly IBrush ContentBrush = SolidColorBrush.Parse("#C8C8D7");
    private static readonly IBrush OffBrush = SolidColorBrush.Parse("#3A3A55");

    private readonly WidgetConfig _cfg;
    private readonly HaApiClient _api;
    private readonly Action? _onAfterToggle;
    private readonly bool _testMode;

    /// <summary>Widget configuration this window was built from.</summary>
    public WidgetConfig Config => _cfg;

    // ── Sensor card controls ──
    private TextBlock _sensorValue = null!;
    private TextBlock _sensorUnit = null!;

    // ── Toggle card controls ──
    private TextBlock _toggleState = null!;
    private Button _toggleButton = null!;

    // ── Multi-toggle rows (entityId → row) ──
    private readonly Dictionary<string, ToggleRow> _rows = new();

    private sealed class ToggleRow
    {
        public string EntityId = "";
        public TextBlock Name = null!;
        public TextBlock State = null!;
        public Button Button = null!;
        public string CurrentState = "";
    }

    public WidgetWindow(WidgetConfig cfg, HaApiClient api, bool testMode = false, Action? onAfterToggle = null)
    {
        _cfg = cfg;
        _api = api;
        _testMode = testMode;
        _onAfterToggle = onAfterToggle;

        var isMulti = cfg.Type == "multi_toggle";
        Title = $"HA DeskLink – Widget: {(string.IsNullOrEmpty(cfg.Name) ? cfg.EntityId : cfg.Name)}";
        Width = isMulti ? 270 : 230;
        Height = cfg.Type switch
        {
            "multi_toggle" => 64 + 52 * Math.Max(2, cfg.Entities.Count),
            _ => 150,
        };

        // Frameless desktop card — never in taskbar, never steals focus on show.
        SystemDecorations = SystemDecorations.None;
        ShowInTaskbar = false;
        ShowActivated = false;
        CanResize = false;
        Topmost = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Background = Brushes.Transparent;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };

        Content = BuildCard();
        ApplyPosition();

        KeyDown += (s, e) => { if (_testMode && e.Key == Key.Escape) Close(); };
    }

    // ═══════════════════════════════════════════════════════
    // UI
    // ═══════════════════════════════════════════════════════

    private Control BuildCard()
    {
        var dock = new DockPanel();

        // ── Header: widget name (+ close ✕ in test mode) ──
        var header = BuildHeader();
        DockPanel.SetDock(header, Dock.Top);
        dock.Children.Add(header);

        // ── Body per type ──
        dock.Children.Add(_cfg.Type switch
        {
            "multi_toggle" => BuildMultiToggleBody(),
            "toggle" => BuildToggleBody(),
            _ => BuildSensorBody(),
        });

        return new Border
        {
            Background = BgBrush,
            CornerRadius = new CornerRadius(10),
            ClipToBounds = true,
            Margin = new Thickness(6),
            Padding = new Thickness(12, 8, 12, 10),
            Child = dock
        };
    }

    private Control BuildHeader()
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };

        var nameText = new TextBlock
        {
            Text = string.IsNullOrEmpty(_cfg.Name) ? (_cfg.Type == "multi_toggle" ? "Widgets" : _cfg.EntityId) : _cfg.Name,
            FontSize = 12,
            FontWeight = FontWeight.Bold,
            Foreground = GrayBrush,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(nameText, 0);
        grid.Children.Add(nameText);

        if (_testMode)
        {
            var closeBtn = new Button
            {
                Content = "✕",
                FontSize = 11,
                Background = Brushes.Transparent,
                Foreground = GrayBrush,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(4, 0),
                VerticalAlignment = VerticalAlignment.Top
            };
            closeBtn.Click += (s, e) => Close();
            Grid.SetColumn(closeBtn, 1);
            grid.Children.Add(closeBtn);
        }

        return new Border
        {
            Child = grid,
            Padding = new Thickness(0, 0, 0, 6)
        };
    }

    // ─── Sensor card: big live value ───
    private Control BuildSensorBody()
    {
        _sensorValue = new TextBlock
        {
            Text = "—",
            FontSize = 30,
            FontWeight = FontWeight.Bold,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        _sensorUnit = new TextBlock
        {
            Text = "",
            FontSize = 13,
            Foreground = GrayBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 2, 0, 0)
        };

        return new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Children = { _sensorValue, _sensorUnit }
        };
    }

    // ─── Toggle card: one entity, one big switch ───
    private Control BuildToggleBody()
    {
        _toggleState = new TextBlock
        {
            Text = "…",
            FontSize = 13,
            Foreground = ContentBrush,
            VerticalAlignment = VerticalAlignment.Center
        };

        _toggleButton = new Button
        {
            Content = "⏻",
            FontSize = 22,
            Width = 48,
            Height = 48,
            CornerRadius = new CornerRadius(24),
            Background = OffBrush,
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        _toggleButton.Click += (s, e) => _ = ToggleAsync(_cfg.EntityId);

        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 14,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { _toggleButton, _toggleState }
        };
    }

    // ─── Multi-toggle card: one row per entity (2-4) ───
    private Control BuildMultiToggleBody()
    {
        var panel = new StackPanel { Spacing = 6 };
        foreach (var entityId in _cfg.Entities)
        {
            if (string.IsNullOrEmpty(entityId)) continue;
            var row = new ToggleRow { EntityId = entityId };

            row.Name = new TextBlock
            {
                Text = entityId,
                FontSize = 12,
                Foreground = Brushes.White,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center
            };

            row.State = new TextBlock
            {
                Text = "…",
                FontSize = 11,
                Foreground = GrayBrush,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };

            row.Button = new Button
            {
                Content = "⏻",
                FontSize = 13,
                Width = 32,
                Height = 32,
                CornerRadius = new CornerRadius(16),
                Background = OffBrush,
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0)
            };
            row.Button.Click += (s, e) => _ = ToggleAsync(row.EntityId);

            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto") };
            Grid.SetColumn(row.Name, 0);
            Grid.SetColumn(row.State, 1);
            Grid.SetColumn(row.Button, 2);
            grid.Children.Add(row.Name);
            grid.Children.Add(row.State);
            grid.Children.Add(row.Button);

            panel.Children.Add(new Border
            {
                Background = RowBrush,
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 6),
                Child = grid
            });

            _rows[entityId] = row;
        }
        return panel;
    }

    // ═══════════════════════════════════════════════════════
    // STATE UPDATES (called by WidgetManager polling)
    // ═══════════════════════════════════════════════════════

    /// <summary>
    /// Push a fresh entity state into the card UI. Safe to call repeatedly.
    /// </summary>
    public void UpdateEntity(string entityId, string state, string friendlyName, string unit)
    {
        if (_cfg.Type == "multi_toggle")
        {
            if (_rows.TryGetValue(entityId, out var row))
            {
                row.CurrentState = state;
                row.State.Text = state;
                row.Button.Background = IsOn(state) ? SuccessBrush : OffBrush;
                if (string.IsNullOrEmpty(_cfg.Name) && !string.IsNullOrEmpty(friendlyName) && row.Name.Text == entityId)
                    row.Name.Text = friendlyName;
            }
            return;
        }

        if (entityId != _cfg.EntityId) return;

        switch (_cfg.Type)
        {
            case "sensor":
                _sensorValue.Text = string.IsNullOrEmpty(state) ? "—" : state;
                _sensorUnit.Text = unit ?? "";
                break;
            case "toggle":
                _toggleState.Text = state;
                _toggleButton.Background = IsOn(state) ? SuccessBrush : OffBrush;
                break;
        }
    }

    private static bool IsOn(string state) =>
        state.Equals("on", StringComparison.OrdinalIgnoreCase) ||
        state.Equals("true", StringComparison.OrdinalIgnoreCase) ||
        state.Equals("1", StringComparison.OrdinalIgnoreCase) ||
        state.Equals("open", StringComparison.OrdinalIgnoreCase) ||
        state.Equals("playing", StringComparison.OrdinalIgnoreCase) ||
        state.Equals("home", StringComparison.OrdinalIgnoreCase);

    private async Task ToggleAsync(string entityId)
    {
        if (string.IsNullOrEmpty(entityId)) return;
        try
        {
            await _api.ToggleEntityAsync(entityId);
            // Optimistic flip; the manager poll refreshes the real state shortly after.
            if (_cfg.Type == "multi_toggle" && _rows.TryGetValue(entityId, out var row))
            {
                row.CurrentState = IsOn(row.CurrentState) ? "off" : "on";
                row.State.Text = row.CurrentState;
                row.Button.Background = IsOn(row.CurrentState) ? SuccessBrush : OffBrush;
            }
            else if (_cfg.Type == "toggle" && entityId == _cfg.EntityId)
            {
                var nowOn = !IsOn(_toggleState.Text ?? "");
                _toggleState.Text = nowOn ? "on" : "off";
                _toggleButton.Background = nowOn ? SuccessBrush : OffBrush;
            }
            _onAfterToggle?.Invoke();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Widget] Toggle failed for {entityId}: {ex.Message}");
        }
    }

    // ═══════════════════════════════════════════════════════
    // POSITIONING (multi-monitor, NotificationPopup pattern)
    // ═══════════════════════════════════════════════════════

    /// <summary>
    /// Position the widget on the configured monitor at the configured offset,
    /// clamped so the card stays inside the monitor's working area.
    /// </summary>
    public void ApplyPosition()
    {
        var screens = Screens.All;
        if (screens == null || screens.Count == 0) return;

        var monitor = Math.Clamp(_cfg.Monitor, 0, screens.Count - 1);
        var wa = screens[monitor].WorkingArea;

        var x = wa.X + _cfg.OffsetX;
        var y = wa.Y + _cfg.OffsetY;

        // Clamp into the working area so the widget can never be positioned off-screen
        // (both edges: negative offsets would push it out on the left/top side)
        var maxX = wa.X + wa.Width - (int)Width;
        var maxY = wa.Y + wa.Height - (int)Height;
        if (x > maxX) x = Math.Max(wa.X, maxX);
        if (y > maxY) y = Math.Max(wa.Y, maxY);
        if (x < wa.X) x = wa.X;
        if (y < wa.Y) y = wa.Y;

        Position = new PixelPoint(x, y);
    }

    /// <summary>
    /// Re-Apply der X11-Desktop-Layer-Hints bei JEDEM erneuten Anzeigen:
    /// Hide/Show re-mapt das native Fenster und wirft die Properties zurück
    /// (Avalonia #16115). IsVisible→true ist der zuverlässige Hook — das
    /// Opened-Event feuert nicht in jedem Hide/Show-Zyklus. Idempotent
    /// (PropModeReplace), daher gefahrlos mehrfach aufrufbar.
    /// </summary>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty && (bool)change.NewValue!)
            WidgetManager.WidgetManagerX11.ApplyHints(this, _cfg);
    }
}