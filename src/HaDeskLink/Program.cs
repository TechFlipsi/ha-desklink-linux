#nullable enable
using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace HaDeskLink;

class Program
{
    public static async Task<int> Main(string[] args)
    {
        // CLI argument handling
        if (args.Length > 0)
        {
            switch (args[0].ToLowerInvariant())
            {
                case "--daemon":
                case "-d":
                    return await RunDaemonAsync();

                case "--setup":
                case "-s":
                    return await RunSetupAsync();

                case "--reset-device":
                    var configDir = Config.GetConfigDir();
                    var api = new HaApiClient(configDir);
                    api.ResetDeviceId();
                    Console.WriteLine("Neue Geräte-ID erstellt. Starte ha-desklink --daemon neu.");
                    return 0;

                case "--update":
                case "-u":
                    return await RunUpdateAsync();

                case "--version":
                case "-v":
                    Console.WriteLine($"HA DeskLink Linux v{HaApiClient.GetVersion()}");
                    return 0;

                case "--help":
                case "-h":
                    ShowHelp();
                    return 0;

                default:
                    Console.WriteLine($"Unbekannter Befehl: {args[0]}");
                    ShowHelp();
                    return 1;
            }
        }

        // Default: start with GUI
        return RunWithGui();
    }

    private static int RunWithGui()
    {
        var configDir = Config.GetConfigDir();
        if (!File.Exists(Path.Combine(configDir, "registration.json")))
        {
            Console.WriteLine("Keine Verbindung gefunden. Führe zuerst das Setup aus: ha-desklink --setup");
            return 1;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(Array.Empty<string>());
        return 0;
    }

    private static async Task<int> RunDaemonAsync()
    {
        var configDir = Config.GetConfigDir();
        if (!File.Exists(Path.Combine(configDir, "registration.json")))
        {
            Console.WriteLine("Keine Verbindung gefunden. Führe zuerst das Setup aus: ha-desklink --setup");
            return 1;
        }

        var appConfig = Config.Load();
        var haApi = new HaApiClient(configDir, appConfig.VerifySsl);
        haApi.LoadRegistration();

        var builder = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(appConfig);
                services.AddSingleton(haApi);
                services.AddHostedService<DeskLinkApp>();
            });

        var host = builder.Build();
        await host.RunAsync();
        return 0;
    }

    private static async Task<int> RunSetupAsync()
    {
        var wizard = new SetupWizard();
        if (await wizard.RunAsync())
        {
            var config = Config.Load();
            config.HaUrl = wizard.HaUrl;
            config.HaToken = wizard.HaToken;
            config.VerifySsl = wizard.VerifySsl;
            config.Save();
            Console.WriteLine("Setup abgeschlossen! Starte mit: ha-desklink (GUI) oder ha-desklink --daemon");
            return 0;
        }
        return 1;
    }

    private static async Task<int> RunUpdateAsync()
    {
        try
        {
            var config = Config.Load();
            var api = new HaApiClient(Config.GetConfigDir(), config.VerifySsl);
            var updateUrl = await api.CheckForUpdateAsync(includePrerelease: config.UpdateChannel == "prerelease");
            if (updateUrl == null)
            {
                Console.WriteLine("HA DeskLink ist auf dem neuesten Stand.");
                return 0;
            }

            Console.WriteLine($"Update gefunden! Downloade von {updateUrl}...");
            using var client = new System.Net.Http.HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", "HA-DeskLink-Linux");

            var tempDir = Path.Combine(Path.GetTempPath(), "HA_DeskLink_Update");
            Directory.CreateDirectory(tempDir);
            var archivePath = Path.Combine(tempDir, "ha-desklink-linux.tar.gz");

            var bytes = await client.GetByteArrayAsync(updateUrl);
            await File.WriteAllBytesAsync(archivePath, bytes);

            Console.WriteLine("Download abgeschlossen. Bitte entpacken und ersetzen.");
            Console.WriteLine($"Archiv: {archivePath}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Update fehlgeschlagen: {ex.Message}");
            return 1;
        }
    }

    private static void ShowHelp()
    {
        Console.WriteLine($@"HA DeskLink Linux v{HaApiClient.GetVersion()} – Home Assistant Companion

Usage: ha-desklink [OPTION]

Options:
  (kein Argument)   Startet mit grafischer Oberfläche
  --daemon, -d       Startet als Hintergrund-Daemon (ohne GUI)
  --setup, -s        Einrichtung (HA URL + Token eingeben)
  --reset-device     Neue Geräte-ID erstellen
  --update, -u       Nach Update suchen
  --version, -v      Version anzeigen
  --help, -h         Diese Hilfe anzeigen

Systemd Service einrichten:
  sudo cp ha-desklink.service /etc/systemd/system/
  sudo systemctl daemon-reload
  sudo systemctl enable --now ha-desklink

Config: {Config.GetConfigDir()}");
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}