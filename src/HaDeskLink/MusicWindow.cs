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
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HaDeskLink;

/// <summary>
/// Music Assistant window — full MA control inside HA DeskLink.
/// Layout: sidebar (players list) + main area (search/library/queue/player).
/// Matches the v5 dark theme (like SettingsWindow/QuickActionWindow).
/// Requires MA config (MaHost/MaPort/MaToken) — shows setup hint if missing.
/// </summary>
public class MusicWindow : Window
{
    private readonly Config _config;
    private readonly ListBox _playersList = new();
    private readonly TextBox _searchBox = new();
    private readonly StackPanel _resultsPanel = new();
    private readonly StackPanel _queuePanel = new();
    private TextBlock _nowPlayingTitle = new();
    private TextBlock _nowPlayingArtist = new();
    private TextBlock _statusText = new();
    private Button _btnPlayPause = new();
    private Button _btnNext = new();
    private Button _btnPrev = new();
    private Slider _volumeSlider = new();
    private TextBlock _volumeLabel = new();

    private MaPlayer? _selectedPlayer;
    private MaQueue? _selectedQueue;
    private bool _suppressVolumeEvent;

    public MusicWindow(Config config)
    {
        _config = config;
        Title = $"HA DeskLink – {Localization.Get("music_window_title")}";
        Width = 1080; Height = 680;
        MinWidth = 820; MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.Parse("#1A1A2E"));

        Content = BuildLayout();
        WireSidebarButtons();
        _ = InitializeAsync();
    }

    private Control BuildLayout()
    {
        var dock = new DockPanel();

        // ─── Header ───
        var header = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#0F3460")),
            Padding = new Thickness(16, 10)
        };
        DockPanel.SetDock(header, Dock.Top);
        var headerGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        var title = new TextBlock
        {
            Text = "🎵 " + Localization.Get("music_window_title"),
            FontSize = 18,
            FontWeight = FontWeight.Bold,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(title, 0);
        _statusText = new TextBlock
        {
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.Parse("#8C8CA0")),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0)
        };
        Grid.SetColumn(_statusText, 1);
        var btnClose = new Button { Content = "✕", Background = Brushes.Transparent, Foreground = Brushes.White, BorderThickness = new Thickness(0) };
        btnClose.Click += (s, e) => Close();
        Grid.SetColumn(btnClose, 2);
        headerGrid.Children.Add(title);
        headerGrid.Children.Add(_statusText);
        headerGrid.Children.Add(btnClose);
        header.Child = headerGrid;
        dock.Children.Add(header);

        // ─── Sidebar: Players ───
        var sidebar = new Border
        {
            Width = 230,
            Background = new SolidColorBrush(Color.Parse("#16213E")),
            Padding = new Thickness(8)
        };
        DockPanel.SetDock(sidebar, Dock.Left);
        var sidebarDock = new DockPanel();
        var sidebarLabel = new TextBlock
        {
            Text = "Player",
            FontSize = 12,
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(Color.Parse("#8C8CA0")),
            Margin = new Thickness(8, 10, 0, 6)
        };
        sidebarDock.Children.Add(sidebarLabel);
        DockPanel.SetDock(sidebarLabel, Dock.Top);

        _playersList.Background = Brushes.Transparent;
        _playersList.Margin = new Thickness(0, 4, 0, 0);
        sidebarDock.Children.Add(_playersList);
        sidebar.Child = sidebarDock;
        dock.Children.Add(sidebar);

        // ─── Main area ───
        var main = new DockPanel { Margin = new Thickness(12) };

        // Now-playing bar (bottom)
        var nowPlaying = BuildNowPlayingBar();
        DockPanel.SetDock(nowPlaying, Dock.Bottom);
        dock.Children.Add(nowPlaying);

        // Search bar (top)
        var searchPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 0, 10) };
        _searchBox.Watermark = Localization.Get("music_search_placeholder");
        _searchBox.Height = 36;
        _searchBox.Background = new SolidColorBrush(Color.Parse("#0F3460"));
        _searchBox.Foreground = Brushes.White;
        _searchBox.BorderBrush = new SolidColorBrush(Color.Parse("#1A5276"));
        _searchBox.CornerRadius = new CornerRadius(6);
        _searchBox.HorizontalAlignment = HorizontalAlignment.Stretch;
        var btnSearch = new Button { Content = "🔍", Width = 42, Height = 36, CornerRadius = new CornerRadius(6) };
        btnSearch.Click += OnSearchClick;
        searchPanel.Children.Add(_searchBox);
        searchPanel.Children.Add(btnSearch);
        DockPanel.SetDock(searchPanel, Dock.Top);
        dock.Children.Add(searchPanel);

        // Results + queue split
        var split = new Grid { ColumnDefinitions = new ColumnDefinitions("*,15,340") };

        var resultsScroll = new ScrollViewer { Content = _resultsPanel, Margin = new Thickness(0, 0, 4, 0) };
        var resultsBorder = new Border { Child = resultsScroll, CornerRadius = new CornerRadius(8), Background = new SolidColorBrush(Color.Parse("#16213E")), Padding = new Thickness(8) };
        Grid.SetColumn(resultsBorder, 0);
        split.Children.Add(resultsBorder);

        var queueBorder = new Border
        {
            Child = new ScrollViewer { Content = _queuePanel },
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.Parse("#16213E")),
            Padding = new Thickness(8)
        };
        Grid.SetColumn(queueBorder, 2);
        split.Children.Add(queueBorder);

        dock.Children.Add(split);
        return dock;
    }

    private Control BuildNowPlayingBar()
    {
        var bar = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#0F3460")),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 10),
            Margin = new Thickness(0, 10, 0, 0)
        };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto,Auto,Auto,240") };

        var titleStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        _nowPlayingTitle = new TextBlock { FontSize = 14, FontWeight = FontWeight.Bold, Foreground = Brushes.White, Text = "—" };
        _nowPlayingArtist = new TextBlock { FontSize = 12, Foreground = new SolidColorBrush(Color.Parse("#8C8CA0")), Text = "Nichts abgespielt" };
        titleStack.Children.Add(_nowPlayingTitle);
        titleStack.Children.Add(_nowPlayingArtist);
        Grid.SetColumn(titleStack, 0);
        grid.Children.Add(titleStack);

        _btnPrev = new Button { Content = "⏮", FontSize = 16, Background = Brushes.Transparent, Foreground = Brushes.White, BorderThickness = new Thickness(0) };
        _btnPrev.Click += (s, e) => _ = DoQueueCommand(c => c.PreviousAsync(RequireQueueId()));
        Grid.SetColumn(_btnPrev, 2);
        grid.Children.Add(_btnPrev);

        _btnPlayPause = new Button { Content = "▶", FontSize = 18, Background = new SolidColorBrush(Color.Parse("#1A5276")), Foreground = Brushes.White, Width = 44, Height = 36, CornerRadius = new CornerRadius(6) };
        _btnPlayPause.Click += (s, e) => _ = DoQueueCommand(c => c.PlayPauseAsync(RequireQueueId()));
        Grid.SetColumn(_btnPlayPause, 3);
        grid.Children.Add(_btnPlayPause);

        _btnNext = new Button { Content = "⏭", FontSize = 16, Background = Brushes.Transparent, Foreground = Brushes.White, BorderThickness = new Thickness(0) };
        _btnNext.Click += (s, e) => _ = DoQueueCommand(c => c.NextAsync(RequireQueueId()));
        Grid.SetColumn(_btnNext, 4);
        grid.Children.Add(_btnNext);

        var volStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0) };
        _volumeSlider = new Slider { Minimum = 0, Maximum = 100, Width = 130, VerticalAlignment = VerticalAlignment.Center };
        _volumeSlider.ValueChanged += OnVolumeChanged;
        _volumeLabel = new TextBlock { Text = "70%", Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, FontSize = 12 };
        volStack.Children.Add(_volumeSlider);
        volStack.Children.Add(_volumeLabel);
        Grid.SetColumn(volStack, 5);
        grid.Children.Add(volStack);

        var btnShuffle = new Button { Content = "🔀", Background = Brushes.Transparent, Foreground = Brushes.White, BorderThickness = new Thickness(0), Margin = new Thickness(10, 0, 0, 0) };
        btnShuffle.Click += (s, e) => _ = ToggleShuffle();
        Grid.SetColumn(btnShuffle, 6);
        grid.Children.Add(btnShuffle);

        bar.Child = grid;
        return bar;
    }

    private void WireSidebarButtons()
    {
        _playersList.SelectionChanged += async (s, e) =>
        {
            if (_playersList.SelectedItem is ListBoxItem item && item.Tag is string playerId)
            {
                await SelectPlayerInWindowAsync(playerId);
            }
        };
        _searchBox.KeyDown += (s, e) => { if (e.Key == Avalonia.Input.Key.Enter) OnSearchClick(s, new RoutedEventArgs()); };
    }

    private async Task InitializeAsync()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_config.MaHost))
            {
                _statusText.Text = "❌ MA nicht konfiguriert (Einstellungen → Music Assistant)";
                return;
            }
            _statusText.Text = $"Verbinde mit {_config.MaHost}:{_config.MaPort}...";
            var client = await MaManager.Instance.GetClientAsync();
            _statusText.Text = $"✅ Verbunden – {client.ServerInfo?.Name ?? "MA"} (v{client.ServerInfo?.ServerVersion})";
            MaManager.Instance.StateChanged += OnStateChanged;
            RefreshPlayers();
            RefreshState();
            _ = LoadFavoritesAsync();
        }
        catch (Exception ex)
        {
            _statusText.Text = $"❌ Verbindung fehlgeschlagen: {ex.Message}";
        }
    }

    private void OnStateChanged()
    {
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                _selectedPlayer = MaManager.Instance.SelectedPlayer;
                _selectedQueue = MaManager.Instance.SelectedQueue;
                RefreshPlayers();
                RefreshState();
                _ = LoadQueueItemsAsync();
            }
            catch { }
        });
    }
    private void RefreshPlayers()
    {
        var players = MaManager.Instance.Players;
        var selected = _selectedPlayer?.PlayerId;
        _playersList.Items.Clear();
        foreach (var p in players)
        {
            var icon = p.State switch
            {
                "playing" => "▶",
                "paused" => "⏸",
                _ => "⏹"
            };
            var item = new ListBoxItem
            {
                Tag = p.PlayerId,
                Content = new TextBlock
                {
                    Text = $"{icon} {p.Name}",
                    Foreground = Brushes.White,
                    FontSize = 13
                },
                Padding = new Thickness(8, 6),
                IsSelected = p.PlayerId == selected
            };
            _playersList.Items.Add(item);
        }
        SyncVolumeSlider();
    }

    /// <summary>Sync the volume slider with the selected player WITHOUT firing
    /// the user-changed handler (suppress flag). Must run on the UI thread.</summary>
    private void SyncVolumeSlider()
    {
        if (_selectedPlayer?.VolumeLevel is int vol)
        {
            _suppressVolumeEvent = true;
            try
            {
                _volumeSlider.Value = vol;
                _volumeLabel.Text = $"{vol}%";
            }
            finally { _suppressVolumeEvent = false; }
        }
    }

    private void RefreshState()
    {
        var queue = _selectedQueue;
        var current = queue?.CurrentItem;
        if (current != null)
        {
            _nowPlayingTitle.Text = current.Name;
            var artists = current.Artists != null ? string.Join(", ", current.Artists.Select(a => a.Name)) : "";
            _nowPlayingArtist.Text = artists;
        }
        else
        {
            _nowPlayingTitle.Text = _selectedPlayer != null ? "Nichts abgespielt" : "Kein Player gewählt";
            _nowPlayingArtist.Text = _selectedPlayer?.Name ?? "";
        }
        // Player-State-Icon: playing → ⏸ (Pause-Button), paused/idle → ▶ (Play-Button).
        // Fallback auf den Player-State, falls die Queue (noch) keinen State liefert.
        var state = _selectedQueue?.State ?? _selectedPlayer?.State;
        _btnPlayPause.Content = state == "playing" ? "⏸" : "▶";
    }

    private async Task LoadQueueItemsAsync()
    {
        if (_selectedPlayer == null) return;
        // Snapshot des aktuellen Tracks vor den awaits — zum Hervorheben der laufenden Zeile.
        var currentItemId = _selectedQueue?.CurrentItem?.QueueItemId;
        var currentIndex = _selectedQueue?.Index;
        try
        {
            var client = await MaManager.Instance.GetClientAsync();
            var queueId = _selectedPlayer.ActiveQueueId ?? _selectedPlayer.PlayerId;
            var items = await client.GetQueueItemsAsync(queueId, 200);
            Dispatcher.UIThread.Post(() =>
            {
                _queuePanel.Children.Clear();
                _queuePanel.Children.Add(new TextBlock
                {
                    Text = "Warteschlange",
                    FontWeight = FontWeight.Bold,
                    Foreground = Brushes.White,
                    FontSize = 14,
                    Margin = new Thickness(4, 6, 0, 8)
                });
                var idx = 0;
                foreach (var it in items)
                {
                    var row = new Button
                    {
                        Tag = idx,
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        HorizontalContentAlignment = HorizontalAlignment.Left,
                        Background = Brushes.Transparent,
                        Foreground = Brushes.White,
                        BorderThickness = new Thickness(0),
                        Padding = new Thickness(8, 5),
                        CornerRadius = new CornerRadius(5)
                    };
                    var artist = it.Artists != null && it.Artists.Count > 0 ? " – " + string.Join(", ", it.Artists.Select(x => x.Name)) : "";
                    var isCurrent = (!string.IsNullOrEmpty(currentItemId) && it.QueueItemId == currentItemId)
                        || (currentIndex != null && idx == currentIndex);
                    var rowText = new TextBlock { Text = $"{idx + 1}. {it.Name}{artist}", TextWrapping = TextWrapping.Wrap, FontSize = 12 };
                    if (isCurrent)
                    {
                        rowText.FontWeight = FontWeight.Bold;
                        rowText.Foreground = new SolidColorBrush(Color.Parse("#4FC3F7"));
                    }
                    row.Content = rowText;
                    var capturedIdx = idx;
                    row.Click += (s, e) => _ = DoQueueCommand(c => c.PlayIndexAsync(queueId, capturedIdx));
                    _queuePanel.Children.Add(row);
                    idx++;
                }
                if (items.Count == 0)
                {
                    _queuePanel.Children.Add(new TextBlock { Text = "Leer", Foreground = new SolidColorBrush(Color.Parse("#8C8CA0")), Margin = new Thickness(8) });
                }
            });
        }
        catch (Exception ex)
        {
            Dispatcher.UIThread.Post(() =>
            {
                _queuePanel.Children.Clear();
                _queuePanel.Children.Add(new TextBlock { Text = $"Fehler: {ex.Message}", Foreground = new SolidColorBrush(Color.Parse("#E74C3C")), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8) });
            });
        }
    }

    private async Task OnSearch(string query)
    {
        try
        {
            var client = await MaManager.Instance.GetClientAsync();
            var result = await client.SearchAsync(query, 15);
            Dispatcher.UIThread.Post(() =>
            {
                _resultsPanel.Children.Clear();
                AddSectionToResults("Tracks", result.Tracks, "track");
                AddSectionToResults("Alben", result.Albums, "album");
                AddSectionToResults("Artists", result.Artists, "artist");
                AddSectionToResults("Playlists", result.Playlists, "playlist");
                AddSectionToResults("Radio", result.Radio, "radio");
                if (result.TotalHits == 0)
                    _resultsPanel.Children.Add(new TextBlock { Text = "Keine Treffer", Foreground = new SolidColorBrush(Color.Parse("#8C8CA0")), Margin = new Thickness(8) });
            });
        }
        catch (Exception ex)
        {
            Dispatcher.UIThread.Post(() =>
            {
                _resultsPanel.Children.Clear();
                _resultsPanel.Children.Add(new TextBlock { Text = $"Suchfehler: {ex.Message}", Foreground = new SolidColorBrush(Color.Parse("#E74C3C")), TextWrapping = TextWrapping.Wrap });
            });
        }
    }

    /// <summary>
    /// Lädt die Library-Favoriten (GetFavoritesAsync) und zeigt sie als
    /// 'Favoriten'-Sektion in den Ergebnissen an — beim Start und bei leerem Suchtext.
    /// </summary>
    private async Task LoadFavoritesAsync()
    {
        try
        {
            var client = await MaManager.Instance.GetClientAsync();
            var favorites = await client.GetFavoritesAsync(50);
            Dispatcher.UIThread.Post(() =>
            {
                _resultsPanel.Children.Clear();
                AddSectionToResults(Localization.Get("music_favorites"), favorites, "favorite");
                if (favorites.Count == 0)
                    _resultsPanel.Children.Add(new TextBlock { Text = "Keine Favoriten", Foreground = new SolidColorBrush(Color.Parse("#8C8CA0")), Margin = new Thickness(8) });
            });
        }
        catch (Exception ex)
        {
            Dispatcher.UIThread.Post(() =>
            {
                _resultsPanel.Children.Clear();
                _resultsPanel.Children.Add(new TextBlock { Text = $"Fehler: {ex.Message}", Foreground = new SolidColorBrush(Color.Parse("#E74C3C")), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8) });
            });
        }
    }

    private void AddSectionToResults(string header, List<MaMediaItem>? items, string type)
    {
        if (items == null || items.Count == 0) return;
        _resultsPanel.Children.Add(new TextBlock
        {
            Text = header,
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(Color.Parse("#8C8CA0")),
            FontSize = 12,
            Margin = new Thickness(4, 10, 0, 4)
        });
        foreach (var it in items)
        {
            var sub = (type == "track" || type == "favorite") && it.Artists is { Count: > 0 }
                ? " – " + string.Join(", ", it.Artists.Select(a => a.Name))
                : "";
            var btn = new Button
            {
                Content = new TextBlock { Text = $"{it.Name}{sub}", Foreground = Brushes.White, TextWrapping = TextWrapping.NoWrap, FontSize = 13 },
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(8, 6),
                CornerRadius = new CornerRadius(5),
                Tag = it
            };
            btn.Click += (s, e) => _ = PlayMediaItem(it);
            _resultsPanel.Children.Add(btn);
        }
    }

    private async Task PlayMediaItem(MaMediaItem item)
    {
        try
        {
            var client = await MaManager.Instance.GetClientAsync();
            var playerId = _selectedPlayer?.PlayerId ?? MaManager.Instance.Players.FirstOrDefault()?.PlayerId;
            if (playerId == null) return;
            var queueId = _selectedPlayer?.ActiveQueueId ?? playerId;
            var isRadio = item.MediaType == "radio";
            if (isRadio)
                await client.PlayMediaRadioAsync(queueId, item.Uri);
            else
                await client.PlayMediaAsync(queueId, item.Uri, "play");
            _statusText.Text = $"▶ {item.Name}";
        }
        catch (Exception ex)
        {
            _statusText.Text = $"❌ {ex.Message}";
        }
    }

    private void OnSearchClick(object? s, RoutedEventArgs e)
    {
        var q = _searchBox.Text?.Trim();
        if (string.IsNullOrEmpty(q)) _ = LoadFavoritesAsync();
        else _ = OnSearch(q);
    }

    private async Task DoQueueCommand(Func<MusicAssistantClient, Task> action)
    {
        try
        {
            var client = await MaManager.Instance.GetClientAsync();
            await action(client);
        }
        catch (Exception ex)
        {
            Dispatcher.UIThread.Post(() => _statusText.Text = $"❌ {ex.Message}");
        }
    }

    private string RequireQueueId()
    {
        var q = _selectedQueue?.QueueId;
        if (!string.IsNullOrEmpty(q)) return q;
        return _selectedPlayer?.ActiveQueueId ?? _selectedPlayer?.PlayerId ?? throw new MaException("Kein Player gewählt", 0);
    }

    private async Task ToggleShuffle()
    {
        try
        {
            var client = await MaManager.Instance.GetClientAsync();
            var queueId = RequireQueueId();
            var newState = !(_selectedQueue?.ShuffleEnabled ?? false);
            await client.SetShuffleAsync(queueId, newState);
        }
        catch (Exception ex) { _statusText.Text = $"❌ {ex.Message}"; }
    }

    private void OnVolumeChanged(object? s, RoutedEventArgs e)
    {
        if (_suppressVolumeEvent) return;
        var vol = (int)_volumeSlider.Value;
        _volumeLabel.Text = $"{vol}%";
        if (_selectedPlayer == null) return;
        var playerId = _selectedPlayer.PlayerId;
        _ = Task.Run(async () =>
        {
            try
            {
                var client = await MaManager.Instance.GetClientAsync();
                await client.SetVolumeAsync(playerId, vol);
            }
            catch { }
        });
    }

    private async Task SelectPlayerInWindowAsync(string playerId)
    {
        try
        {
            await MaManager.Instance.SelectPlayerAsync(playerId);
            _selectedPlayer = MaManager.Instance.SelectedPlayer;
            _selectedQueue = MaManager.Instance.SelectedQueue;
            RefreshState();
            SyncVolumeSlider();
            _ = LoadQueueItemsAsync();
        }
        catch (Exception ex)
        {
            _statusText.Text = $"❌ {ex.Message}";
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        MaManager.Instance.StateChanged -= OnStateChanged;
        base.OnClosed(e);
    }
}