// HA DeskLink - Home Assistant Companion App
// Copyright (C) 2026 Fabian Kirchweger
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using AvaloniaWebView;
using System;
using System.Diagnostics;
using System.Linq;

namespace HaDeskLink.Views;

/// <summary>
/// Embedded HA Dashboard using WebView.Avalonia.Linux.Cross.
/// Opens the HA login page — user logs in once with username/password,
/// then WebView remembers the session (just like a regular browser).
/// </summary>
public class DashboardWindow : Window
{
    private readonly string _haUrl;
    private WebView? _webView;
    private TextBlock? _errorLabel;
    private StackPanel? _loadingPanel;
    private Border? _mainPanel;

    public DashboardWindow(string haUrl)
    {
        _haUrl = haUrl.TrimEnd('/');

        Title = "HA DeskLink - Dashboard";
        Width = 1200;
        Height = 800;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromArgb(255, 26, 26, 46));

        BuildContent();
        Loaded += OnLoaded;
    }

    private void BuildContent()
    {
        _errorLabel = new TextBlock
        {
            Text = "",
            Foreground = Brushes.OrangeRed,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13,
            IsVisible = false
        };

        _loadingPanel = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = "\U0001F3E0 Dashboard wird geladen\u2026", FontSize = 18, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center },
                new TextBlock { Text = "Verbinde mit Home Assistant\u2026", FontSize = 12, Foreground = Brushes.Gray, HorizontalAlignment = HorizontalAlignment.Center }
            }
        };

        var browserBtn = new Button
        {
            Content = "\U0001F517 Im Browser \u00f6ffnen",
            Background = Brushes.Transparent, Foreground = Brushes.Gray,
            CornerRadius = new CornerRadius(6), Padding = new Thickness(16, 8), HorizontalAlignment = HorizontalAlignment.Center
        };
        browserBtn.Click += OnOpenBrowser;

        _mainPanel = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 26, 26, 46)),
            Child = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Spacing = 16,
                Children =
                {
                    new TextBlock { Text = "\U0001F310 Embedded Dashboard", FontSize = 20, FontWeight = FontWeight.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center },
                    _loadingPanel,
                    _errorLabel,
                    browserBtn
                }
            }
        };

        Content = _mainPanel;
    }

    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        try
        {
            _webView = new WebView
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };

            // Navigate directly to HA — user logs in once, session persists
            _webView.Url = new Uri(_haUrl);

            _mainPanel!.Child = _webView;
        }
        catch (Exception ex)
        {
            ShowError("Fehler beim Laden: " + ex.Message);
        }
    }

    private void ShowError(string message)
    {
        if (_loadingPanel != null) _loadingPanel.IsVisible = false;
        if (_errorLabel != null) { _errorLabel.Text = message; _errorLabel.IsVisible = true; }
    }

    private void OnOpenBrowser(object? sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(_haUrl) { UseShellExecute = true }); } catch { }
        Close();
    }

    /// <summary>
    /// Opens the dashboard window. If already open, activates it.
    /// No token needed — user logs in once via normal HA login page.
    /// </summary>
    public static void Open(string haUrl)
    {
        // Find existing window or create new one
        var existing = Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.Windows.OfType<DashboardWindow>().FirstOrDefault()
            : null;

        if (existing != null)
        {
            existing.Activate();
            return;
        }

        var window = new DashboardWindow(haUrl);
        window.Show();
    }
}