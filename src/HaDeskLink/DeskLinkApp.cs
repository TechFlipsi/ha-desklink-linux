
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
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;

namespace HaDeskLink;

/// <summary>
/// Main application - headless daemon for Linux with optional Avalonia UI dashboard.
/// </summary>
public class DeskLinkApp : BackgroundService
{
    private readonly Config _config;
    private readonly HaApiClient _api;
    private SensorManager? _sensors;
    private HaWebSocketClient? _wsClient;
    private readonly Dictionary<string, object> _lastSensorStates = new();
    private MqttClient? _mqttClient;
    private MediaPlayer? _mediaPlayer;
    private System.Threading.Timer? _mediaTimer;

    public DeskLinkApp(Config config, HaApiClient api)
    {
        _config = config;
        _api = api;
        Localization.LoadLanguage(config.Language);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Clean up stale update pending marker
        try { File.Delete(Path.Combine(Config.GetConfigDir(), ".update_pending")); } catch { }

        Console.WriteLine($"[HA DeskLink] v{HaApiClient.GetVersion()} starting...");

        if (!_api.LoadRegistration())
        {
            Console.WriteLine("[HA DeskLink] No registration found. Run setup first: ha-desklink --setup");
            return;
        }

        try
        {
            _sensors = new SensorManager();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[HA DeskLink] Sensor init failed: {ex.Message}");
        }

        // Initial sensor registration
        if (_sensors != null)
        {
            try
            {
                // Check if the API client is blocked before trying
                if (_api.IsBlocked)
                {
                    Console.WriteLine("[HA DeskLink] Login gesperrt: Token ungültig. Bitte überprüfe deinen Home Assistant Token in den Einstellungen.");
                    Console.WriteLine("[HA DeskLink] Daemon bleibt inaktiv bis zur manuellen Korrektur des Tokens.");
                    // Wait indefinitely until token is fixed and daemon is restarted
                    while (!stoppingToken.IsCancellationRequested)
                    {
                        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                    }
                    return;
                }

                var initial = _sensors.CollectAll();
                foreach (var sensor in initial)
                {
                    try { await _api.RegisterSensorAsync(sensor); }
                    catch { }
                }
                await _api.UpdateSensorStatesAsync(initial);
                await _api.SendLocationAsync();
                await _api.UpdateRegistrationAsync();
                Console.WriteLine($"[HA DeskLink] Registered {initial.Count} sensors");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HA DeskLink] Initial registration failed: {ex.Message}");
                if (_api.IsBlocked)
                {
                    Console.WriteLine("[HA DeskLink] Login gesperrt: Token ungültig. Bitte überprüfe deinen Home Assistant Token in den Einstellungen.");
                    Console.WriteLine("[HA DeskLink] Daemon bleibt inaktiv bis zur manuellen Korrektur des Tokens.");
                    while (!stoppingToken.IsCancellationRequested)
                    {
                        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                    }
                    return;
                }
            }
        }

        // Start WebSocket for push notifications
        var webhookId = _api.GetWebhookId();

        if (string.IsNullOrEmpty(_config.HaToken))
        {
            Console.WriteLine("[HA DeskLink] FEHLER: Token konnte nicht geladen werden. Bitte App neu einrichten.");
            return;
        }

        _wsClient = new HaWebSocketClient(_config.HaUrl, _config.HaToken, webhookId,
            msg => Console.WriteLine($"[HA DeskLink] Notification: {msg}"),
            isBlocked: () => _api.IsBlocked,
            verifySsl: _config.VerifySsl);
        _ = _wsClient.ConnectAsync();

        // ── MQTT smart routing ──────────────────────────────────────
        if (_config.MqttEnabled && !string.IsNullOrEmpty(_config.MqttBroker) && _config.MqttPort > 0)
        {
            var configDir = Config.GetConfigDir();
            var mqttPassword = string.IsNullOrEmpty(_config.MqttPasswordEncrypted) ? _config.MqttPassword : _config.MqttPassword;
            _mqttClient = new MqttClient(_config.MqttBroker, _config.MqttPort,
                string.IsNullOrEmpty(_config.MqttUsername) ? null : _config.MqttUsername,
                string.IsNullOrEmpty(mqttPassword) ? null : mqttPassword,
                _config.MqttUseSsl, configDir, HaApiClient.GetVersion(),
                onCommandReceived: cmd =>
                {
                    try { CommandHandler.Execute(cmd); }
                    catch (Exception ex) { Console.WriteLine($"[MQTT Cmd] Error: {ex.Message}"); }
                });
            _ = MqttConnectAsync(stoppingToken);
        }

        // ── Media player state polling via MQTT ─────────────────────
        try
        {
            _mediaPlayer = new MediaPlayer();
            _mediaTimer = new System.Threading.Timer(async _ =>
            {
                try
                {
                    if (_mqttClient?.IsConnected == true)
                    {
                        var mediaState = _mediaPlayer.GetCurrentMediaState();
                        var attrs = System.Text.Json.JsonSerializer.Serialize(new
                        {
                            title = mediaState.Title,
                            artist = mediaState.Artist,
                            album = mediaState.Album,
                            source = mediaState.Source
                        });
                        await _mqttClient.PublishMediaStateAsync(mediaState.State, attrs);
                    }
                }
                catch { }
            }, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
        }
        catch { }

        // Check for updates
        _ = CheckForUpdatesAsync(stoppingToken);

        // Sensor loop
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_sensors != null)
                {
                    var allSensors = _sensors.CollectAll();
                    var changed = new List<SensorData>();
                    foreach (var s in allSensors)
                    {
                        var key = s.UniqueId;
                        if (!_lastSensorStates.TryGetValue(key, out var lastState) || !Equals(lastState, s.State))
                        {
                            changed.Add(s);
                            _lastSensorStates[key] = s.State;
                        }
                    }
                    if (changed.Count > 0)
                    {
                        // Always send via webhook (keeps mobile_app registration intact)
                        await _api.UpdateSensorStatesAsync(changed);

                        // Smart routing: also publish via MQTT if connected
                        if (_mqttClient?.IsConnected == true)
                        {
                            try { await _mqttClient.PublishSensorStatesAsync(changed); }
                            catch (Exception ex) { Console.WriteLine($"[MQTT Sensor] Publish error: {ex.Message}"); }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HA DeskLink] Sensor update error: {ex.Message}");
            }

            await Task.Delay(_config.SensorInterval * 1000, stoppingToken);
        }
    }

    private async Task CheckForUpdatesAsync(CancellationToken ct)
    {
        // Initial check
        try
        {
            var updateUrl = await _api.CheckForUpdateAsync(includePrerelease: _config.UpdateChannel == "prerelease");
            if (updateUrl != null)
            {
                Console.WriteLine($"[HA DeskLink] Update available: {updateUrl}");
                Console.WriteLine("[HA DeskLink] Run: ha-desklink --update to install");
            }
        }
        catch { }

        // Periodic check every 2 hours
        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(TimeSpan.FromHours(2), ct); }
            catch { break; }
            try
            {
                var updateUrl = await _api.CheckForUpdateAsync(includePrerelease: _config.UpdateChannel == "prerelease");
                if (updateUrl != null)
                {
                    Console.WriteLine($"[HA DeskLink] Update available: {updateUrl}");
                    Console.WriteLine("[HA DeskLink] Run: ha-desklink --update to install");
                }
            }
            catch { }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        Console.WriteLine("[HA DeskLink] Stopping...");
        _mediaTimer?.Dispose();

        // Send pc_status = "off" before shutting down
        try
        {
            var pcOff = new SensorData("pc_status", "PC Status", "off",
                deviceClass: "connectivity", icon: "mdi:desktop-classic")
            {
                SensorKind = SensorType.BinarySensor,
                EntityCategory = null
            };
            await _api.UpdateSensorStatesAsync(new List<SensorData> { pcOff });
        }
        catch { }

        // MQTT: publish pc_status OFF + disconnect
        try
        {
            if (_mqttClient?.IsConnected == true)
            {
                var pcOff = new SensorData("pc_status", "PC Status", "off",
                    deviceClass: "connectivity", icon: "mdi:desktop-classic")
                {
                    SensorKind = SensorType.BinarySensor
                };
                await _mqttClient.PublishSensorStateAsync(pcOff);
                await _mqttClient.DisconnectAsync();
                _mqttClient.Dispose();
            }
        }
        catch { }

        _wsClient?.Dispose();
        await base.StopAsync(cancellationToken);
    }

    // ── MQTT Smart Routing ────────────────────────────────────────

    /// <summary>
    /// Connect to MQTT, publish discovery on connect, and handle reconnect with state republish.
    /// </summary>
    private async Task MqttConnectAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (_mqttClient != null)
                {
                    await _mqttClient.ConnectAsync();

                    // Publish discovery for all sensors + media player on connect
                    if (_mqttClient.IsConnected && _sensors != null)
                    {
                        try
                        {
                            await _mqttClient.PublishDiscoveryAsync(_sensors.CollectAll());
                        }
                        catch (Exception ex) { Console.WriteLine($"[MQTT] Discovery error: {ex.Message}"); }

                        // Publish current states on connect
                        try
                        {
                            await _mqttClient.PublishSensorStatesAsync(_sensors.CollectAll());
                        }
                        catch (Exception ex) { Console.WriteLine($"[MQTT] State publish error: {ex.Message}"); }
                    }
                }

                await Task.Delay(TimeSpan.FromSeconds(5), ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                Console.WriteLine($"[MQTT] Connect loop error: {ex.Message}");
                try { await Task.Delay(TimeSpan.FromSeconds(30), ct); }
                catch { break; }
            }
        }
    }
}