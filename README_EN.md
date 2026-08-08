# HA DeskLink Linux v4.3

[![Build](https://img.shields.io/github/actions/workflow/status/TechFlipsi/ha-desklink-linux/build.yml?branch=main&label=Build)](https://github.com/TechFlipsi/ha-desklink-linux/actions)
[![Version](https://img.shields.io/github/v/release/TechFlipsi/ha-desklink-linux?label=Version)](https://github.com/TechFlipsi/ha-desklink-linux/releases/latest)
[![License](https://img.shields.io/github/license/TechFlipsi/ha-desklink-linux?label=License)](https://github.com/TechFlipsi/ha-desklink-linux/LICENSE)
[![Downloads](https://img.shields.io/github/downloads/TechFlipsi/ha-desklink-linux/total?label=Downloads)](https://github.com/TechFlipsi/ha-desklink-linux/releases)
[![Discord](https://img.shields.io/discord/1496261911677894867?label=Discord)](https://discord.com/invite/zHPhQ7EaqH)

⚠️ **PRE-RELEASE / BETA** – This version has not been tested on a real Linux system yet. Use at your own risk!

**Linux Companion App for Home Assistant** – headless, native, reliable.

📖 **[Manual](MANUAL.md)** – Installation, Sensors, Commands, Quick Actions, Actionable Notifications, Screenshot, Webcam & more (DE + EN)

📊 **[HASS.Agent vs. HA DeskLink](COMPARISON.md)** – Feature comparison (DE + EN)

Written in **C# / .NET 8**, uses `/sys`, `/proc`, and Linux tools for hardware sensors.

## Features
- 🌡️ **CPU & GPU Temperature** – via `/sys/class/thermal` and `hwmon`
- 📊 **All Sensors** – CPU, GPU, RAM, all drives, VRAM, Battery, Uptime, Network, Audio, Microphone, Webcam, Idle Time, Active Window
- 🖥️ **PC Commands from HA** – Shutdown, Restart, Hibernate, Suspend, Sleep, Lock, Volume, Media Control, Brightness
- 🖥️ **Embedded Dashboard** – WebView.Avalonia shows HA dashboard in-app (login once, session persists)
- 🖥️ **Graphical UI** – Avalonia UI dashboard with status, sensors & setup
- 📬 **Push Notifications** – WebSocket-based, like the mobile app
- 🔔 **Actionable Notifications** – Notifications with action buttons
- ⚡ **Quick Actions** – Dashboard button for HA entity toggles
- 📸 **Screenshot** – Screenshot save + upload as HA event
- 📷 **Webcam Sensor** – Shows if webcam is active (on/off)
- 🔌 **mobile_app Protocol** – identical to the Windows app, no extra HA configuration needed
- 🔄 **Auto-Update** – checks for updates every 2 hours
- 🐧 **Headless Daemon** – runs as systemd service in the background
- 🛡️ **Downgrade Protection** – only upgrades, no older versions

## MQTT (v4.3)

HA DeskLink v4.3 brings **optional MQTT support** for advanced features:

- 🔊 **Media Player Entity** – Your PC appears as a Media Player in Home Assistant with now-playing info, play/pause and volume control
- 📡 **PC Status Binary Sensor** – Instant online/offline detection via Last Will Testament (LWT)
- ⚡ **Commands to Sleeping PC** – MQTT commands reach the PC even in sleep mode
- 🔍 **Automatic Device Discovery** – Media Player and PC Status appear automatically in HA
- 🔒 **More Reliable Connection** – Auto-reconnect with exponential backoff
- 🪄 **Zero-Config Setup** – On first launch, automatically detects Mosquitto and configures the connection
- 🧭 **Smart Routing** – MQTT for sensors + commands, WebSocket stays for notifications

MQTT is **optional** – HA DeskLink works without MQTT as usual.

## System Requirements
- Linux (x64, ARM64)
- .NET 8 Runtime (or self-contained build)
- For sensors: `lm-sensors` recommended (`sudo apt install lm-sensors`)

## Installation
1. Download the latest `ha-desklink-linux-x64.tar.gz` from [Releases](https://github.com/TechFlipsi/ha-desklink-linux/releases/latest)
2. Extract: `tar xzf ha-desklink-linux-x64.tar.gz`
3. Run setup: `./ha-desklink --setup`
4. Install as service:
   ```bash
   sudo cp ha-desklink /usr/local/bin/
   sudo cp ha-desklink.service /etc/systemd/system/
   sudo systemctl daemon-reload
   sudo systemctl enable --now ha-desklink
   ```

**For ARM64 (Raspberry Pi etc.):** Use `ha-desklink-linux-arm64.tar.gz`.

## CLI Commands
| Command | Description |
|---|---|
| `ha-desklink` | Start with graphical interface |
| `ha-desklink --daemon` | Start as background daemon (no GUI) |
| `ha-desklink --setup` | Setup (enter HA URL + token) |
| `ha-desklink --reset-device` | Generate new device ID |
| `ha-desklink --update` | Check for updates |
| `ha-desklink --version` | Show version |
| `ha-desklink --help` | Show help |

## PC Commands from Home Assistant

| Command | Value | Effect |
|---|---|---|
| Shutdown | `shutdown` | Shuts down the PC |
| Restart | `restart` | Restarts the PC |
| Hibernate | `hibernate` | Puts the PC into hibernation |
| Suspend | `suspend` | Puts the PC into sleep mode |
| Sleep | `sleep` | Puts the PC into sleep mode |
| Lock PC | `lock_screen` | Locks the screen |
| Mute | `volume_mute` | Mutes the audio |
| Volume Up | `volume_up` | Increases volume by 10% |
| Volume Down | `volume_down` | Decreases volume by 10% |
| Media Play/Pause | `media_play_pause` | Play/Pause media playback |
| Media Next | `media_next` | Next track |
| Media Previous | `media_previous` | Previous track |
| Brightness Up | `brightness_up` | Increases brightness by 10% (⚠️ laptops only) |
| Brightness Down | `brightness_down` | Decreases brightness by 10% (⚠️ laptops only) |
| Brightness Set | `brightness:50` | Sets brightness to value 0-100 (⚠️ laptops only) |
| Monitor On | `monitor_on` | Turns the monitor on |
| Monitor Off | `monitor_off` | Turns the monitor off |
| Screenshot | `screenshot` | Takes a screenshot and uploads to HA |
| Screenshot Save | `screenshot_save` | Saves screenshot locally + uploads to HA |

> ⚠️ **Brightness commands** (`brightness_up`, `brightness_down`, `brightness:XX`) generally only work on **laptops** with built-in displays. Desktop PCs with external monitors will ignore the commands.

## Sensors in Home Assistant

| Sensor | Description |
|---|---|
| `sensor.ha_desklink_cpu_usage` | CPU usage in % |
| `sensor.ha_desklink_cpu_temperature` | CPU temperature in °C |
| `sensor.ha_desklink_cpu_clock` | CPU clock speed in MHz |
| `sensor.ha_desklink_gpu_load` | GPU usage in % |
| `sensor.ha_desklink_gpu_memory_used` | GPU VRAM used in MB |
| `sensor.ha_desklink_gpu_memory_total` | GPU VRAM total in MB |
| `sensor.ha_desklink_memory_usage` | RAM usage in % |
| `sensor.ha_desklink_memory_used` | RAM used in GB |
| `sensor.ha_desklink_memory_free` | RAM free in GB |
| `sensor.ha_desklink_memory_total` | RAM total in GB |
| `sensor.ha_desklink_disk_*_usage` | Drive usage in % |
| `sensor.ha_desklink_disk_*_free` | Drive free in GB |
| `sensor.ha_desklink_uptime` | Uptime in hours |
| `sensor.ha_desklink_battery` | Battery level in % (laptops) |
| `sensor.ha_desklink_ip_address` | Current IPv4 address |
| `sensor.ha_desklink_wifi_ssid` | Connected WiFi network |
| `binary_sensor.ha_desklink_connectivity` | Online/Offline status |
| `sensor.ha_desklink_process_count` | Number of running processes |
| `sensor.ha_desklink_network_upload` | Upload speed in KB/s |
| `sensor.ha_desklink_network_download` | Download speed in KB/s |
| `sensor.ha_desklink_audio_volume` | System volume in % |
| `binary_sensor.ha_desklink_audio_mute` | Mute status (on/off) |
| `binary_sensor.ha_desklink_mic_active` | Microphone in use (on/off) |
| `sensor.ha_desklink_idle_time` | Seconds since last user input |
| `sensor.ha_desklink_active_window` | Active window (foreground app) |
| `sensor.ha_desklink_fan_*` | Fan speeds in RPM |
| `sensor.ha_desklink_webcam_active` | Webcam active (on/off) |
| `sensor.ha_desklink_fullscreen` | Fullscreen mode (on/off) |
| `sensor.ha_desklink_brightness` | Screen brightness in % |
| `sensor.ha_desklink_version` | Current HA DeskLink version |

> 💡 Additional drives are detected automatically. hwmon sensors (GPU temp etc.) appear automatically if available.

## Build
```bash
dotnet publish src/HaDeskLink -c Release -r linux-x64 --self-contained -o publish
```

For ARM64:
```bash
dotnet publish src/HaDeskLink -c Release -r linux-arm64 --self-contained -o publish
```

## 📐 Versioning
Starting from v2.2.1, each platform has **independent version numbers**:

| Change | Example | Description |
|---|---|---|
| **Bug Fix** | 2.2.1 → 2.2.2 | Bug fix, affected platform only |
| **New Features** | 2.2.x → 3.0.0 | New features, all platforms simultaneously |

Each platform (Windows, Linux, macOS) has **its own version number**. A bug fix on Linux doesn't change the Windows version – and vice versa. Major feature updates bump all platforms at once.

## License
GPL v3 – Copyright © 2026 Fabian Kirchweger

This program is free software: you can redistribute it and/or modify it under the terms of the GNU General Public License v3.

**Important:** If you modify or distribute this software, you MUST release your changes under the same GPL v3 license. Closed-source or proprietary use is NOT permitted.

## Windows Version
See [ha-desklink-dotnet](https://github.com/TechFlipsi/ha-desklink-dotnet) for the Windows version.

## macOS Version
See [ha-desklink-mac](https://github.com/TechFlipsi/ha-desklink-mac) – ⚠️ Community Test Version.

## Community
💬 [Discord](https://discord.com/invite/zHPhQ7EaqH) – Questions, Feedback, Help

## Attribution
This project was created with AI assistance. All code was written and developed by **GLM-5.1** (via OpenClaw) – from architecture to implementation to debugging. The English documentation was translated from German by AI. The German documentation is the original version.