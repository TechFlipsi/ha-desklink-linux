
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
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using AvaloniaWebView;
using HaDeskLink.Views;

namespace HaDeskLink;

public class App : Application
{
    public static Config? CurrentConfig { get; private set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void RegisterServices()
    {
        base.RegisterServices();
        AvaloniaWebViewBuilder.Initialize(default);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        CurrentConfig = Config.Load();

        // Sprache beim App-Start laden
        if (CurrentConfig != null)
            Localization.LoadLanguage(CurrentConfig.Language);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
            desktop.MainWindow.Title = $"HA DeskLink Linux v{HaApiClient.GetVersion()}";
            if (CurrentConfig != null)
            {
                ((MainWindow)desktop.MainWindow).HaUrl = CurrentConfig.HaUrl;
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}