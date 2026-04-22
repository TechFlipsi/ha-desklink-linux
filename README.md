# HA DeskLink Linux v3.0

[![Build](https://img.shields.io/github/actions/workflow/status/TechFlipsi/ha-desklink-linux/build.yml?branch=main&label=Build)](https://github.com/TechFlipsi/ha-desklink-linux/actions)
[![Version](https://img.shields.io/github/v/release/TechFlipsi/ha-desklink-linux?label=Version)](https://github.com/TechFlipsi/ha-desklink-linux/releases/latest)
[![License](https://img.shields.io/github/license/TechFlipsi/ha-desklink-linux?label=License)](https://github.com/TechFlipsi/ha-desklink-linux/LICENSE)
[![Downloads](https://img.shields.io/github/downloads/TechFlipsi/ha-desklink-linux/total?label=Downloads)](https://github.com/TechFlipsi/ha-desklink-linux/releases)
[![Discord](https://img.shields.io/discord/1496261911677894867?label=Discord)](https://discord.gg/HnCZY54U7)

⚠️ **PRE-RELEASE / BETA** – Diese Version wurde noch nicht auf einem echten Linux-System getestet. Verwendung auf eigene Gefahr. Feedback und Bug-Reports sind willkommen!

**Linux Companion App für Home Assistant** – headless, nativ, zuverlässig.

📖 **[Betriebsanleitung / Manual](MANUAL.md)** – Installation, Sensoren, Befehle, Quick Actions, Actionable Notifications, Screenshot, Webcam, Plattform-Vergleich & mehr (DE + EN)

📊 **[HASS.Agent vs. HA DeskLink](COMPARISON.md)** – Features, Architektur & Migration im Vergleich (DE + EN)

Geschrieben in **C# / .NET 8**, nutzt `/sys`, `/proc` und Linux-Tools für Hardware-Sensoren.

## Features
- 🌡️ **CPU & GPU Temperatur** – via `/sys/class/thermal` und `hwmon`
- 📊 **Alle Sensoren** – CPU, RAM, alle Laufwerke, Akku, Uptime, Netzwerk
- 🖥️ **PC-Befehle aus HA** – Shutdown, Restart, Hibernate, Suspend, Lock, Lautstärke
- 🖥️ **Grafische Oberfläche** – Avalonia UI Dashboard mit Status, Sensoren & Einrichtung
- 📬 **Push-Benachrichtigungen** – WebSocket-basiert, wie die Handy-App
- 🔔 **Actionable Notifications** – Benachrichtigungen mit Aktions-Buttons
- ⚡ **Quick Actions** – Dashboard-Button für HA-Entity-Toggles
- 📸 **Screenshot** – Bildschirmfoto speichern + als HA-Event hochladen
- 📷 **Webcam-Sensor** – Zeigt ob Webcam aktiv ist (on/off)
- 🔌 **mobile_app Protokoll** – identisch zur Windows-App, keine Extra-Konfiguration in HA nötig
- 🔄 **Auto-Update** – Alle 2 Stunden wird nach Updates gesucht
- 🐧 **Headless Daemon** – läuft als systemd-Service im Hintergrund
- 🛡️ **Downgrade-Schutz** – nur Upgrades, keine älteren Versionen

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
| PC sperren | `lock` | Sperrt den Bildschirm |
| Lautstärke stumm | `mute` | Schaltet den Ton stumm |
| Lautstärke lauter | `volume_up` | Erhöht die Lautstärke um 10% |
| Lautstärke leiser | `volume_down` | Verringert die Lautstärke um 10% |
| Helligkeit rauf | `brightness_up` | Erhöht die Bildschirmhelligkeit um 10% |
| Helligkeit runter | `brightness_down` | Verringert die Bildschirmhelligkeit um 10% |
| Helligkeit setzen | `brightness:50` | Setzt Helligkeit auf bestimmten Wert (0-100) |
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
💬 [Discord](https://discord.gg/HnCZY54U7) – Fragen, Feedback, Hilfe

## Erstellung
Dieses Projekt wurde unter Verwendung von KI-Unterstützung erstellt. Der gesamte Code wurde von **GLM-5.1** (via OpenClaw) geschrieben und entwickelt – von der Architektur über die Implementierung bis zum Debugging. Die englische Dokumentation wurde ebenfalls von der KI aus dem Deutschen ins Englische übersetzt. Die deutsche Dokumentation ist die Originalversion.