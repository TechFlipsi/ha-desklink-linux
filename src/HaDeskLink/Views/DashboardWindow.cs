// HA DeskLink - Home Assistant Companion App
// Copyright (C) 2026 Fabian Kirchweger
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using AvaloniaWebView;
using WebViewCore.Events;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace HaDeskLink.Views;

/// <summary>
/// Embedded HA Dashboard using WebView.Avalonia.Linux.Cross with external_auth API.
/// Auto-logs in using the Long-Lived Access Token from config.
/// Includes rate-limiting and IP-ban prevention via AuthGuard.
/// </summary>
public class DashboardWindow : Window
{
    private readonly string _haUrl;
    private readonly string _token;
    private readonly AuthGuard _authGuard;
    private WebView? _webView;
    private TextBlock? _errorLabel;
    private StackPanel? _loadingPanel;
    private Border? _mainPanel;

    public DashboardWindow(string haUrl, string token)
    {
        _haUrl = haUrl.TrimEnd('/');
        _token = token;
        _authGuard = new AuthGuard();

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

        var retryBtn = new Button
        {
            Name = "BtnRetry", Content = "\U0001F504 Erneut versuchen", IsVisible = false,
            Background = new SolidColorBrush(Color.FromArgb(255, 15, 52, 96)), Foreground = Brushes.White,
            CornerRadius = new CornerRadius(6), Padding = new Thickness(16, 8), HorizontalAlignment = HorizontalAlignment.Center
        };
        retryBtn.Click += OnRetry;

        var browserBtn = new Button
        {
            Name = "BtnOpenBrowser", Content = "\U0001F517 Im Browser \u00f6ffnen",
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
                    new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Spacing = 10, Children = { retryBtn, browserBtn } }
                }
            }
        };

        Content = _mainPanel;
    }

    private async void OnLoaded(object? sender, RoutedEventArgs e) => await InitializeWebView();

    private async Task InitializeWebView()
    {
        if (_authGuard.IsBlocked) { ShowError(_authGuard.BlockMessage); return; }

        try
        {
            _webView = new WebView
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            _webView.Url = _haUrl + "?external_auth=1";
            _webView.NavigationCompleted += OnNavigationCompleted;

            _mainPanel!.Child = _webView;
        }
        catch (Exception ex)
        {
            _authGuard.RecordFailure(ex.Message);
            ShowError(_authGuard.IsBlocked ? _authGuard.BlockMessage : "Fehler beim Laden: " + ex.Message);
        }
    }

    private async void OnNavigationCompleted(object? sender, WebViewUrlLoadedEventArg e)
    {
        if (_webView == null || _authGuard.IsBlocked) return;
        try
        {
            await Task.Delay(500);
            _webView.EvaluateJavaScript(BuildExternalAuthScript());
        }
        catch (Exception ex)
        {
            _authGuard.RecordFailure("Auth inject failed: " + ex.Message);
        }
    }

    private string BuildExternalAuthScript()
    {
        var t = _token.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\"", "\\\"").Replace("\n", "").Replace("\r", "");
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("(function() {");
        sb.AppendLine("  if (window._externalAuthInjected) return;");
        sb.AppendLine("  window._externalAuthInjected = true;");
        sb.AppendLine("  window.externalApp = {");
        sb.AppendLine("    getExternalAuth: function(cb, force) {");
        sb.AppendLine("      try { cb({ access_token: '" + t + "', expires_in: 900, refresh_token: '" + t + "', token_type: 'Bearer' }); }");
        sb.AppendLine("      catch(e) { console.error('[HA DeskLink] getExternalAuth error:', e); }");
        sb.AppendLine("    },");
        sb.AppendLine("    saveExternalAuth: function(data, cb) { try { if (cb) cb(); } catch(e) {} },");
        sb.AppendLine("    revokeExternalAuth: function(cb) { try { if (cb) cb(); } catch(e) {} if (window.close) window.close(); }");
        sb.AppendLine("  };");
        sb.AppendLine("  console.log('[HA DeskLink] externalAuth interface injected');");
        sb.AppendLine("})();");
        return sb.ToString();
    }

    private void ShowError(string message)
    {
        if (_loadingPanel != null) _loadingPanel.IsVisible = false;
        if (_errorLabel != null) { _errorLabel.Text = message; _errorLabel.IsVisible = true; }
        var btn = this.FindControl<Button>("BtnRetry");
        if (btn != null && !_authGuard.IsHardBlocked) btn.IsVisible = true;
    }

    private void OnRetry(object? sender, RoutedEventArgs e)
    {
        _authGuard.Reset();
        if (_errorLabel != null) _errorLabel.IsVisible = false;
        if (_loadingPanel != null) _loadingPanel.IsVisible = true;
        var btn = this.FindControl<Button>("BtnRetry");
        if (btn != null) btn.IsVisible = false;
        _ = InitializeWebView();
    }

    private void OnOpenBrowser(object? sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(_haUrl) { UseShellExecute = true }); } catch { }
        Close();
    }
}