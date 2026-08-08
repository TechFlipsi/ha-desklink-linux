# HA DeskLink Linux v4.3

[![Build](https://img.shields.io/github/actions/workflow/status/TechFlipsi/ha-desklink-linux/build.yml?branch=main&label=Build)](https://github.com/TechFlipsi/ha-desklink-linux/actions)
[![Version](https://img.shields.io/github/v/release/TechFlipsi/ha-desklink-linux?label=Version)](https://github.com/TechFlipsi/ha-desklink-linux/releases/latest)
[![License](https://img.shields.io/github/license/TechFlipsi/ha-desklink-linux?label=License)](https://github.com/TechFlipsi/ha-desklink-linux/LICENSE)
[![Downloads](https://img.shields.io/github/downloads/TechFlipsi/ha-desklink-linux/total?label=Downloads)](https://github.com/TechFlipsi/ha-desklink-linux/releases)
[![Discord](https://img.shields.io/discord/1496261911677894867?label=Discord)](https://discord.com/invite/zHPhQ7EaqH)

⚠️ **PRE-RELEASE / BETA** – Diese Version wurde noch nicht auf einem echten Linux-System getestet. Verwendung auf eigene Gefahr. Feedback und Bug-Reports sind willkommen!

**Linux Companion App für Home Assistant** – headless, nativ, zuverlässig.

> 🔍 **Suchst du einen Home Assistant Desktop-Companion für Linux?** HA DeskLink verbindet deinen Linux-PC direkt mit Home Assistant – Sensordaten, Systemstatus und Steuerelemente live. Kein Browser nötig.

<!-- SEO: home assistant linux desktop app, home assistant linux companion, hass linux, home assistant sensor monitor linux, smart home linux widget -->

📖 **[Betriebsanleitung / Manual](MANUAL.md)** – Installation, Sensoren, Befehle, Quick Actions, Actionable Notifications, Screenshot, Webcam, Plattform-Vergleich & mehr (DE + EN)

📊 **[HASS.Agent vs. HA DeskLink](COMPARISON.md)** – Features, Architektur & Migration im Vergleich (DE + EN)

Geschrieben in **C# / .NET 8**, nutzt `/sys`, `/proc` und Linux-Tools für Hardware-Sensoren.

## Features
- 🌡️ **CPU & GPU Temperatur** – via `/sys/class/thermal` und `hwmon`
- 📊 **Alle Sensoren** – CPU, GPU, RAM, alle Laufwerke, VRAM, Akku, Uptime, Netzwerk, Audio, Mikrofon, Webcam, Idle-Zeit, Aktives Fenster
- 🖥️ **PC-Befehle aus HA** – Shutdown, Restart, Hibernate, Suspend, Sleep, Lock, Lautstärke, Mediensteuerung
- 🖥️ **Eingebettetes Dashboard** – WebView.Avalonia zeigt HA-Dashboard direkt in der App (einmaliges Login, Session bleibt erhalten)
- 🖥️ **Grafische Oberfläche** – Avalonia UI Dashboard mit Status, Sensoren & Einrichtung
- 📬 **Push-Benachrichtigungen** – WebSocket-basiert, wie die Handy-App
- 🔔 **Actionable Notifications** – Benachrichtigungen mit Aktions-Buttons
- ⚡ **Quick Actions** – Dashboard-Button für HA-Entity-Toggles
- 📸 **Screenshot** – Bildschirmfoto speichern + als HA-Event hochladen
- 📷 **Webcam-Sensor** – Zeigt ob Webcam aktiv ist (on/off)
- 🔔 **Actionable Notifications** – Benachrichtigungen mit Aktions-Buttons
- 📸 **Screenshot** – Bildschirmfoto speichern + als HA-Event hochladen
- 🔋 **Helligkeits-Befehle** – brightness_up/down/:N (Laptops)
- 🔌 **mobile_app Protokoll** – identisch zur Windows-App, keine Extra-Konfiguration in HA nötig
- 🔄 **Auto-Update** – Alle 2 Stunden wird nach Updates gesucht
- 🐧 **Headless Daemon** – läuft als systemd-Service im Hintergrund
- 🛡️ **Downgrade-Schutz** – nur Upgrades, keine älteren Versionen

## MQTT (v4.3)

HA DeskLink v4.3 bringt **optionale MQTT-Unterstützung** für erweiterte Features:

- 🔊 **Media Player Entity** – Dein PC erscheint als Media Player in Home Assistant mit now-playing Info, Play/Pause und Lautstärke-Regelung
- 📡 **PC Status Binary Sensor** – Sofortige Online/Offline-Erkennung via Last Will Testament (LWT)
- ⚡ **Befehle an schlafenden PC** – MQTT-Befehle erreichen den PC auch im Energiesparmodus
- 🔍 **Automatische Geräteerkennung** – Media Player und PC Status erscheinen automatisch in HA
- 🔒 **Zuverlässigere Verbindung** – Auto-Reconnect mit exponentiellem Backoff
- 🪄 **Zero-Config Setup** – Beim ersten Start wird automatisch nach Mosquitto gesucht und die Verbindung eingerichtet
- 🧭 **Smart Routing** – MQTT für Sensoren + Befehle, WebSocket bleibt für Benachrichtigungen

MQTT ist **optional** – HA DeskLink funktioniert auch ohne MQTT wie gewohnt weiter.

## Systemanforderungen
- Linux (x64, ARM64)
- .NET 8 Runtime (oder self-contained build)
- Für Sensoren: `lm-sensors` empfohlen (`sudo apt install lm-sensors`)

## Installation
1. Neueste `ha-desklink-linux-x64.tar.gz` von [Releases](https://github.com/TechFlipsi/ha-desklink-linux/releases/latest) herunterladen
2. Entpacken: `tar xzf ha-desklink-linux-x64.tar.gz`
3. Setup ausführen: `./ha-desklink --setup`
4. Als Service installieren:
   ```bash
   sudo cp ha-desklink /usr/local/bin/
   sudo cp ha-desklink.service /etc/systemd/system/
   sudo systemctl daemon-reload
   sudo systemctl enable --now ha-desklink
   ```

## CLI-Befehle
| Befehl | Beschreibung |
|---|---|
| `ha-desklink` | Mit grafischer Oberfläche starten |
| `ha-desklink --daemon` | Als Hintergrund-Daemon starten (ohne GUI) |
| `ha-desklink --setup` | Einrichtung (HA URL + Token) |
| `ha-desklink --reset-device` | Neue Geräte-ID erstellen |
| `ha-desklink --update` | Nach Update suchen |
| `ha-desklink --version` | Version anzeigen |
| `ha-desklink --help` | Hilfe anzeigen |

## PC-Befehle aus Home Assistant

| Befehl | Schreibweise | Wirkung |
|---|---|---|
| Herunterfahren | `shutdown` | Fährt den PC herunter |
| Neustarten | `restart` | Startet den PC neu |
| Ruhezustand | `hibernate` | Versetzt in den Ruhezustand |
| Bereitschaft | `suspend` | Versetzt in den Bereitschaftsmodus |
| Energie sparen | `sleep` | Versetzt in den Energiesparmodus |
| PC sperren | `lock_screen` | Sperrt den Bildschirm |
| Lautstärke stumm | `volume_mute` | Schaltet den Ton stumm |
| Lautstärke lauter | `volume_up` | Erhöht die Lautstärke um 10% |
| Lautstärke leiser | `volume_down` | Verringert die Lautstärke um 10% |
| Media Play/Pause | `media_play_pause` | Play/Pause für Medienwiedergabe |
| Media Nächster | `media_next` | Nächster Titel |
| Media Vorheriger | `media_previous` | Vorheriger Titel |
| Helligkeit rauf | `brightness_up` | Erhöht die Bildschirmhelligkeit um 10% (⚠️ nur Laptops/int. Monitore) |
| Helligkeit runter | `brightness_down` | Verringert die Bildschirmhelligkeit um 10% (⚠️ nur Laptops/int. Monitore) |
| Helligkeit setzen | `brightness:50` | Setzt Helligkeit auf bestimmten Wert (0-100, ⚠️ nur Laptops/int. Monitore) |
| Monitor an | `monitor_on` | Schaltet den Monitor an |
| Monitor aus | `monitor_off` | Schaltet den Monitor aus |
| Bildschirmfoto | `screenshot` | Macht einen Screenshot und lädt ihn zu HA hoch |
| Bildschirmfoto speichern | `screenshot_save` | Speichert Screenshot lokal und lädt zu HA hoch |

## Sensoren in Home Assistant

| Sensor | Beschreibung |
|---|---|
| `sensor.ha_desklink_cpu_usage` | CPU-Auslastung in % |
| `sensor.ha_desklink_cpu_temperature` | CPU-Temperatur in °C |
| `sensor.ha_desklink_cpu_clock` | CPU-Taktrate in MHz |
| `sensor.ha_desklink_gpu_load` | GPU-Auslastung in % |
| `sensor.ha_desklink_gpu_memory_used` | GPU VRAM verwendet in MB |
| `sensor.ha_desklink_gpu_memory_total` | GPU VRAM gesamt in MB |
| `sensor.ha_desklink_memory_usage` | RAM-Auslastung in % |
| `sensor.ha_desklink_memory_used` | RAM verwendet in GB |
| `sensor.ha_desklink_memory_free` | RAM frei in GB |
| `sensor.ha_desklink_memory_total` | RAM gesamt in GB |
| `sensor.ha_desklink_disk_*_usage` | Laufwerk-Auslastung in % |
| `sensor.ha_desklink_disk_*_free` | Laufwerk frei in GB |
| `sensor.ha_desklink_uptime` | Laufzeit in Stunden |
| `sensor.ha_desklink_battery` | Akkustand in % (Laptops) |
| `sensor.ha_desklink_ip_address` | Aktuelle IPv4-Adresse |
| `binary_sensor.ha_desklink_connectivity` | Online/Offline-Status |
| `sensor.ha_desklink_process_count` | Anzahl laufende Prozesse |
| `sensor.ha_desklink_wifi_ssid` | Verbundenes WiFi-Netzwerk |
| `sensor.ha_desklink_network_upload` | Upload-Geschwindigkeit in KB/s |
| `sensor.ha_desklink_network_download` | Download-Geschwindigkeit in KB/s |
| `sensor.ha_desklink_audio_volume` | System-Lautstärke in % |
| `binary_sensor.ha_desklink_audio_mute` | Stummschaltung (on/off) |
| `binary_sensor.ha_desklink_mic_active` | Mikrofon in Benutzung (on/off) |
| `sensor.ha_desklink_idle_time` | Sekunden seit letzter Benutzereingabe |
| `sensor.ha_desklink_active_window` | Aktives Fenster (Vordergrund-App) |
| `sensor.ha_desklink_fan_*` | Lüfter-Drehzahlen in RPM |
| `sensor.ha_desklink_webcam_active` | Webcam aktiv (on/off) |
| `sensor.ha_desklink_fullscreen` | Vollbild-Modus (on/off) |
| `sensor.ha_desklink_brightness` | Bildschirmhelligkeit in % |
| `sensor.ha_desklink_version` | Aktuelle HA DeskLink Version |

> 💡 Weitere Laufwerke werden automatisch erkannt. hwmon-Sensoren (GPU-Temp etc.) erscheinen automatisch wenn verfügbar.

## Build
```bash
dotnet publish src/HaDeskLink -c Release -r linux-x64 --self-contained -o publish
```

Für ARM64 (Raspberry Pi etc.):
```bash
dotnet publish src/HaDeskLink -c Release -r linux-arm64 --self-contained -o publish
```

## 📐 Versionierung
Ab v2.2.1 gelten **plattformunabhängige Versionsnummern**:

| Änderung | Beispiel | Erklärung |
|---|---|---|
| **Bug Fix** | 2.2.1 → 2.2.2 | Fehlerbehebung, nur betroffene Plattform |
| **Neue Funktionen** | 2.2.x → 3.0.0 | Neue Features, alle Plattformen gleichzeitig |

Jede Plattform (Windows, Linux, macOS) hat **eigene Versionsnummern**. Ein Bug-Fix unter Linux ändert nicht die Windows-Version – und umgekehrt. Große Funktionsupdates (Major) bekommen alle Plattformen gleichzeitig.

## Lizenz
GPL v3 – Copyright © 2026 Fabian Kirchweger

This program is free software: you can redistribute it and/or modify it under the terms of the GNU General Public License v3.

**Important:** If you modify or distribute this software, you MUST release your changes under the same GPL v3 license. Closed-source or proprietary use is NOT permitted. – Copyright © 2026 Fabian Kirchweger

## macOS-Version
Es gibt jetzt eine macOS-Version von HA DeskLink! 🎉 Siehe [ha-desklink-mac](https://github.com/TechFlipsi/ha-desklink-mac) – ⚠️ Community Test Version, nicht vom Entwickler getestet.

## Community
💬 [Discord](https://discord.com/invite/zHPhQ7EaqH) – Fragen, Feedback, Hilfe

## Erstellung
Dieses Projekt wurde unter Verwendung von KI-Unterstützung erstellt. Die Entwicklung erfolgte durch **J.A.R.V.I.S. (Hermes Agent)**. Als Hauptmodell kam **GLM-5.1** zum Einsatz (Architektur, Code, Debugging); **DeepSeek V4 Pro** wurde für Sub-Agenten-Aufgaben wie Tests und Audits verwendet. Die englische Dokumentation wurde ebenfalls von der KI aus dem Deutschen ins Englische übersetzt. Die deutsche Dokumentation ist die Originalversion. Details siehe [CREDITS.md](CREDITS.md).