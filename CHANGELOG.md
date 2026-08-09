# Changelog

## [v5.0.0] - 2026-08-08

### Versionsangleichung mit Windows (v5.0.x)
- **Versionssprung:** v4.4.0 → v5.0.0 (Angleichung an Windows-Version v5.0.x)
- **Feature-Parität:** MQTT, Media Player, PC Status Binary Sensor und alle v4.4-Features sind jetzt Teil der v5.0.x Linie
- Alle drei Plattformen (Windows, Linux, macOS) nutzen nun die gleiche Major-Version v5.0.x

## [v4.4.0] - 2026-05-24

### MQTT Support (optional)
- MQTT support (optional, auto-configure from Home Assistant)
- Media Player entity (now playing info, play/pause/volume controls)
- PC Status binary sensor (instant online/offline detection)
- Zero-config MQTT setup wizard on first launch
- MQTT settings in main settings
- Smart routing: MQTT for sensors + commands, WebSocket for notifications
- Last Will Testament (instant offline detection)
- Auto-reconnect with exponential backoff

### 🌍 Neue Lokalisierungs-Keys
- 27 neue MQTT/MediaPlayer/PCStatus-Keys in allen 6 Sprachen

## [v4.2.0] - 2026-05-23

### 📊 Neue Sensoren
- **idle_time** – Sekunden seit letzter Benutzereingabe
- **active_window** – Aktives Fenster (Vordergrund-App)
- **audio_volume** – System-Lautstärke 0-100%
- **audio_mute** – Stummschaltung (on/off)
- **mic_active** – Mikrofon in Benutzung (binary sensor)
- **gpu_memory_used / gpu_memory_total** – GPU VRAM (NVIDIA/AMD)
- **gpu_load** – GPU-Auslastung in %
- **network_upload / network_download** – Netzwerk-Durchsatz in KB/s

### ⚡ Neue Befehle
- **lock_screen**, **sleep**, **hibernate** – PC-Energiebefehle
- **volume_up**, **volume_down**, **volume_mute** – Lautstärke-Steuerung
- **media_play_pause**, **media_next**, **media_previous** – Mediensteuerung

### 🌍 Lokalisierung
- 22 neue Lokalisierungs-Keys in allen 6 Sprachen (de, en, es, fr, zh, ja)

### 🐛 Bugfixes
- Empty Disk Key für Root-Mount "/" behoben
- hwmon Duplicate IDs korrigiert
- Over-aggressive Reconnect-Block entschärft
- SSL defaults auf false gesetzt
- WebSocket Message-Loop-Fix
- Config Race Condition behoben

## [v4.1.0] - 2026-05-23
- 🎨 **Notification Toast Overhaul:** Modernes Dark-Theme-Design – Navy-Blue-Palette (#16213E), Accent-Farben (Blau/Grün), Timestamp-Label, Hover-Effekte auf Buttons
- 🔔 **ShowConnectionToast:** Neue statische Methode mit grünem Accent für Verbindungs-Benachrichtigungen
- 🛠 **Settings UI:** Tooltips auf allen Buttons für bessere Bedienbarkeit
- 📌 **Version Bump:** Alle Versions-Strings auf 4.1.0 aktualisiert (VERSION, csproj, Fallback)
- 🐛 **Bug Fix:** VERSION-Datei in src/HaDeskLink/VERSION zeigte falsche Version 3.0.9

## [v4.0.0] - 2026-05-23
- 🆕 **Neu:** Embedded HA Dashboard mit WebView.Avalonia (WebKitGTK) — einmaliges Login, Session bleibt erhalten
- 🎨 **Redesign:** Moderne Notification-Popups (Dark Theme, abgerundete Ecken)
- 🎨 **Redesign:** Modernisierte Einstellungen
- 📊 **Sensoren:** `/sys/class/thermal` + `hwmon` + `lm-sensors` (treiberlos)


## [v3.0.7] - 2026-04-22
- 📖 **Doku aktualisiert:** README, README_EN, MANUAL, COMPARISON auf v3.0-Stand
- 🎨 **Dashboard modernisiert:** Bessere Übersicht, Sektions-Header
- 📷 **Webcam-Sensor** in Doku korrekt dokumentiert
- 📸 **Screenshot & Actionable Notifications** in Doku ergänzt
- 🔋 **Helligkeits-Befehle** in Doku ergänzt

## [v3.0.6] - 2026-04-22
- 🐛 **Bug Fix:** Notification-Parsing – unterstützt jetzt verschachteltes data.data.command Format (HA mobile_app)
- 🐛 **Bug Fix:** Brightness-Befehl – PowerShell-Fallback wenn WMI nicht funktioniert (Windows)
- 🐛 **Bug Fix:** fullscreen_app Sensor entfernt (Duplikat) auf Linux + Mac
- ✨ **Neu:** ha_desklink_version Sensor auf allen 3 Plattformen

## [v3.0.5] - 2026-04-22
- ✨ **Neu:** ha_desklink_version Sensor – zeigt aktuelle App-Version in HA
- 🐛 **Bug Fix:** fullscreen_app Sensor entfernt (Duplikat von active_window/fullscreen)

## [v3.0.1] - 2026-04-22
- 🐛 **Bug Fix:** Token-Entschlüsselung gibt leeren String zurück → keine HA-Verbindung mehr (verhindert IP-Sperre durch zu viele fehlgeschlagene Auth-Versuche)
- 🐛 **Bug Fix:** WebhookServer-Crash durch disposed CancellationTokenSource (Windows)
Alle nennenswerten Änderungen an diesem Projekt werden hier dokumentiert.

## [v3.0.0] - 2026-04-22
- 🔔 **Actionable Notifications** – Benachrichtigungen mit Aktions-Buttons via notify-send. Daemon führt `command_on_action` automatisch aus, Dashboard zeigt verfügbare Aktionen.
- ⚡ **Quick Actions** – Avalonia UI Popup mit HA-Entity-Toggle-Buttons. Button im Dashboard. Konfigurierbar in config.json (`QuickActions`-Feld).
- 📸 **Screenshot-Befehl** – `screenshot`/`screenshot_save` speichert Bildschirmfoto (gnome-screenshot/scrot/grim) und sendet als HA-Event.
- 📷 **Webcam-Sensor** – Neuer Sensor `webcam_active` (on/off) prüft `/dev/video*` ob eine Webcam in Benutzung ist.
- 🌍 **Neue Lokalisierungs-Keys** für alle 6 Sprachen

## [v2.2.0] - 2026-04-22
- 🖥️ **Vollbild-Sensor** – zeigt welches Programm im Vollbild läuft (X11, `xdotool`/`xprop`)
- 📺 **Monitor-Layout-Sensor** – aktives Monitor-Layout (`xrandr`)
- ☀️ **Helligkeit steuern** – neue Befehle `brightness_up`/`brightness_down`/`brightness:50` via `brightnessctl` + Sensor
- 🌍 **Mehrsprachigkeit** – Deutsch (Standard), Englisch, Spanisch, Französisch, Chinesisch, Japanisch
- 🌍 Community kann eigene Sprachdateien hinzufügen

## [v2.1.1] - 2026-04-22
- Avalonia UI Dashboard (Status, Sensoren, Setup, Discord-Link)
- Lizenz auf GPL v3 geändert (Closed-Source-Nutzung nicht mehr erlaubt)
- CREDITS.md hinzugefügt (KI-Attribution)
- Englische README hinzugefügt (Deutsch = Original)
- macOS-Hinweis: Keine Mac-Hardware zum Testen verfügbar

## [v2.1.0] - (nicht veröffentlicht – Änderungen in v2.1.1 enthalten)

## [v2.0.0] - 2026-04-22
- Initialer Linux-Port basierend auf der Windows-Version
- C# / .NET 8, Avalonia UI
- Sensoren: CPU, RAM, Laufwerke, Akku, Uptime, Netzwerk (via /sys, /proc, lm-sensors)
- PC-Befehle: Shutdown, Restart, Hibernate, Suspend, Lock, Lautstärke, Monitor an/aus
- WebSocket-Push-Notifications
- Systemd-Service für Hintergrundbetrieb
- Setup-Wizard und grafische Oberfläche
- Auto-Update von GitHub Releases
- CI/CD via GitHub Actions (x64 + ARM64)

---

Das Format basiert auf [Keep a Changelog](https://keepachangelog.com/de/).