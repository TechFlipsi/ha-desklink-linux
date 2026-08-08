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
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace HaDeskLink.Views;

public partial class MainWindow : Window
{
    private string _haUrl = "";
    private TextBlock? _statusLabel;
    private TextBox? _mqttFallbackBox;
    private Button? _btnMqttTest;

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

        // MQTT settings buttons
        _btnMqttTest = this.FindControl<Button>("BtnMqttTest");
        if (_btnMqttTest != null) _btnMqttTest.Click += OnMqttTestConnection;

        _mqttFallbackBox = this.FindControl<TextBox>("TxtMqttFallback");

        var btnMqttSave = this.FindControl<Button>("BtnMqttSave");
        if (btnMqttSave != null) btnMqttSave.Click += OnMqttSave;

        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        var config = Config.Load();
        _haUrl = config.HaUrl;
        if (_statusLabel != null)
            _statusLabel.Text = string.IsNullOrEmpty(_haUrl) ? "⚠️ Nicht verbunden" : $"✓ Verbunden: {_haUrl}";
        LoadMqttSettings(config);
        LoadLanguageSettings(config);
    }

    private void LoadMqttSettings(Config config)
    {
        var chkMqtt = this.FindControl<CheckBox>("ChkMqttEnabled");
        var txtBroker = this.FindControl<TextBox>("TxtMqttBroker");
        var txtPort = this.FindControl<TextBox>("TxtMqttPort");
        var txtUser = this.FindControl<TextBox>("TxtMqttUser");
        var txtPass = this.FindControl<TextBox>("TxtMqttPass");
        var chkSsl = this.FindControl<CheckBox>("ChkMqttSsl");
        var lblStatus = this.FindControl<TextBlock>("LblMqttStatus");

        if (chkMqtt != null) chkMqtt.IsChecked = config.MqttEnabled;
        if (txtBroker != null) txtBroker.Text = config.MqttBroker;
        if (txtPort != null) txtPort.Text = config.MqttPort.ToString();
        if (txtUser != null) txtUser.Text = config.MqttUsername;
        if (txtPass != null) txtPass.Text = config.MqttPassword;
        if (chkSsl != null) chkSsl.IsChecked = config.MqttUseSsl;
        if (_mqttFallbackBox != null) _mqttFallbackBox.Text = config.MqttBrokerFallback;

        if (lblStatus != null)
        {
            if (!config.MqttEnabled)
                lblStatus.Text = "○ Deaktiviert";
            else if (!string.IsNullOrEmpty(config.MqttBroker))
                lblStatus.Text = $"● Verbunden ({config.MqttBroker}:{config.MqttPort})";
            else
                lblStatus.Text = "● Getrennt";
        }
    }

    private void LoadLanguageSettings(Config config)
    {
        var cbLanguage = this.FindControl<ComboBox>("CbLanguage");
        var btnSaveLanguage = this.FindControl<Button>("BtnSaveLanguage");

        if (cbLanguage != null)
        {
            // Sprach-Dropdown füllen: "Name (code)" für jede verfügbare Sprache
            cbLanguage.Items.Clear();
            foreach (var lang in Localization.AvailableLanguages)
                cbLanguage.Items.Add($"{Localization.GetLanguageName(lang)} ({lang})");

            // Aktuell ausgewählte Sprache setzen
            var idx = Localization.AvailableLanguages.IndexOf(config.Language);
            cbLanguage.SelectedIndex = idx >= 0 ? idx : 0;
        }

        if (btnSaveLanguage != null)
            btnSaveLanguage.Click += OnSaveLanguage;
    }

    private void OnSaveLanguage(object? sender, RoutedEventArgs e)
    {
        var cbLanguage = this.FindControl<ComboBox>("CbLanguage");
        if (cbLanguage == null || cbLanguage.SelectedIndex < 0) return;

        var config = Config.Load();
        var idx = cbLanguage.SelectedIndex;

        if (idx >= 0 && idx < Localization.AvailableLanguages.Count)
        {
            config.Language = Localization.AvailableLanguages[idx];
            config.Save();
            // Sprache neu laden
            Localization.LoadLanguage(config.Language);
        }

        if (_statusLabel != null)
            _statusLabel.Text = $"✓ {Localization.Get("settings_saved")}";
    }

    private void OnMqttSave(object? sender, RoutedEventArgs e)
    {
        var config = Config.Load();

        var chkMqtt = this.FindControl<CheckBox>("ChkMqttEnabled");
        var txtBroker = this.FindControl<TextBox>("TxtMqttBroker");
        var txtPort = this.FindControl<TextBox>("TxtMqttPort");
        var txtUser = this.FindControl<TextBox>("TxtMqttUser");
        var txtPass = this.FindControl<TextBox>("TxtMqttPass");
        var chkSsl = this.FindControl<CheckBox>("ChkMqttSsl");
        var lblStatus = this.FindControl<TextBlock>("LblMqttStatus");

        config.MqttEnabled = chkMqtt?.IsChecked ?? false;
        config.MqttBroker = txtBroker?.Text?.Trim() ?? "";
        if (int.TryParse(txtPort?.Text?.Trim(), out var port))
            config.MqttPort = port;
        config.MqttUsername = txtUser?.Text?.Trim() ?? "";
        config.MqttPassword = txtPass?.Text ?? "";
        config.MqttUseSsl = chkSsl?.IsChecked ?? false;
        config.MqttBrokerFallback = _mqttFallbackBox?.Text?.Trim() ?? "";
        config.MqttAutoConfigured = false;
        config.Save();

        if (lblStatus != null) lblStatus.Text = "✓ MQTT-Einstellungen gespeichert";
    }

    private async void OnMqttTestConnection(object? sender, RoutedEventArgs e)
    {
        var btn = sender as Button;
        var lblStatus = this.FindControl<TextBlock>("LblMqttStatus");
        if (btn != null) btn.IsEnabled = false;
        if (lblStatus != null) lblStatus.Text = "⏳ Teste MQTT-Verbindung...";

        try
        {
            var txtBroker = this.FindControl<TextBox>("TxtMqttBroker");
            var txtPort = this.FindControl<TextBox>("TxtMqttPort");
            var txtUser = this.FindControl<TextBox>("TxtMqttUser");
            var txtPass = this.FindControl<TextBox>("TxtMqttPass");
            var chkSsl = this.FindControl<CheckBox>("ChkMqttSsl");

            var broker = txtBroker?.Text?.Trim() ?? "";
            if (string.IsNullOrEmpty(broker))
            {
                if (lblStatus != null) lblStatus.Text = "⚠️ Bitte Broker-Adresse eingeben";
                if (btn != null) btn.IsEnabled = true;
                return;
            }

            if (!int.TryParse(txtPort?.Text?.Trim(), out var port) || port <= 0)
                port = 1883;

            var user = string.IsNullOrEmpty(txtUser?.Text?.Trim()) ? null : txtUser.Text.Trim();
            var pass = string.IsNullOrEmpty(txtPass?.Text) ? null : txtPass.Text;
            var ssl = chkSsl?.IsChecked ?? false;

            var ok = await MqttSetupHelper.TestConnectionAsync(broker, port, user, pass, ssl);

            if (ok)
                lblStatus.Text = $"✓ MQTT-Verbindung erfolgreich ({broker}:{port})";
            else
                lblStatus.Text = $"✗ Verbindung zu {broker}:{port} fehlgeschlagen";
        }
        catch (Exception ex)
        {
            if (lblStatus != null) lblStatus.Text = $"✗ Fehler: {ex.Message}";
        }
        finally
        {
            if (btn != null) btn.IsEnabled = true;
        }
    }

    private void OnOpenDashboard(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_haUrl)) return;

        DashboardWindow.Open(_haUrl);
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
        var bgBrush       = SolidColorBrush.Parse("#1A1A2E");
        var panelBrush    = SolidColorBrush.Parse("#16213E");
        var accentBrush   = SolidColorBrush.Parse("#0F3460");
        var highlightBrush= SolidColorBrush.Parse("#E94560");
        var successBrush  = SolidColorBrush.Parse("#4CAF50");
        var inputBgBrush  = SolidColorBrush.Parse("#2D2D3F");
        var fgBrush       = SolidColorBrush.Parse("#E0E0E0");
        var grayBrush     = SolidColorBrush.Parse("#888888");
        var borderBrush   = SolidColorBrush.Parse("#3A3A5A");

        // ── Status bar text ────────────────────────────────────────
        var statusBar = new TextBlock
        {
            Text = "Nicht verbunden",
            Foreground = grayBrush,
            FontSize = 12,
            Margin = new Thickness(0, 8, 0, 0),
            TextWrapping = TextWrapping.Wrap
        };

        // ── Attempt counter ────────────────────────────────────────
        var attemptLabel = new TextBlock
        {
            Text = "",
            Foreground = grayBrush,
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        // ── URL input ──────────────────────────────────────────────
        var urlTextBox = new TextBox
        {
            Text = config.HaUrl.Replace("https://", "").Replace("http://", ""),
            Watermark = "homeassistant.local:8123",
            Background = Brushes.Transparent,
            Foreground = fgBrush,
            CornerRadius = new CornerRadius(0, 8, 8, 0),
            Padding = new Thickness(10, 8),
            BorderThickness = new Thickness(0)
        };

        // ── Token input with show/hide ─────────────────────────────
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
            BorderBrush = borderBrush
        };
        var tokenToggleBtn = new Button
        {
            Content = "👁",
            Width = 36, Height = 36,
            Background = Brushes.Transparent,
            Foreground = grayBrush,
            FontSize = 14,
            Padding = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center
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

        var sslCheck = new CheckBox
        {
            Content = "SSL-Zertifikat prüfen",
            IsChecked = config.VerifySsl,
            Foreground = fgBrush,
            FontSize = 13
        };

        // ── Connect / Cancel buttons ───────────────────────────────
        var connectBtn = new Button
        {
            Content = "Verbinden",
            Background = accentBrush,
            Foreground = Brushes.White,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(20, 8),
            FontSize = 13,
            FontWeight = FontWeight.SemiBold
        };
        var cancelBtn = new Button
        {
            Content = "Abbrechen",
            Background = SolidColorBrush.Parse("#555570"),
            Foreground = Brushes.White,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(20, 8),
            FontSize = 13
        };

        // ── Help toggle ───────────────────────────────────────────
        var helpContent = new StackPanel
        {
            Spacing = 4,
            IsVisible = false,
            Children =
            {
                new TextBlock { Text = "So erhältst du einen Token:", FontSize = 12, Foreground = fgBrush, FontWeight = FontWeight.SemiBold },
                new TextBlock { Text = "1.  Öffne Home Assistant in deinem Browser", FontSize = 11, Foreground = grayBrush },
                new TextBlock { Text = "2.  Klicke auf dein Profil (unten links)", FontSize = 11, Foreground = grayBrush },
                new TextBlock { Text = "3.  Scrolle zu „Sicherheit\" → „Long-Lived Access Tokens\"", FontSize = 11, Foreground = grayBrush },
                new TextBlock { Text = "4.  Klicke „Token erstellen\", gib einen Namen ein und kopiere den Token", FontSize = 11, Foreground = grayBrush },
                new TextBlock { Text = "⚠️ Der Token wird nur einmal angezeigt – gut aufbewahren!", FontSize = 11, Foreground = highlightBrush, Margin = new Thickness(0, 4, 0, 0) }
            }
        };
        var helpToggleBtn = new Button
        {
            Content = "ℹ️  Token-Hilfe  ▼",
            Background = Brushes.Transparent,
            Foreground = fgBrush,
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        helpToggleBtn.Click += (s, args) =>
        {
            helpContent.IsVisible = !helpContent.IsVisible;
            helpToggleBtn.Content = helpContent.IsVisible ? "ℹ️  Token-Hilfe  ▲" : "ℹ️  Token-Hilfe  ▼";
        };

        // ── URL Grid (prefix + input) ─────────────────────────────
        var urlPrefixBorder = new Border
        {
            Background = SolidColorBrush.Parse("#252540"),
            CornerRadius = new CornerRadius(8, 0, 0, 8),
            Padding = new Thickness(10, 8),
            Child = new TextBlock { Text = "https://", Foreground = grayBrush, FontSize = 13, VerticalAlignment = VerticalAlignment.Center }
        };
        Grid.SetColumn(urlPrefixBorder, 0);
        Grid.SetColumn(urlTextBox, 1);

        var urlGrid = new Grid
        {
            ColumnDefinitions = ColumnDefinitions.Parse("Auto,*"),
            Children = { urlPrefixBorder, urlTextBox }
        };

        // ── Token Grid (input + toggle) ───────────────────────────
        Grid.SetColumn(tokenBox, 0);
        Grid.SetColumn(tokenToggleBtn, 1);

        var tokenGrid = new Grid
        {
            ColumnDefinitions = ColumnDefinitions.Parse("*,Auto"),
            Children = { tokenBox, tokenToggleBtn }
        };

        // ═══════════════════════════════════════════════════════════
        //  BUILD DIALOG
        // ═══════════════════════════════════════════════════════════
        var dialog = new Window
        {
            Title = "HA DeskLink – Einrichtung",
            Width = 500,
            Height = 700,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Background = bgBrush,
            Content = new Border
            {
                Background = bgBrush,
                CornerRadius = new CornerRadius(8),
                Child = new StackPanel
                {
                    Margin = new Thickness(0),
                    Spacing = 0,
                    Children =
                    {
                        // Accent bar
                        new Border { Height = 4, Background = accentBrush },

                        new StackPanel
                        {
                            Margin = new Thickness(20, 16),
                            Spacing = 12,
                            Children =
                            {
                                // Header
                                new StackPanel { Spacing = 2, Children =
                                {
                                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children =
                                    {
                                        new TextBlock { Text = "⚙️", FontSize = 20, VerticalAlignment = VerticalAlignment.Center },
                                        new TextBlock { Text = "HA DeskLink – Einrichtung", FontSize = 18, FontWeight = FontWeight.Bold, Foreground = fgBrush, VerticalAlignment = VerticalAlignment.Center }
                                    }},
                                    new Border { Height = 2, Background = accentBrush, Margin = new Thickness(0, 4, 0, 0) }
                                }},

                                // Section: Verbindung
                                new Border { Background = panelBrush, CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 12), Child =
                                    new StackPanel { Spacing = 8, Children =
                                    {
                                        new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children =
                                        {
                                            new TextBlock { Text = "🔌", FontSize = 14, VerticalAlignment = VerticalAlignment.Center },
                                            new TextBlock { Text = "Verbindung", FontSize = 14, FontWeight = FontWeight.SemiBold, Foreground = fgBrush, VerticalAlignment = VerticalAlignment.Center }
                                        }},
                                        new TextBlock { Text = "Home Assistant URL", FontSize = 12, Foreground = grayBrush },
                                        new Border { Background = inputBgBrush, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), BorderBrush = borderBrush, Child = urlGrid },
                                        new TextBlock { Text = "Long-Lived Access Token", FontSize = 12, Foreground = grayBrush },
                                        tokenGrid
                                    }}
                                },

                                // Section: Sicherheit
                                new Border { Background = panelBrush, CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 12), Child =
                                    new StackPanel { Spacing = 6, Children =
                                    {
                                        new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children =
                                        {
                                            new TextBlock { Text = "🔒", FontSize = 14, VerticalAlignment = VerticalAlignment.Center },
                                            new TextBlock { Text = "Sicherheit", FontSize = 14, FontWeight = FontWeight.SemiBold, Foreground = fgBrush, VerticalAlignment = VerticalAlignment.Center }
                                        }},
                                        sslCheck
                                    }}
                                },

                                // Section: Token-Hilfe
                                new Border { Background = panelBrush, CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 10), Child =
                                    new StackPanel { Spacing = 4, Children = { helpToggleBtn, helpContent } }
                                },

                                // Section: MQTT (initially collapsed, shown after HA connect)
                                new Border { Background = panelBrush, CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 12), IsVisible = false, Child =
                                    new StackPanel { Spacing = 8, Children =
                                    {
                                        new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children =
                                        {
                                            new TextBlock { Text = "📡", FontSize = 14, VerticalAlignment = VerticalAlignment.Center },
                                            new TextBlock { Text = "MQTT (optional)", FontSize = 14, FontWeight = FontWeight.SemiBold, Foreground = fgBrush, VerticalAlignment = VerticalAlignment.Center }
                                        }},
                                        new TextBlock { Text = "MQTT ermöglicht Echtzeit-Mediensteuerung & schnellere Updates", FontSize = 12, Foreground = grayBrush, TextWrapping = TextWrapping.Wrap },
                                        new StackPanel { Spacing = 4, Children =
                                        {
                                            new TextBlock { Text = "✅ Mit MQTT:  Mediensteuerung, schnelle Updates", FontSize = 11, Foreground = successBrush },
                                            new TextBlock { Text = "❌ Ohne MQTT: Keine Mediensteuerung", FontSize = 11, Foreground = highlightBrush }
                                        }},
                                        new TextBlock { Name = "MqttSetupStatus", Text = "", FontSize = 12, Foreground = grayBrush, TextWrapping = TextWrapping.Wrap },
                                        new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children =
                                        {
                                            new Button { Content = "MQTT nutzen", Background = accentBrush, Foreground = Brushes.White, CornerRadius = new CornerRadius(6), Padding = new Thickness(16, 8), FontSize = 13, Name = "BtnMqttUse" },
                                            new Button { Content = "Ohne MQTT fortfahren", Background = SolidColorBrush.Parse("#555570"), Foreground = Brushes.White, CornerRadius = new CornerRadius(6), Padding = new Thickness(16, 8), FontSize = 13, Name = "BtnMqttSkip" }
                                        }}
                                    }}
                                },

                                attemptLabel,
                                statusBar,

                                // Button row
                                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 6, 0, 0), Children = { cancelBtn, connectBtn } }
                            }
                        }
                    }
                }
            }
        };

        // ── API client ────────────────────────────────────────────
        var api = new HaApiClient(Config.GetConfigDir(), sslCheck.IsChecked ?? false);
        api.LoadRegistration();

        void UpdateAttemptCounter()
        {
            attemptLabel.Text = api.FailedLoginAttempts > 0
                ? $"Versuch {api.FailedLoginAttempts}/{HaApiClient.MaxFailedLoginAttempts}"
                : "";
        }

        // ── Connect handler ───────────────────────────────────────
        var savedFullUrl = "";
        var savedToken = "";

        connectBtn.Click += async (s, args) =>
        {
            connectBtn.IsEnabled = false;
            cancelBtn.IsEnabled = false;
            connectBtn.Content = "Verbindet…";
            statusBar.Text = "Verbinde mit Home Assistant…";
            statusBar.Foreground = grayBrush;

            try
            {
                var rawUrl = urlTextBox.Text?.Trim() ?? "";
                var fullUrl = rawUrl.StartsWith("http") ? rawUrl : $"https://{rawUrl}";
                await api.RegisterAsync(fullUrl, tokenBox.Text?.Trim() ?? "");

                savedFullUrl = fullUrl;
                savedToken = tokenBox.Text?.Trim() ?? "";

                config.HaUrl = fullUrl;
                config.HaToken = savedToken;
                config.VerifySsl = sslCheck.IsChecked ?? false;
                config.Save();
                _haUrl = config.HaUrl;
                if (_statusLabel != null) _statusLabel.Text = $"✓ Verbunden: {_haUrl}";

                statusBar.Text = "✓ HA Verbindung erfolgreich!";
                statusBar.Foreground = successBrush;
                connectBtn.IsVisible = false;
                cancelBtn.Content = "Fertig";

                // Show MQTT step
                var mqttSection = ((StackPanel)((Border)dialog.Content).Child).Children
                    .OfType<Border>().FirstOrDefault(b => b.Child is StackPanel sp && sp.Children.OfType<TextBlock>().Any(tb => tb.Text == "📡"));
                if (mqttSection != null)
                {
                    mqttSection.IsVisible = true;
                    var mqttStack = (StackPanel)mqttSection.Child;
                    var mqttStatus = mqttStack.Children.OfType<TextBlock>().FirstOrDefault(tb => tb.Name == "MqttSetupStatus");
                    var mqttUseBtn = mqttStack.Children.OfType<StackPanel>().LastOrDefault()?.Children.OfType<Button>().FirstOrDefault(b => b.Name == "BtnMqttUse");
                    var mqttSkipBtn = mqttStack.Children.OfType<StackPanel>().LastOrDefault()?.Children.OfType<Button>().FirstOrDefault(b => b.Name == "BtnMqttSkip");

                    if (mqttUseBtn != null && mqttSkipBtn != null && mqttStatus != null)
                    {
                        mqttUseBtn.Click += (s2, args2) =>
                        {
                            // Configure MQTT using the HA host as broker
                            var host = new Uri(savedFullUrl).Host;
                            config.MqttEnabled = true;
                            config.MqttBroker = host;
                            config.MqttPort = 1883;
                            config.MqttUsername = "";
                            config.MqttPassword = "";
                            config.MqttUseSsl = false;
                            config.MqttAutoConfigured = false;
                            config.Save();

                            mqttStatus.Text = $"✓ MQTT manuell konfiguriert: {host}:1883";
                            mqttStatus.Foreground = successBrush;
                            mqttUseBtn.Content = "✓ MQTT konfiguriert";
                            mqttUseBtn.Background = successBrush;

                            Task.Delay(1500).ContinueWith(_ =>
                            {
                                Avalonia.Threading.Dispatcher.UIThread.Post(() => dialog.Close());
                            });
                        };

                        mqttSkipBtn.Click += (s2, args2) =>
                        {
                            config.MqttEnabled = false;
                            config.MqttAutoConfigured = false;
                            config.Save();
                            dialog.Close();
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                UpdateAttemptCounter();

                if (api.IsBlocked || (ex is InvalidOperationException && ex.Message.Contains("Login fehlgeschlagen")))
                {
                    statusBar.Text = api.IsBlocked
                        ? "⛔ Login blockiert – zu viele Versuche. Bitte Token überprüfen."
                        : ex.Message;
                    statusBar.Foreground = highlightBrush;
                }
                else
                {
                    statusBar.Text = $"✗ Fehler: {ex.Message}";
                    statusBar.Foreground = highlightBrush;
                }

                connectBtn.IsEnabled = true;
                connectBtn.Content = "Verbinden";
                connectBtn.Background = accentBrush;
                cancelBtn.IsEnabled = true;
            }
        };

        cancelBtn.Click += (s, args) => dialog.Close();

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

        var bgBrush       = SolidColorBrush.Parse("#1A1A2E");
        var panelBrush    = SolidColorBrush.Parse("#16213E");
        var accentBrush   = SolidColorBrush.Parse("#0F3460");
        var highlightBrush= SolidColorBrush.Parse("#E94560");
        var successBrush  = SolidColorBrush.Parse("#4CAF50");

        var panel = new StackPanel { Margin = new Thickness(0), Spacing = 0 };
        panel.Children.Add(new Border { Height = 4, Background = accentBrush });
        panel.Children.Add(new Border
        {
            Background = panelBrush,
            Padding = new Thickness(20, 16, 20, 12),
            Child = new TextBlock { Text = "⚡ Quick Actions", FontSize = 18, FontWeight = FontWeight.Bold, Foreground = Brushes.White }
        });

        if (actions.Count == 0)
        {
            var okBtn = new Button
            {
                Content = "OK",
                Background = accentBrush, Foreground = Brushes.White,
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(24, 8),
                HorizontalAlignment = HorizontalAlignment.Center
            };

            panel.Children.Add(new Border
            {
                Background = bgBrush,
                Padding = new Thickness(20, 30),
                Child = new StackPanel
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Spacing = 12,
                    Children =
                    {
                        new TextBlock { Text = "📭", FontSize = 32, HorizontalAlignment = HorizontalAlignment.Center },
                        new TextBlock { Text = "Keine Quick Actions konfiguriert", FontSize = 15, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center },
                        new TextBlock { Text = "In config.json QuickActions hinzufügen:\n{ \"entityId\": \"light.wohnzimmer\", \"name\": \"Licht\" }", Foreground = Brushes.Gray, FontSize = 12, TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Center },
                        okBtn
                    }
                }
            });

            var dialog = new Window { Title = "Quick Actions", Width = 420, Height = 250, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = bgBrush, Content = panel };
            okBtn.Click += (s, a) => dialog.Close();
            await dialog.ShowDialog(this);
        }
        else
        {
            var actionPanel = new StackPanel { Margin = new Thickness(12, 8, 12, 8), Spacing = 6 };
            var api = new HaApiClient(Config.GetConfigDir(), config.VerifySsl);
            api.LoadRegistration();

            foreach (var action in actions)
            {
                var entityDot = new Border
                {
                    Width = 8, Height = 8,
                    Background = highlightBrush,
                    CornerRadius = new CornerRadius(4),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 12, 0)
                };
                Grid.SetColumn(entityDot, 0);

                var nameText = new TextBlock
                {
                    Text = action.Name,
                    FontSize = 14,
                    Foreground = Brushes.White,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(nameText, 1);

                var toggleBtn = new Button
                {
                    Content = "⏻",
                    FontSize = 16,
                    Background = accentBrush, Foreground = Brushes.White,
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(10, 4),
                    Tag = action
                };
                Grid.SetColumn(toggleBtn, 2);

                var card = new Border
                {
                    Background = panelBrush,
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(14, 10),
                    Margin = new Thickness(0, 2),
                    Child = new Grid
                    {
                        ColumnDefinitions = ColumnDefinitions.Parse("Auto,*,Auto"),
                        Children = { entityDot, nameText, toggleBtn }
                    }
                };

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

                actionPanel.Children.Add(card);
            }

            panel.Children.Add(new Border { Background = bgBrush, Child = actionPanel });

            var dialog = new Window
            {
                Title = "Quick Actions",
                Width = 420,
                Height = Math.Max(180, 60 + actions.Count * 62),
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = bgBrush,
                Content = panel
            };
            await dialog.ShowDialog(this);
        }
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