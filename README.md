# HA DeskLink Linux

**Linux Companion App für Home Assistant** – headless, nativ, zuverlässig.

Geschrieben in **C# / .NET 8**, nutzt `/sys`, `/proc` und Linux-Tools für Hardware-Sensoren.

## Features
- 🌡️ **CPU & GPU Temperatur** – via `/sys/class/thermal` und `hwmon`
- 📊 **Alle Sensoren** – CPU, RAM, alle Laufwerke, Akku, Uptime, Netzwerk
- 🖥️ **PC-Befehle aus HA** – Shutdown, Restart, Hibernate, Suspend, Lock, Lautstärke
- 📬 **Push-Benachrichtigungen** – WebSocket-basiert, wie die Handy-App
- 🔌 **mobile_app Protokoll** – identisch zur Windows-App, keine Extra-Konfiguration in HA nötig
- 🔄 **Auto-Update** – Alle 2 Stunden wird nach Updates gesucht
- 🐧 **Headless Daemon** – läuft als systemd-Service im Hintergrund
- 🛡️ **Downgrade-Schutz** – nur Upgrades, keine älteren Versionen

## Systemanforderungen
- Linux (x64, ARM64)
- .NET 8 Runtime (oder self-contained build)
- Für Sensoren: `lm-sensors` empfohlen (`sudo apt install lm-sensors`)

## Installation
1. Neueste Release von [Releases](https://github.com/TechFlipsi/ha-desklink-linux/releases/latest) herunterladen
2. Entpacken: `tar xzf ha-desklink-linux.tar.gz`
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
| `ha-desklink` | Als Daemon starten |
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
| Monitor an | `monitor_on` | Schaltet den Monitor an |
| Monitor aus | `monitor_off` | Schaltet den Monitor aus |

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

> 💡 Weitere Laufwerke werden automatisch erkannt. hwmon-Sensoren (GPU-Temp etc.) erscheinen automatisch wenn verfügbar.

## Build
```bash
dotnet publish src/HaDeskLink -c Release -r linux-x64 --self-contained -o publish
```

Für ARM64 (Raspberry Pi etc.):
```bash
dotnet publish src/HaDeskLink -c Release -r linux-arm64 --self-contained -o publish
```

## Lizenz
MIT License – Copyright © 2026 Fabian Kirchweger

## Erstellung
Dieses Projekt wurde unter Verwendung von KI-Unterstützung erstellt. Als Sprachmodell kam **GLM-5.1** (via OpenClaw) zum Einsatz – für Codegenerierung, Debugging und Dokumentation.