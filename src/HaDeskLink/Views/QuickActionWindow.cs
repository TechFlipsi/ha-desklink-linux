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
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace HaDeskLink.Views;

/// <summary>
/// Quick Actions popup window — Avalonia port of the Windows QuickActionWindow.
/// Shows HA entity toggle buttons, triggered by the configured hotkey.
/// Closes on Escape or when losing focus.
/// </summary>
public class QuickActionWindow : Window
{
    private readonly List<QuickAction> _actions;
    private readonly HaApiClient _api;
    private static QuickActionWindow? _instance;

    private static readonly IBrush BgBrush = new SolidColorBrush(Color.FromArgb(255, 26, 26, 46));
    private static readonly IBrush PanelBrush = new SolidColorBrush(Color.FromArgb(255, 22, 33, 62));
    private static readonly IBrush AccentBrush = new SolidColorBrush(Color.FromArgb(255, 15, 52, 96));
    private static readonly IBrush SuccessBrush = new SolidColorBrush(Color.FromArgb(255, 76, 175, 80));
    private static readonly IBrush DangerBrush = new SolidColorBrush(Color.FromArgb(255, 233, 69, 96));

    public QuickActionWindow(List<QuickAction> actions, HaApiClient api)
    {
        _actions = actions;
        _api = api;

        Title = "HA DeskLink - Quick Actions";
        Width = 320;
        SizeToContent = SizeToContent.Height;
        MaxHeight = 500;
        MinHeight = 120;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        CanResize = false;
        ShowInTaskbar = false;
        Topmost = true;
        Background = BgBrush;

        BuildContent();

        KeyDown += OnKeyDown;
        Deactivated += (s, e) => Close();
        Closed += (s, e) => _instance = null;
    }

    private void BuildContent()
    {
        var panel = new StackPanel { Margin = new Thickness(12), Spacing = 8 };

        if (_actions.Count == 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = Localization.Get("quickactions_empty", "Keine Quick Actions konfiguriert"),
                Foreground = Brushes.White,
                FontSize = 13,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 20, 0, 20),
                TextWrapping = TextWrapping.Wrap,
            });
        }
        else
        {
            foreach (var action in _actions)
            {
                var btn = new Button
                {
                    Content = action.Name,
                    MinHeight = 38,
                    FontSize = 14,
                    Background = PanelBrush,
                    Foreground = Brushes.White,
                    BorderThickness = new Thickness(0),
                    CornerRadius = new CornerRadius(8),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    Padding = new Thickness(14, 8),
                    Tag = action,
                };
                btn.Click += OnActionClicked;
                panel.Children.Add(btn);
            }
        }

        Content = panel;
    }

    private async void OnActionClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not QuickAction action) return;

        btn.IsEnabled = false;
        var originalContent = btn.Content;
        btn.Content = "⏳ ...";

        try
        {
            await _api.ToggleEntityAsync(action.EntityId);
            btn.Content = $"✓ {action.Name}";
            btn.Background = SuccessBrush;
        }
        catch
        {
            btn.Content = $"✗ {action.Name}";
            btn.Background = DangerBrush;
        }

        // Auto-close after 1.5s
        await Task.Delay(1500);
        Close();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    /// <summary>
    /// Show the Quick Actions popup. Closes any existing instance first.
    /// </summary>
    public static void ShowActions(List<QuickAction> actions, HaApiClient api)
    {
        if (_instance != null)
        {
            _instance.Close();
        }
        _instance = new QuickActionWindow(actions, api);
        _instance.Show();
        _instance.Activate();
    }

    /// <summary>
    /// Load Quick Actions from the config JSON (same format as Windows).
    /// </summary>
    public static List<QuickAction> LoadFromConfig(Config config)
    {
        var result = new List<QuickAction>();
        try
        {
            var arr = System.Text.Json.JsonDocument.Parse(config.QuickActions).RootElement;
            foreach (var item in arr.EnumerateArray())
            {
                var entityId = item.TryGetProperty("entityId", out var eid) ? eid.GetString() ?? "" : "";
                var name = item.TryGetProperty("name", out var n) ? n.GetString() ?? entityId : entityId;
                if (!string.IsNullOrEmpty(entityId))
                    result.Add(new QuickAction(entityId, name));
            }
        }
        catch { }
        return result;
    }
}