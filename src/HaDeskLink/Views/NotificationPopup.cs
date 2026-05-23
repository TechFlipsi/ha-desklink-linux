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
/// Modern floating notification popup — v4.1.0 toast style matching Windows.
/// Supports accent color (blue default, green for connection), timestamp, hover pause, and improved button styling.
/// </summary>
public class NotificationPopup : Window
{
    private DispatcherTimer? _autoCloseTimer;
    private bool _isClosing;

    // ── Modern dark navy palette (matching Windows v4.1.0) ──
    public static readonly IBrush BgBrush = SolidColorBrush.Parse("#16213E");
    public static readonly IBrush AccentBlueBrush = SolidColorBrush.Parse("#4285F4");
    public static readonly IBrush AccentGreenBrush = SolidColorBrush.Parse("#4CAF50");
    public static readonly IBrush GrayBrush = SolidColorBrush.Parse("#8C8CA0");
    public static readonly IBrush ContentBrush = SolidColorBrush.Parse("#C8C8D7");
    public static readonly IBrush ButtonHoverBrush = SolidColorBrush.Parse("#5294FF");
    public static readonly IBrush ButtonGreenHoverBrush = SolidColorBrush.Parse("#66BB6A");

    private readonly IBrush _accentBrush;

    public NotificationPopup(string title, string message, List<NotificationActionInfo>? actions = null, IBrush? accentBrush = null)
    {
        _accentBrush = accentBrush ?? AccentBlueBrush;
        var hoverBrush = _accentBrush == AccentGreenBrush ? ButtonGreenHoverBrush : ButtonHoverBrush;

        CanResize = false;
        ShowInTaskbar = false;
        Topmost = true;
        Width = 380;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Background = Brushes.Transparent;

        actions ??= new List<NotificationActionInfo>();

        var accentBar = new Border
        {
            Background = _accentBrush,
            CornerRadius = new CornerRadius(12, 0, 0, 12)
        };
        Grid.SetColumn(accentBar, 0);

        var contentStack = new StackPanel { Margin = new Thickness(16, 14, 14, 14), Spacing = 8 };
        Grid.SetColumn(contentStack, 1);
        BuildContentChildren(contentStack, title, message, actions, hoverBrush);

        var card = new Border
        {
            Background = BgBrush,
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

        // ── Auto-close timer (pauses on hover, restarts on leave) ──
        _autoCloseTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
        _autoCloseTimer.Tick += (s, e) =>
        {
            _autoCloseTimer!.Stop();
            CloseAnimated();
        };
        _autoCloseTimer.Start();

        PointerEntered += (s, e) => _autoCloseTimer?.Stop();
        PointerExited += (s, e) =>
        {
            if (!_isClosing)
                _autoCloseTimer?.Start();
        };
    }

    private void BuildContentChildren(StackPanel stack, string title, string message, List<NotificationActionInfo> actions, IBrush hoverBrush)
    {
        // ── Header row (title + close button) ──
        var titleText = new TextBlock
        {
            Text = title,
            FontSize = 15,
            FontWeight = FontWeight.Bold,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetColumn(titleText, 0);

        var closeBtn = new Button
        {
            Content = "✕",
            FontSize = 14,
            Background = Brushes.Transparent,
            Foreground = GrayBrush,
            Padding = new Thickness(4, 2),
            CornerRadius = new CornerRadius(4),
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        Grid.SetColumn(closeBtn, 1);
        closeBtn.Click += (s, e) => CloseAnimated();

        var headerGrid = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("*,Auto") };
        headerGrid.Children.Add(titleText);
        headerGrid.Children.Add(closeBtn);
        stack.Children.Add(headerGrid);

        // ── Message body ──
        stack.Children.Add(new TextBlock
        {
            Text = message,
            FontSize = 13,
            Foreground = ContentBrush,
            TextWrapping = TextWrapping.Wrap,
            MaxLines = 5
        });

        // ── Timestamp ──
        stack.Children.Add(new TextBlock
        {
            Text = DateTime.Now.ToString("HH:mm"),
            FontSize = 11,
            Foreground = GrayBrush,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 2, 0, 0)
        });

        // ── Action buttons with hover styling ──
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
                    Background = _accentBrush,
                    Foreground = Brushes.White,
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(14, 6),
                    Tag = action
                };

                // Hover effect
                btn.PointerEntered += (s, e) => btn.Background = hoverBrush;
                btn.PointerExited += (s, e) => btn.Background = _accentBrush;

                btn.Click += (s, e) =>
                {
                    action.OnAction?.Invoke();
                    CloseAnimated();
                };

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

    private void CloseAnimated()
    {
        if (_isClosing) return;
        _isClosing = true;
        _autoCloseTimer?.Stop();
        _autoCloseTimer = null;
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        _autoCloseTimer?.Stop();
        _autoCloseTimer = null;
        base.OnClosed(e);
    }

    /// <summary>
    /// Show a standard notification with blue accent.
    /// </summary>
    public static NotificationPopup ShowNotification(string title, string message, List<NotificationActionInfo>? actions = null)
    {
        var popup = new NotificationPopup(title, message, actions);
        popup.Show();
        popup.PositionTopRight();
        return popup;
    }

    /// <summary>
    /// Show a connection success toast with green accent.
    /// </summary>
    public static NotificationPopup ShowConnectionToast(string title, string message)
    {
        var popup = new NotificationPopup(title, message, null, AccentGreenBrush);
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
    public NotificationActionInfo(string actionKey, string title, Action? onAction = null)
    {
        ActionKey = actionKey;
        Title = title;
        OnAction = onAction;
    }
}
