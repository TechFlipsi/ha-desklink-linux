// HA DeskLink - Home Assistant Companion App
// Copyright (C) 2026 Fabian Kirchweger
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HaDeskLink.Views;

/// <summary>
/// Modern floating notification popup for HA push notifications.
/// Auto-dismisses after 8s, supports action buttons.
/// </summary>
public class NotificationPopup : Window
{
    private DispatcherTimer? _autoCloseTimer;
    private bool _isClosing;

    private static readonly IBrush PanelBrush = new SolidColorBrush(Color.FromArgb(255, 22, 33, 62));
    private static readonly IBrush AccentBrush = new SolidColorBrush(Color.FromArgb(255, 15, 52, 96));
    private static readonly IBrush HaBlueBrush = new SolidColorBrush(Color.FromArgb(255, 66, 133, 244));
    private static readonly IBrush GrayBrush = new SolidColorBrush(Color.FromArgb(255, 140, 140, 160));

    public NotificationPopup(string title, string message, List<NotificationActionInfo>? actions = null)
    {
        CanResize = false;
        ShowInTaskbar = false;
        Topmost = true;
        Width = 380;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Background = new SolidColorBrush(Color.FromArgb(255, 26, 26, 46));

        actions ??= new List<NotificationActionInfo>();

        var accentBar = new Border { Background = HaBlueBrush, CornerRadius = new CornerRadius(12, 0, 0, 12) };
        Grid.SetColumn(accentBar, 0);

        var contentStack = new StackPanel { Margin = new Thickness(16, 14, 14, 14), Spacing = 8 };
        Grid.SetColumn(contentStack, 1);
        BuildContentChildren(contentStack, title, message, actions);

        var card = new Border
        {
            Background = PanelBrush,
            CornerRadius = new CornerRadius(12),
            ClipToBounds = true,
            Margin = new Thickness(8),
            Child = new Grid
            {
                ColumnDefinitions = ColumnDefinitions.Parse("4,*"),
                Children = { accentBar, contentStack }
            }
        };

        Content = card;

        _autoCloseTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
        _autoCloseTimer.Tick += (s, e) => { _autoCloseTimer!.Stop(); CloseAnimated(); };
        _autoCloseTimer.Start();

        PointerEntered += (s, e) => _autoCloseTimer?.Stop();
        PointerExited += (s, e) => { if (!_isClosing) _autoCloseTimer?.Start(); };
    }

    private void BuildContentChildren(StackPanel stack, string title, string message, List<NotificationActionInfo> actions)
    {
        var titleText = new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeight.Bold, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        Grid.SetColumn(titleText, 0);
        var closeBtn = new Button { Content = "✕", FontSize = 14, Background = Brushes.Transparent, Foreground = GrayBrush, Padding = new Thickness(4, 2), CornerRadius = new CornerRadius(4), VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Right };
        Grid.SetColumn(closeBtn, 1);
        closeBtn.Click += (s, e) => CloseAnimated();

        var headerGrid = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("*,Auto") };
        headerGrid.Children.Add(titleText);
        headerGrid.Children.Add(closeBtn);
        stack.Children.Add(headerGrid);

        stack.Children.Add(new TextBlock { Text = message, FontSize = 13, Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 200, 215)), TextWrapping = TextWrapping.Wrap, MaxLines = 5 });
        stack.Children.Add(new TextBlock { Text = DateTime.Now.ToString("HH:mm"), FontSize = 11, Foreground = GrayBrush, HorizontalAlignment = HorizontalAlignment.Right });

        if (actions.Count > 0)
        {
            var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 4, 0, 0) };
            foreach (var action in actions)
            {
                var btn = new Button { Content = action.Title, FontSize = 12, Background = AccentBrush, Foreground = Brushes.White, CornerRadius = new CornerRadius(6), Padding = new Thickness(14, 6), Tag = action };
                btn.Click += (s, e) => { action.OnAction?.Invoke(); CloseAnimated(); };
                btnPanel.Children.Add(btn);
            }
            stack.Children.Add(btnPanel);
        }
    }

    public void PositionTopRight(double offsetX = 20, double offsetY = 20)
    {
        var screen = Screens.ScreenFromWindow(this) ?? Screens.All.FirstOrDefault();
        if (screen != null)
        {
            var wa = screen.WorkingArea;
            Position = new PixelPoint(wa.X + wa.Width - (int)Width - (int)offsetX - 16, wa.Y + (int)offsetY);
        }
    }

    private void CloseAnimated() { if (_isClosing) return; _isClosing = true; _autoCloseTimer?.Stop(); Close(); }

    public static NotificationPopup ShowNotification(string title, string message, List<NotificationActionInfo>? actions = null)
    {
        var popup = new NotificationPopup(title, message, actions);
        popup.Show();
        popup.PositionTopRight();
        return popup;
    }
}

public class NotificationActionInfo
{
    public string ActionKey { get; }
    public string Title { get; }
    public Action? OnAction { get; set; }
    public NotificationActionInfo(string actionKey, string title, Action? onAction = null) { ActionKey = actionKey; Title = title; OnAction = onAction; }
}