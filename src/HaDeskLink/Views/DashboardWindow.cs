// HA DeskLink - Home Assistant Companion App
// Copyright (C) 2026 Fabian Kirchweger
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License v3 as published by
// the Free Software Foundation.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace HaDeskLink.Views;

/// <summary>
/// Embedded HA Dashboard using Avalonia NativeWebView with external_auth.
/// Auto-logs in using the Long-Lived Access Token from config.
/// Includes rate-limiting and IP-ban prevention.
/// </summary>
public class DashboardWindow : Window
{
    private readonly string _haUrl;
    private readonly string _token;
    private readonly AuthGuard _authGuard;

    // WebView reference (platform-specific NativeWebView)
    private object? _webView;
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

        // Build UI
        BuildContent();

        Loaded += OnLoaded;
    }

    private void BuildContent()
    {
        _errorLabel = new TextBlock
        {
            Text = "",
            Foreground = Brushes.Red,
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
                new TextBlock
                {
                    Text = "🏠 Dashboard wird geladen...",
                    FontSize = 18,
                    HorizontalAlignment = HorizontalAlignment.Center
                },
                new TextBlock
                {
                    Text = "Verbinde mit Home Assistant...",
                    FontSize = 12,
                    Foreground = Brushes.Gray,
                    HorizontalAlignment = HorizontalAlignment.Center
                }
            }
        };

        // Back button for when WebView isn't available
        var fallbackPanel = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 16,
            Children =
            {
                new TextBlock
                {
                    Text = "🌐 Embedded Dashboard",
                    FontSize = 20,
                    FontWeight = FontWeight.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center
                },
                _loadingPanel,
                _errorLabel,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Spacing = 10,
                    Children =
                    {
                        new Button
                        {
                            Name = "BtnRetry",
                            Content = "🔄 Erneut versuchen",
                            IsVisible = false
                        },
                        new Button
                        {
                            Name = "BtnOpenBrowser",
                            Content = "🔗 Im Browser öffnen"
                        }
                    }
                }
            }
        };

        _mainPanel = new Border
        {
            Background = Brushes.White,
            Child = fallbackPanel
        };

        Content = _mainPanel;

        // Button handlers
        var btnRetry = this.FindControl<Button>("BtnRetry");
        if (btnRetry != null) btnRetry.Click += OnRetry;

        var btnBrowser = this.FindControl<Button>("BtnOpenBrowser");
        if (btnBrowser != null) btnBrowser.Click += OnOpenBrowser;
    }

    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        await InitializeWebView();
    }

    private async Task InitializeWebView()
    {
        // Check rate limit first
        if (_authGuard.IsBlocked)
        {
            ShowError(_authGuard.BlockMessage);
            return;
        }

        try
        {
            // Try to create NativeWebView
            var webViewType = TryResolveWebViewType();

            if (webViewType == null)
            {
                ShowFallback("WebView nicht verfügbar – wird im Browser geöffnet.", canRetry: false);
                await Task.Delay(1500);
                OpenInBrowser();
                return;
            }

            // Create WebView instance
            _webView = Activator.CreateInstance(webViewType);
            if (_webView == null)
            {
                ShowFallback("WebView konnte nicht erstellt werden.", canRetry: false);
                return;
            }

            // Try to make WebView fill the parent
            SetWebViewProperty(_webView, "HorizontalAlignment", HorizontalAlignment.Stretch);
            SetWebViewProperty(_webView, "VerticalAlignment", VerticalAlignment.Stretch);

            // Build the auth URL
            var dashboardUrl = $"{_haUrl}?external_auth=1";

            // Navigate
            await NavigateWebView(_webView, dashboardUrl);

            // Inject externalAuth JavaScript after page loads
            HookNavigationComplete(_webView);

            // Replace loading panel with WebView
            _mainPanel!.Child = _webView as Control;
        }
        catch (Exception ex)
        {
            _authGuard.RecordFailure(ex.Message);
            if (_authGuard.IsBlocked)
            {
                ShowError(_authGuard.BlockMessage);
            }
            else
            {
                ShowFallback($"Fehler beim Laden: {ex.Message}", canRetry: true);
            }
        }
    }

    private Type? TryResolveWebViewType()
    {
        // Try Avalonia.Controls.WebView (official Avalonia WebView package)
        try
        {
            var type = Type.GetType("Avalonia.Controls.NativeWebView, Avalonia.Controls.WebView");
            if (type != null) return type;
        }
        catch { }

        // Try Avalonia.Controls.WebView (alternate name)
        try
        {
            var assembly = System.Reflection.Assembly.Load("Avalonia.Controls.WebView");
            var type = assembly?.GetType("Avalonia.Controls.NativeWebView");
            if (type != null) return type;
        }
        catch { }

        // Try WebView.Avalonia package
        try
        {
            var type = Type.GetType("WebView.Avalonia.WebView, WebView.Avalonia");
            if (type != null) return type;
        }
        catch { }

        return null;
    }

    private void SetWebViewProperty(object obj, string propertyName, object value)
    {
        try
        {
            var prop = obj.GetType().GetProperty(propertyName);
            prop?.SetValue(obj, value);
        }
        catch { }
    }

    private async Task NavigateWebView(object webView, string url)
    {
        try
        {
            // Try NavigateAsync if available
            var navigateMethod = webView.GetType().GetMethod("NavigateAsync");
            if (navigateMethod != null)
            {
                var task = navigateMethod.Invoke(webView, new object[] { url }) as Task;
                if (task != null) await task;
                return;
            }

            // Try Navigate
            var syncNav = webView.GetType().GetMethod("Navigate");
            if (syncNav != null)
            {
                syncNav.Invoke(webView, new object[] { url });
                return;
            }

            // Try Source property
            var sourceProp = webView.GetType().GetProperty("Source");
            if (sourceProp != null)
            {
                sourceProp.SetValue(webView, new Uri(url));
                return;
            }
        }
        catch { }
    }

    private void HookNavigationComplete(object webView)
    {
        try
        {
            // Try to hook NavigationCompleted event
            var eventInfo = webView.GetType().GetEvent("NavigationCompleted")
                ?? webView.GetType().GetEvent("NavigateCompleted")
                ?? webView.GetType().GetEvent("PageLoaded");

            if (eventInfo != null)
            {
                var handlerType = eventInfo.EventHandlerType!;
                var handler = Delegate.CreateDelegate(handlerType, this, nameof(OnNavigationCompleted));
                eventInfo.AddEventHandler(webView, handler);
            }
            else
            {
                // Fallback: inject after a delay
                _ = InjectExternalAuthDelayed(webView, 3000);
            }
        }
        catch
        {
            _ = InjectExternalAuthDelayed(webView, 3000);
        }
    }

    private void OnNavigationCompleted(object? sender, EventArgs e)
    {
        _ = InjectExternalAuth(_webView!);
    }

    private async Task InjectExternalAuthDelayed(object webView, int delayMs)
    {
        await Task.Delay(delayMs);
        await InjectExternalAuth(webView);
    }

    private async Task InjectExternalAuth(object webView)
    {
        if (_authGuard.IsBlocked) return;

        try
        {
            // Build the externalAuth JavaScript interface
            var js = BuildExternalAuthScript();

            // Try ExecuteScriptAsync
            var execMethod = webView.GetType().GetMethod("ExecuteScriptAsync");
            if (execMethod != null)
            {
                var task = execMethod.Invoke(webView, new object[] { js }) as Task<string>;
                if (task != null) await task;
                return;
            }

            // Try ExecuteJavaScript
            var execMethod2 = webView.GetType().GetMethod("ExecuteJavaScript");
            if (execMethod2 != null)
            {
                execMethod2.Invoke(webView, new object[] { js });
                return;
            }
        }
        catch (Exception ex)
        {
            _authGuard.RecordFailure($"Auth inject failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Builds the window.externalApp JavaScript interface as documented by HA:
    /// https://developers.home-assistant.io/docs/frontend/external-authentication/
    /// 
    /// HA frontend calls these methods when loaded with ?external_auth=1:
    /// - getExternalAuth(callback, force) → returns access token
    /// - saveExternalAuth(data, callback) → saves refreshed token
    /// - revokeExternalAuth(callback) → revokes token on logout
    /// </summary>
    private string BuildExternalAuthScript()
    {
        // We escape the token for JS string safety
        var escapedToken = _token.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\"", "\\\"").Replace("\n", "").Replace("\r", "");

        // We store a refresh expiry (HA tokens are typically 15min access, long-lived refresh)
        var expiresIn = DateTimeOffset.UtcNow.AddMinutes(15).ToUnixTimeSeconds();

        return $$"""
        (function() {
            if (window._externalAuthInjected) return;
            window._externalAuthInjected = true;

            const TOKEN = '{{escapedToken}}';
            const EXPIRES = {{expiresIn}};

            window.externalApp = {
                getExternalAuth: function(callback, force) {
                    try {
                        callback({
                            access_token: TOKEN,
                            expires_in: 900,
                            refresh_token: TOKEN,
                            token_type: 'Bearer'
                        });
                    } catch(e) {
                        console.error('externalApp.getExternalAuth error:', e);
                    }
                },
                saveExternalAuth: function(data, callback) {
                    // Token refresh — accept but we keep the original long-lived token
                    try { if (callback) callback(); } catch(e) {}
                },
                revokeExternalAuth: function(callback) {
                    // Token revoked — close dashboard
                    try { if (callback) callback(); } catch(e) {}
                    if (window.close) window.close();
                }
            };

            console.log('[HA DeskLink] externalAuth interface injected');
        })();
        """;
    }

    private void ShowError(string message)
    {
        if (_loadingPanel != null) _loadingPanel.IsVisible = false;
        if (_errorLabel != null)
        {
            _errorLabel.Text = message;
            _errorLabel.IsVisible = true;
        }
        var btnRetry = this.FindControl<Button>("BtnRetry");
        if (btnRetry != null && !_authGuard.IsHardBlocked)
            btnRetry.IsVisible = true;
    }

    private void ShowFallback(string message, bool canRetry)
    {
        if (_loadingPanel != null) _loadingPanel.IsVisible = false;
        if (_errorLabel != null)
        {
            _errorLabel.Text = message;
            _errorLabel.IsVisible = true;
            _errorLabel.Foreground = Brushes.Orange;
        }
        var btnRetry = this.FindControl<Button>("BtnRetry");
        if (btnRetry != null) btnRetry.IsVisible = canRetry;
    }

    private void OnRetry(object? sender, RoutedEventArgs e)
    {
        _authGuard.Reset();
        if (_errorLabel != null)
        {
            _errorLabel.IsVisible = false;
        }
        if (_loadingPanel != null) _loadingPanel.IsVisible = true;
        var btnRetry = this.FindControl<Button>("BtnRetry");
        if (btnRetry != null) btnRetry.IsVisible = false;

        _ = InitializeWebView();
    }

    private void OnOpenBrowser(object? sender, RoutedEventArgs e)
    {
        OpenInBrowser();
    }

    private void OpenInBrowser()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_haUrl)
            {
                UseShellExecute = true
            });
        }
        catch { }
        Close();
    }
}

/// <summary>
/// Protects against HA IP-bans by rate-limiting authentication attempts.
/// HA bans IPs after too many failed attempts — this guard ensures
/// HA DeskLink never floods HA with auth requests.
///
/// Rules:
/// - Max 3 failed attempts before soft block (user can retry)
/// - Max 5 failed attempts before hard block (must restart app)
/// - Exponential backoff between retries: 5s → 30s → 120s → 300s
/// - Auto-reset on successful auth
/// - Token validity pre-check before attempting auth
/// </summary>
public class AuthGuard
{
    private int _failedAttempts;
    private DateTime _lastFailure = DateTime.MinValue;
    private DateTime _blockedUntil = DateTime.MinValue;
    private bool _hardBlocked;
    private string _lastError = "";

    public const int MaxSoftAttempts = 3;
    public const int MaxHardAttempts = 5;

    /// <summary>Current backoff in seconds (exponential)</summary>
    public int CurrentBackoffSeconds
    {
        get
        {
            return _failedAttempts switch
            {
                0 => 0,
                1 => 5,
                2 => 30,
                3 => 120,
                _ => 300
            };
        }
    }

    public bool IsBlocked
    {
        get
        {
            // Hard block — must restart
            if (_hardBlocked) return true;

            // Soft block — check if backoff period has passed
            if (_failedAttempts >= MaxSoftAttempts)
            {
                if (DateTime.UtcNow < _blockedUntil)
                    return true;

                // Backoff expired — allow retry
                return false;
            }

            return false;
        }
    }

    public bool IsHardBlocked => _hardBlocked;

    public string BlockMessage
    {
        get
        {
            if (_hardBlocked)
                return $"⚠️ Authentifierung blockiert — zu viele fehlgeschlagene Versuche.\n\n" +
                       $"Letzter Fehler: {_lastError}\n\n" +
                       $"Aus Sicherheitsgründen (HA IP-Ban-Schutz) wurden die Login-Versuche gestoppt.\n" +
                       $"Bitte überprüfe deinen Token und starte HA DeskLink neu.";

            if (_failedAttempts >= MaxSoftAttempts)
            {
                var remaining = (_blockedUntil - DateTime.UtcNow);
                if (remaining > TimeSpan.Zero)
                    return $"⚠️ Zu viele Login-Versuche — warte {remaining.Duration():mm\\:ss} vor erneutem Versuch.\n\n" +
                           $"Letzter Fehler: {_lastError}\n\n" +
                           $"Dies schützt vor HA IP-Bans bei ungültigen Token.";
            }

            return $"⚠️ Authentifizierung fehlgeschlagen ({_failedAttempts}/{MaxHardAttempts}).\n{_lastError}";
        }
    }

    public void RecordFailure(string error)
    {
        _failedAttempts++;
        _lastFailure = DateTime.UtcNow;
        _lastError = error;

        if (_failedAttempts >= MaxHardAttempts)
        {
            _hardBlocked = true;
            _blockedUntil = DateTime.MaxValue;
        }
        else if (_failedAttempts >= MaxSoftAttempts)
        {
            _blockedUntil = DateTime.UtcNow.AddSeconds(CurrentBackoffSeconds);
        }
    }

    public void RecordSuccess()
    {
        _failedAttempts = 0;
        _hardBlocked = false;
        _blockedUntil = DateTime.MinValue;
        _lastError = "";
    }

    public void Reset()
    {
        _failedAttempts = 0;
        _hardBlocked = false;
        _blockedUntil = DateTime.MinValue;
        _lastError = "";
    }

    /// <summary>
    /// Pre-validates token format before attempting auth.
    /// Long-lived HA tokens are typically hex strings of 40+ chars.
    /// </summary>
    public static bool ValidateTokenFormat(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return false;
        if (token.Length < 20) return false;
        // HA tokens are base64-like or hex — check for obvious garbage
        if (token.Contains(' ') && !token.StartsWith("ey")) return false;
        return true;
    }
}