// HA DeskLink - Home Assistant Companion App
// Copyright (C) 2026 Fabian Kirchweger
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License v3 as published by
// the Free Software Foundation.
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace HaDeskLink.Views;

/// <summary>
/// Modern floating notification popup for HA push notifications.
/// Slides in from top-right, auto-dismisses after 8s, supports action buttons.
/// </summary>
public class NotificationPopup : Window
{
    private readonly DispatcherTimer? _autoCloseTimer;
    private bool _isClosing;

    // ── Colors ──────────────────────────────────────────────────
    private static readonly IBrush BgBrush = new SolidColorBrush(Color.FromArgb(255, 26, 26, 46));
    private static readonly IBrush PanelBrush = new SolidColorBrush(Color.FromArgb(255, 22, 33, 62));
    private static readonly IBrush AccentBrush = new SolidColorBrush(Color.FromArgb(255, 15, 52, 96));
    private static readonly IBrush HighlightBrush = new SolidColorBrush(Color.FromArgb(255, 233, 69, 96));
    private static readonly IBrush HaBlueBrush = new SolidColorBrush(Color.FromArgb(255, 66, 133, 244));
    private static readonly IBrush GrayBrush = new SolidColorBrush(Color.FromArgb(255, 140, 140, 160));

    public NotificationPopup(string title, string message, List<NotificationActionInfo>? actions = null)
    {
        SystemBackground = Brushes.Transparent;
        TransparentClientArea = true; // no native chrome
        ExtendClientAreaToDecorationsHint = true;
        CanResize = false;
        ShowInTaskbar = false;
        Topmost = true;
        Width = 380;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.Manual;

        actions ??= new List<NotificationActionInfo>();

        // ── Build content ───────────────────────────────────────
        var card = new Border
        {
            Background = PanelBrush,
            CornerRadius = new CornerRadius(12),
            ClipToBounds = true,
            Margin = new Thickness(8),
            BoxShadow = new BoxShadows(new BoxShadow { Color = Color.FromArgb(80, 0, 0, 0), Blur = 16, OffsetX = 0, OffsetY = 4 }),
            Child = new Grid
            {
                ColumnDefinitions = ColumnDefinitions.Parse("4,*"),
                Children =
                {
                    // Blue accent bar on left
                    new Border
                    {
                        Background = HaBlueBrush,
                        CornerRadius = new CornerRadius(12, 0, 0, 12),
                    }.WithGridColumn(0),

                    // Main content
                    new StackPanel
                    {
                        Margin = new Thickness(16, 14, 14, 14),
                        Spacing = 8,
                        Children = BuildContentChildren(title, message, actions)
                    }.WithGridColumn(1)
                }
            }
        };

        Content = card;

        // ── Auto-close timer (8 seconds) ────────────────────────
        _autoCloseTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
        _autoCloseTimer.Tick += (s, e) =>
        {
            _autoCloseTimer.Stop();
            CloseAnimated();
        };
        _autoCloseTimer.Start();

        // ── Pause auto-close on hover ───────────────────────────
        PointerEnter += (s, e) => _autoCloseTimer?.Stop();
        PointerLeave += (s, e) =>
        {
            if (!_isClosing)
            {
                _autoCloseTimer?.Start();
            }
        };
    }

    private List<Control> BuildContentChildren(string title, string message, List<NotificationActionInfo> actions)
    {
        var children = new List<Control>();

        // ── Header row (title + close button) ────────────────────
        var headerGrid = new Grid
        {
            ColumnDefinitions = ColumnDefinitions.Parse("*,Auto"),
            Children =
            {
                new TextBlock
                {
                    Text = title,
                    FontSize = 15,
                    FontWeight = FontWeight.Bold,
                    Foreground = Brushes.White,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextWrapping = TextWrapping.Wrap
                }.WithGridColumn(0),
                new Button
                {
                    Content = "✕",
                    FontSize = 14,
                    Background = Brushes.Transparent,
                    Foreground = GrayBrush,
                    Padding = new Thickness(4, 2),
                    CornerRadius = new CornerRadius(4),
                    VerticalAlignment = VerticalAlignment.Top,
                    HorizontalAlignment = HorizontalAlignment.Right
                }.WithGridColumn(1)
            }
        };
        children.Add(headerGrid);

        // Wire close button
        var closeBtn = headerGrid.Children.OfType<Button>().FirstOrDefault();
        if (closeBtn != null)
            closeBtn.Click += (s, e) => CloseAnimated();

        // ── Message ─────────────────────────────────────────────
        children.Add(new TextBlock
        {
            Text = message,
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 200, 215)),
            TextWrapping = TextWrapping.Wrap,
            MaxLines = 5
        });

        // ── Timestamp ──────────────────────────────────────────
        children.Add(new TextBlock
        {
            Text = DateTime.Now.ToString("HH:mm"),
            FontSize = 11,
            Foreground = GrayBrush,
            HorizontalAlignment = HorizontalAlignment.Right
        });

        // ── Action buttons ─────────────────────────────────────
        if (actions.Count > 0)
        {
            var btnPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 4, 0, 0)
            };

            foreach (var action in actions)
            {
                var btn = new Button
                {
                    Content = action.Title,
                    FontSize = 12,
                    Background = AccentBrush,
                    Foreground = Brushes.White,
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(14, 6),
                    Tag = action
                };
                btn.Click += (s, e) =>
                {
                    action.OnAction?.Invoke();
                    CloseAnimated();
                };
                btnPanel.Children.Add(btn);
            }
            children.Add(btnPanel);
        }

        return children;
    }

    /// <summary>Position popup in top-right corner of the screen.</summary>
    public void PositionTopRight(double offsetX = 20, double offsetY = 20)
    {
        var screen = Screens.ScreenFromWindow(this) ?? Screens.All.FirstOrDefault();
        if (screen != null)
        {
            var wa = screen.WorkingArea;
            Position = new PixelPoint(
                wa.X + wa.Width - (int)Width - (int)offsetX - 16,
                wa.Y + (int)offsetY
            );
        }
    }

    private void CloseAnimated()
    {
        if (_isClosing) return;
        _isClosing = true;
        _autoCloseTimer?.Stop();
        Close();
    }

    /// <summary>Show notification popup at top-right of screen.</summary>
    public static NotificationPopup ShowNotification(string title, string message, List<NotificationActionInfo>? actions = null)
    {
        var popup = new NotificationPopup(title, message, actions);
        popup.Show();
        popup.PositionTopRight();
        return popup;
    }
}

/// <summary>Info about a notification action button.</summary>
public class NotificationActionInfo
{
    public string ActionKey { get; }
    public string Title { get; }
    public Action? OnAction { get; set; }

    public NotificationActionInfo(string actionKey, string title, Action? onAction = null)
    {
        ActionKey = actionKey;
        Title = title;
        OnAction = onAction;
    }
}