
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
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace HaDeskLink.Views;

public partial class MainWindow : Window
{
    private string _haUrl = "";
    private TextBlock? _statusLabel;

    public string HaUrl
    {
        get => _haUrl;
        set { _haUrl = value; }
    }

    public MainWindow()
    {
        InitializeComponent();
        _statusLabel = this.FindControl<TextBlock>("LblStatus");

        var btnDashboard = this.FindControl<Button>("BtnDashboard");
        if (btnDashboard != null) btnDashboard.Click += OnOpenDashboard;

        var btnRefresh = this.FindControl<Button>("BtnRefresh");
        if (btnRefresh != null) btnRefresh.Click += OnRefresh;

        var btnSetup = this.FindControl<Button>("BtnSetup");
        if (btnSetup != null) btnSetup.Click += OnSetup;

        var btnReset = this.FindControl<Button>("BtnResetDevice");
        if (btnReset != null) btnReset.Click += OnResetDevice;

        var btnQuickActions = this.FindControl<Button>("BtnQuickActions");
        if (btnQuickActions != null) btnQuickActions.Click += OnQuickActions;

        var btnDiscord = this.FindControl<Button>("BtnDiscord");
        if (btnDiscord != null) btnDiscord.Click += (s, e) => OpenUrl("https://discord.gg/7G2SqpXpsC");

        var btnGitHub = this.FindControl<Button>("BtnGitHub");
        if (btnGitHub != null) btnGitHub.Click += (s, e) => OpenUrl("https://github.com/TechFlipsi/ha-desklink-linux");

        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        var config = Config.Load();
        _haUrl = config.HaUrl;
        if (_statusLabel != null)
            _statusLabel.Text = string.IsNullOrEmpty(_haUrl) ? "⚠️ Nicht verbunden" : $"✓ Verbunden: {_haUrl}";
    }

    private void OnOpenDashboard(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_haUrl)) return;

        var config = Config.Load();

        // Validate token format before attempting auth
        if (!AuthGuard.ValidateTokenFormat(config.HaToken))
        {
            // No token or invalid format → fallback to browser (standard login)
            try { Process.Start(new ProcessStartInfo(_haUrl) { UseShellExecute = true }); }
            catch { }
            return;
        }

        // Open embedded dashboard with auto-login
        var dashboard = new DashboardWindow(_haUrl, config.HaToken);
        dashboard.Show();
    }

    private static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { }
    }

    private void OnRefresh(object? sender, RoutedEventArgs e)
    {
        if (_statusLabel != null)
            _statusLabel.Text = "Sensoren aktualisiert ✓";
    }

    private async void OnSetup(object? sender, RoutedEventArgs e)
    {
        var config = Config.Load();

        // ── Dark theme brushes ─────────────────────────────────────
        var bgBrush       = Avalonia.Media.SolidColorBrush.Parse("#1A1A2E");
        var panelBrush    = Avalonia.Media.SolidColorBrush.Parse("#16213E");
        var accentBrush   = Avalonia.Media.SolidColorBrush.Parse("#0F3460");
        var highlightBrush= Avalonia.Media.SolidColorBrush.Parse("#E94560");
        var successBrush  = Avalonia.Media.SolidColorBrush.Parse("#4CAF50");
        var inputBgBrush  = Avalonia.Media.SolidColorBrush.Parse("#2D2D3F");
        var fgBrush       = Avalonia.Media.SolidColorBrush.Parse("#E0E0E0");
        var grayBrush     = Avalonia.Media.SolidColorBrush.Parse("#888888");
        var whiteBrush    = Avalonia.Media.Brushes.White;

        // ── Status bar text (reusable) ─────────────────────────────
        var statusBar = new TextBlock
        {
            Text = "Nicht verbunden",
            Foreground = grayBrush,
            FontSize = 12,
            Margin = new Thickness(0, 8, 0, 0),
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        };

        // ── Attempt counter ────────────────────────────────────────
        var attemptLabel = new TextBlock
        {
            Text = "",
            Foreground = grayBrush,
            FontSize = 11,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right
        };

        // ── Token show/hide toggle ─────────────────────────────────
        var tokenBox = new TextBox
        {
            Text = config.HaToken,
            Watermark = "Long-Lived Access Token",
            PasswordChar = '•',
            Background = inputBgBrush,
            Foreground = fgBrush,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 8),
            BorderThickness = new Thickness(1),
            BorderBrush = Avalonia.Media.SolidColorBrush.Parse("#3A3A5A")
        };
        var tokenToggleBtn = new Button
        {
            Content = "👁",
            Width = 36, Height = 36,
            Background = Avalonia.Media.Brushes.Transparent,
            Foreground = grayBrush,
            FontSize = 14,
            Padding = new Thickness(0),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };
        tokenToggleBtn.Click += (s, args) =>
        {
            if (tokenBox.PasswordChar != default(char))
            {
                tokenBox.PasswordChar = default;
                tokenToggleBtn.Content = "🙈";
                tokenToggleBtn.Foreground = highlightBrush;
            }
            else
            {
                tokenBox.PasswordChar = '•';
                tokenToggleBtn.Content = "👁";
                tokenToggleBtn.Foreground = grayBrush;
            }
        };

        // ═══════════════════════════════════════════════════════════
        //  BUILD DIALOG CONTENT
        // ═══════════════════════════════════════════════════════════
        var rootPanel = new Border
        {
            Background = bgBrush,
            CornerRadius = new CornerRadius(8),
            Child = new StackPanel
            {
                Margin = new Thickness(0),
                Spacing = 0,
                Children =
                {
                    // ── ACCENT BAR ─────────────────────────────────
                    new Border { Height = 4, Background = accentBrush },

                    new StackPanel
                    {
                        Margin = new Thickness(20, 16),
                        Spacing = 10,
                        Children =
                        {
                            // ── HEADER ────────────────────────────
                            new StackPanel { Spacing = 2, Children =
                            {
                                new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8, Children =
                                {
                                    new TextBlock { Text = "⚙️", FontSize = 20, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center },
                                    new TextBlock { Text = "HA DeskLink – Einrichtung", FontSize = 18, FontWeight = Avalonia.Media.FontWeight.Bold, Foreground = fgBrush, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center }
                                }},
                                new Border { Height = 2, Background = accentBrush, Margin = new Thickness(0, 4, 0, 0) }
                            }},

                            // ── SECTION: Verbindung ────────────────
                            new Border { Background = panelBrush, CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 12), Child =
                                new StackPanel { Spacing = 8, Children =
                                {
                                    new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 6, Children =
                                    {
                                        new TextBlock { Text = "🔌", FontSize = 14, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center },
                                        new TextBlock { Text = "Verbindung", FontSize = 14, FontWeight = Avalonia.Media.FontWeight.SemiBold, Foreground = fgBrush, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center }
                                    }},

                                    // URL field with https:// prefix
                                    new StackPanel { Spacing = 4, Children =
                                    {
                                        new TextBlock { Text = "Home Assistant URL", FontSize = 12, Foreground = grayBrush },
                                        new Border { Background = inputBgBrush, CornerRadius = new CornerRadius(8), Padding = new Thickness(0), BorderThickness = new Thickness(1), BorderBrush = Avalonia.Media.SolidColorBrush.Parse("#3A3A5A"), Child =
                                            new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Children =
                                            {
                                                new Border { Background = Avalonia.Media.SolidColorBrush.Parse("#252540"), CornerRadius = new CornerRadius(8,0,0,8), Padding = new Thickness(10, 8), Child =
                                                    new TextBlock { Text = "https://", Foreground = grayBrush, FontSize = 13, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center }
                                                , [Grid.ColumnProperty] = 0 },
                                                new TextBox { Text = config.HaUrl.Replace("https://", "").Replace("http://", ""), Watermark = "homeassistant.local:8123", Background = Avalonia.Media.Brushes.Transparent, Foreground = fgBrush, CornerRadius = new CornerRadius(0,8,8,0), Padding = new Thickness(10, 8), BorderThickness = new Thickness(0), [Grid.ColumnProperty] = 1, Name = "UrlBox" }
                                            }}
                                        }}
                                    }},

                                    // Token field with show/hide
                                    new StackPanel { Spacing = 4, Children =
                                    {
                                        new TextBlock { Text = "Long-Lived Access Token", FontSize = 12, Foreground = grayBrush },
                                        new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Children =
                                        {
                                            tokenBox,
                                            tokenToggleBtn
                                        }}
                                    }}

                                    // Set column for toggle button
                                    Avalonia.Controls.Grid.SetColumn(tokenToggleBtn, 1);
                                }}
                            }},

                            // ── SECTION: Sicherheit ───────────────
                            new Border { Background = panelBrush, CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 12), Child =
                                new StackPanel { Spacing = 6, Children =
                                {
                                    new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 6, Children =
                                    {
                                        new TextBlock { Text = "🔒", FontSize = 14, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center },
                                        new TextBlock { Text = "Sicherheit", FontSize = 14, FontWeight = Avalonia.Media.FontWeight.SemiBold, Foreground = fgBrush, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center }
                                    }},
                                    new CheckBox { Content = "SSL-Zertifikat prüfen", IsChecked = config.VerifySsl, Foreground = fgBrush, FontSize = 13, Name = "SslCheck" }
                                }}
                            }},

                            // ── SECTION: Token-Hilfe (collapsible) ─
                            new Border { Background = panelBrush, CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 10), Child =
                                new StackPanel { Spacing = 4, Children =
                                {
                                    new Button { Content = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 6, Children =
                                    {
                                        new TextBlock { Text = "ℹ️", FontSize = 13 },
                                        new TextBlock { Text = "Token-Hilfe  ▼", FontSize = 13, FontWeight = Avalonia.Media.FontWeight.SemiBold, Foreground = fgBrush }
                                    }}, Background = Avalonia.Media.Brushes.Transparent, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, Padding = new Thickness(0), Name = "HelpToggleBtn" },
                                    new StackPanel { Spacing = 4, IsVisible = false, Name = "HelpContentPanel", Children =
                                    {
                                        new TextBlock { Text = "So erhältst du einen Token:", FontSize = 12, Foreground = fgBrush, FontWeight = Avalonia.Media.FontWeight.SemiBold },
                                        new TextBlock { Text = "1.  Öffne Home Assistant in deinem Browser", FontSize = 11, Foreground = grayBrush },
                                        new TextBlock { Text = "2.  Klicke auf dein Profil (unten links)", FontSize = 11, Foreground = grayBrush },
                                        new TextBlock { Text = "3.  Scrolle zu „Sicherheit\" → „Long-Lived Access Tokens\"", FontSize = 11, Foreground = grayBrush },
                                        new TextBlock { Text = "4.  Klicke „Token erstellen\", gib einen Namen ein und kopiere den Token", FontSize = 11, Foreground = grayBrush },
                                        new TextBlock { Text = "⚠️ Der Token wird nur einmal angezeigt – gut aufbewahren!", FontSize = 11, Foreground = highlightBrush, Margin = new Thickness(0, 4, 0, 0) }
                                    }}
                                }}
                            }},

                            // ── ATTEMPT COUNTER ───────────────────
                            attemptLabel,

                            // ── STATUS BAR ────────────────────────
                            statusBar,

                            // ── BUTTON ROW ────────────────────────
                            new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 10, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, Margin = new Thickness(0, 6, 0, 0), Children =
                            {
                                new Button { Content = "Abbrechen", Background = Avalonia.Media.SolidColorBrush.Parse("#555570"), Foreground = whiteBrush, CornerRadius = new CornerRadius(8), Padding = new Thickness(20, 8), FontSize = 13, Name = "CancelBtn" },
                                new Button { Content = "Verbinden", Background = accentBrush, Foreground = whiteBrush, CornerRadius = new CornerRadius(8), Padding = new Thickness(20, 8), FontSize = 13, FontWeight = Avalonia.Media.FontWeight.SemiBold, Name = "ConnectBtn" }
                            }}
                        }
                    }
                }
            }
        };

        // ── Dialog window ──────────────────────────────────────────
        var dialog = new Window
        {
            Title = "HA DeskLink – Einrichtung",
            Width = 500,
            Height = 580,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Background = bgBrush,
            Content = rootPanel
        };

        // ── Control references ─────────────────────────────────────
        var urlBox = dialog.FindControl<TextBox>("UrlBox");
        var sslCheck = dialog.FindControl<CheckBox>("SslCheck");
        var connectBtn = dialog.FindControl<Button>("ConnectBtn");
        var cancelBtn = dialog.FindControl<Button>("CancelBtn");
        var helpToggleBtn = dialog.FindControl<Button>("HelpToggleBtn");
        var helpContentPanel = dialog.FindControl<StackPanel>("HelpContentPanel");

        // ── Help toggle ───────────────────────────────────────────
        helpToggleBtn!.Click += (s, args) =>
        {
            helpContentPanel!.IsVisible = !helpContentPanel!.IsVisible;
            var arrow = helpContentPanel!.IsVisible ? "▲" : "▼";
            var stackPanel = (StackPanel)helpToggleBtn.Content!;
            var textBlock = (TextBlock)stackPanel.Children[1];
            textBlock.Text = $"Token-Hilfe  {arrow}";
        };

        // ── Shared API client ──────────────────────────────────────
        var api = new HaApiClient(Config.GetConfigDir(), sslCheck!.IsChecked ?? false);
        api.LoadRegistration();

        // ── Update attempt counter ─────────────────────────────────
        void UpdateAttemptCounter()
        {
            attemptLabel.Text = api.FailedLoginAttempts > 0
                ? $"Versuch {api.FailedLoginAttempts}/{HaApiClient.MaxFailedLoginAttempts}"
                : "";
        }

        // ── Connect handler ────────────────────────────────────────
        connectBtn!.Click += async (s, args) =>
        {
            connectBtn.IsEnabled = false;
            cancelBtn!.IsEnabled = false;
            connectBtn.Content = "Verbindet…";
            statusBar.Text = "Verbinde mit Home Assistant…";
            statusBar.Foreground = grayBrush;

            try
            {
                await api.RegisterAsync(urlBox!.Text?.Trim() ?? "", tokenBox.Text?.Trim() ?? "");

                // Success
                var rawUrl = urlBox!.Text?.Trim() ?? "";
                config.HaUrl = rawUrl.StartsWith("http") ? rawUrl : $"https://{rawUrl}";
                config.HaToken = tokenBox.Text?.Trim() ?? "";
                config.VerifySsl = sslCheck!.IsChecked ?? false;
                config.Save();
                _haUrl = config.HaUrl;
                if (_statusLabel != null) _statusLabel.Text = $"✓ Verbunden: {_haUrl}";

                // Animate success
                statusBar.Text = "✓ Verbindung erfolgreich!";
                statusBar.Foreground = successBrush;
                connectBtn.Content = "✓ Verbunden";
                connectBtn.Background = successBrush;

                // Auto-close after 2 seconds
                await Task.Delay(2000);
                dialog.Close();
            }
            catch (Exception ex)
            {
                UpdateAttemptCounter();

                var message = ex.Message;
                if (ex is InvalidOperationException && message.Contains("Login fehlgeschlagen"))
                {
                    statusBar.Text = message;
                    statusBar.Foreground = highlightBrush;

                    // Add retry button if not present
                    var buttonRow = (StackPanel)((StackPanel)((Border)rootPanel.Child!).Child!).Children[^1];
                    var retryBtn = buttonRow.Children.OfType<Button>().FirstOrDefault(b => (b.Name ?? "") == "RetryBtn");
                    if (retryBtn == null)
                    {
                        retryBtn = new Button
                        {
                            Content = "🔄 Erneut versuchen",
                            Background = highlightBrush,
                            Foreground = whiteBrush,
                            CornerRadius = new CornerRadius(8),
                            Padding = new Thickness(16, 8),
                            FontSize = 13,
                            Name = "RetryBtn"
                        };
                        buttonRow.Children.Insert(1, retryBtn);
                        retryBtn.Click += (r_s, r_args) =>
                        {
                            api.ResetBlockState();
                            statusBar.Text = "Nicht verbunden";
                            statusBar.Foreground = grayBrush;
                            connectBtn.IsVisible = true;
                            connectBtn.IsEnabled = true;
                            connectBtn.Content = "Verbinden";
                            connectBtn.Background = accentBrush;
                            retryBtn.IsVisible = false;
                            attemptLabel.Text = "";
                        };
                    }
                    retryBtn.IsVisible = true;
                }
                else if (api.IsBlocked)
                {
                    statusBar.Text = "Login fehlgeschlagen. Token ungültig. Bitte überprüfe deinen Home Assistant Token in den Einstellungen.";
                    statusBar.Foreground = highlightBrush;
                    connectBtn.IsVisible = false;

                    var buttonRow = (StackPanel)((StackPanel)((Border)rootPanel.Child!).Child!).Children[^1];
                    var retryBtn = buttonRow.Children.OfType<Button>().FirstOrDefault(b => (b.Name ?? "") == "RetryBtn");
                    if (retryBtn == null)
                    {
                        retryBtn = new Button
                        {
                            Content = "🔄 Erneut versuchen",
                            Background = highlightBrush,
                            Foreground = whiteBrush,
                            CornerRadius = new CornerRadius(8),
                            Padding = new Thickness(16, 8),
                            FontSize = 13,
                            Name = "RetryBtn"
                        };
                        buttonRow.Children.Insert(1, retryBtn);
                        retryBtn.Click += (r_s, r_args) =>
                        {
                            api.ResetBlockState();
                            statusBar.Text = "Nicht verbunden";
                            statusBar.Foreground = grayBrush;
                            connectBtn.IsVisible = true;
                            connectBtn.IsEnabled = true;
                            connectBtn.Content = "Verbinden";
                            connectBtn.Background = accentBrush;
                            retryBtn.IsVisible = false;
                            attemptLabel.Text = "";
                        };
                    }
                    retryBtn.IsVisible = true;
                }
                else
                {
                    statusBar.Text = $"✗ Fehler: {ex.Message}";
                    statusBar.Foreground = highlightBrush;
                    connectBtn.IsEnabled = true;
                    connectBtn.Content = "Verbinden";
                    connectBtn.Background = accentBrush;
                }

                cancelBtn!.IsEnabled = true;
            }
        };

        // ── Cancel handler ─────────────────────────────────────────
        cancelBtn!.Click += (s, args) => dialog.Close();

        await dialog.ShowDialog(this);
    }

    private void OnResetDevice(object? sender, RoutedEventArgs e)
    {
        var api = new HaApiClient(Config.GetConfigDir());
        api.ResetDeviceId();
        if (_statusLabel != null)
            _statusLabel.Text = "Neue ID erstellt – App bitte neustarten!";
    }

    private async void OnQuickActions(object? sender, RoutedEventArgs e)
    {
        var config = Config.Load();
        var actions = LoadQuickActions(config);

        // ── Color palette ─────────────────────────────────────────
        var bgBrush = new SolidColorBrush(Avalonia.Media.Color.FromArgb(255, 26, 26, 46));      // #1A1A2E
        var panelBrush = new SolidColorBrush(Avalonia.Media.Color.FromArgb(255, 22, 33, 62));    // #16213E
        var accentBrush = new SolidColorBrush(Avalonia.Media.Color.FromArgb(255, 15, 52, 96));   // #0F3460
        var highlightBrush = new SolidColorBrush(Avalonia.Media.Color.FromArgb(255, 233, 69, 96)); // #E94560
        var successBrush = new SolidColorBrush(Avalonia.Media.Color.FromArgb(255, 76, 175, 80));

        var panel = new StackPanel { Margin = new Avalonia.Thickness(0), Spacing = 0 };

        // ── Accent bar ─────────────────────────────────────────────
        panel.Children.Add(new Border { Height = 4, Background = accentBrush });

        // ── Header ─────────────────────────────────────────────────
        panel.Children.Add(new Border
        {
            Background = panelBrush,
            Padding = new Avalonia.Thickness(20, 16, 20, 12),
            Child = new TextBlock { Text = "⚡ Quick Actions", FontSize = 18, FontWeight = Avalonia.Media.FontWeight.Bold, Foreground = Brushes.White }
        });

        if (actions.Count == 0)
        {
            panel.Children.Add(new Border
            {
                Background = bgBrush,
                Padding = new Avalonia.Thickness(20, 30),
                Child = new StackPanel
                {
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    Spacing = 12,
                    Children =
                    {
                        new TextBlock { Text = "📭", FontSize = 32, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center },
                        new TextBlock { Text = "Keine Quick Actions konfiguriert", FontSize = 15, Foreground = Brushes.White, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center },
                        new TextBlock { Text = "In config.json QuickActions hinzufügen:\n{ \"entityId\": \"light.wohnzimmer\", \"name\": \"Wohnzimmer Licht\" }", Foreground = Avalonia.Media.Brushes.Gray, FontSize = 12, TextWrapping = Avalonia.Media.TextWrapping.Wrap, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center },
                        new Button
                        {
                            Content = "OK",
                            Background = accentBrush, Foreground = Brushes.White,
                            CornerRadius = new Avalonia.CornerRadius(8),
                            Padding = new Avalonia.Thickness(24, 8),
                            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
                        }
                    }
                }
            });
        }
        else
        {
            var actionPanel = new StackPanel { Margin = new Avalonia.Thickness(12, 8, 12, 8), Spacing = 6 };

            var api = new HaApiClient(Config.GetConfigDir(), config.VerifySsl);
            api.LoadRegistration();

            foreach (var action in actions)
            {
                var card = new Border
                {
                    Background = panelBrush,
                    CornerRadius = new Avalonia.CornerRadius(8),
                    Padding = new Avalonia.Thickness(14, 10),
                    Margin = new Avalonia.Thickness(0, 2),
                    Child = new Grid
                    {
                        ColumnDefinitions = ColumnDefinitions.Parse("Auto,*,Auto"),
                        Children =
                        {
                            // Entity icon dot
                            new Border
                            {
                                Width = 8, Height = 8,
                                Background = highlightBrush,
                                CornerRadius = new Avalonia.CornerRadius(4),
                                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                                Margin = new Avalonia.Thickness(0, 0, 12, 0),
                                //[0]
                            }.WithGridColumn(0),
                            // Name
                            new TextBlock
                            {
                                Text = action.Name,
                                FontSize = 14,
                                Foreground = Brushes.White,
                                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                            }.WithGridColumn(1),
                            // Toggle button
                            new Button
                            {
                                Content = "⏻",
                                FontSize = 16,
                                Background = accentBrush, Foreground = Brushes.White,
                                CornerRadius = new Avalonia.CornerRadius(6),
                                Padding = new Avalonia.Thickness(10, 4),
                                Tag = action,
                                //[2]
                            }.WithGridColumn(2)
                        }
                    }
                };

                // Wire up the toggle button (Grid child[2] → Button)
                var grid = (Grid)card.Child!;
                var toggleBtn = grid.Children.OfType<Button>().FirstOrDefault();
                if (toggleBtn != null)
                {
                    toggleBtn.Click += async (s, args) =>
                    {
                        var b = s as Button;
                        var a = b?.Tag as QuickActionItem;
                        if (a == null) return;
                        b!.Background = highlightBrush;
                        b.Content = "⏳";
                        try
                        {
                            await api.ToggleEntityAsync(a.EntityId);
                            b.Content = "✓";
                            b.Background = successBrush;
                        }
                        catch
                        {
                            b.Content = "✗";
                            b.Background = highlightBrush;
                        }
                        await Task.Delay(1200);
                        b.Content = "⏻";
                        b.Background = accentBrush;
                    };
                }

                actionPanel.Children.Add(card);
            }

            panel.Children.Add(new Border
            {
                Background = bgBrush,
                Child = actionPanel
            });
        }

        var dialog = new Window
        {
            Title = "Quick Actions",
            Width = 420,
            Height = actions.Count == 0 ? 250 : Math.Max(180, 60 + actions.Count * 62),
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = bgBrush,
            Content = panel
        };

        // Wire close buttons (empty state)
        var closeBtns = panel.Children.OfType<Border>()
            .SelectMany(b => (b.Child as StackPanel)?.Children.OfType<Button>() ?? Enumerable.Empty<Button>());
        foreach (var cb in closeBtns) cb.Click += (s, a) => dialog.Close();

        await dialog.ShowDialog(this);
    }

    private static List<QuickActionItem> LoadQuickActions(Config config)
    {
        var result = new List<QuickActionItem>();
        try
        {
            var arr = System.Text.Json.JsonDocument.Parse(config.QuickActions).RootElement;
            foreach (var item in arr.EnumerateArray())
            {
                var entityId = item.TryGetProperty("entityId", out var eid) ? eid.GetString() ?? "" : "";
                var name = item.TryGetProperty("name", out var n) ? n.GetString() ?? entityId : entityId;
                if (!string.IsNullOrEmpty(entityId))
                    result.Add(new QuickActionItem(entityId, name));
            }
        }
        catch { }
        return result;
    }
}

public class QuickActionItem
{
    public string EntityId { get; }
    public string Name { get; }
    public QuickActionItem(string entityId, string name) { EntityId = entityId; Name = name; }
}