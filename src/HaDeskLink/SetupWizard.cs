
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
using System;
using System.Threading.Tasks;

namespace HaDeskLink;

/// <summary>
/// Console-based setup wizard (for --setup without GUI).
/// </summary>
public class SetupWizard
{
    public string HaUrl { get; private set; } = "";
    public string HaToken { get; private set; } = "";
    public bool VerifySsl { get; private set; } = false;

    public async Task<bool> RunAsync()
    {
        Console.WriteLine("=== HA DeskLink Linux Setup ===\n");

        Console.Write("Home Assistant URL (z.B. https://homeassistant.local:8123): ");
        HaUrl = Console.ReadLine()?.Trim() ?? "";

        Console.Write("Long-Lived Access Token (HA → Profil → Sicherheit): ");
        HaToken = Console.ReadLine()?.Trim() ?? "";

        Console.Write("SSL-Zertifikat prüfen? (j/n, Standard: n): ");
        var ssl = Console.ReadLine()?.Trim().ToLowerInvariant();
        VerifySsl = ssl == "j" || ssl == "y" || ssl == "ja" || ssl == "yes";

        Console.WriteLine("\nVerbinde mit Home Assistant...");

        try
        {
            var configDir = Config.GetConfigDir();
            var api = new HaApiClient(configDir, VerifySsl);
            await api.RegisterAsync(HaUrl, HaToken);
            Console.WriteLine("✓ Verbindung erfolgreich!");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Verbindung fehlgeschlagen: {ex.Message}");
            return false;
        }
    }
}