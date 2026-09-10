# Changelog

## [v5.0.5] - 2026-09-10

### 🎵 Music Assistant Integration (neu)
- **MA voll nativ in der App** – Music Assistant direkt in HA DeskLink steuern, ohne die MA-Web-UI: Suche über die komplette Bibliothek (Spotify, YouTube Music, TuneIn, lokale Dateien), Warteschlange anzeigen und umsortieren, Play/Pause/Next/Previous, Lautstärke, Shuffle/Repeat
- **Player-Auswahl in der Sidebar** – alle MA-Player (Badezimmer, Büro, Küche, Denon-AVR, Chromecast…) live mit Abspielstatus
- **Verbindungstest** – Einstellungen → Music Assistant: Host + Port + Token eintragen, Button testet die Verbindung sofort (funktioniert mit MA auf TrueNAS, als HA-Add-on oder Standalone)
- **Token verschlüsselt gespeichert** (AES, machine-keyed) — wie beim HA-Token
- **Live-Updates per Push** – Player- und Queue-Änderungen erscheinen sofort, ohne Neuladen

### 🧩 Desktop-Widgets (neu)
- **Sensor-Karten direkt am Desktop** – z.B. Büro-Temperatur als kleine Karte, live aktualisiert, hinter allen Fenstern (wie ein Wallpaper)
- **Schalter-Karten** – Einzeltoggle oder 2–4 Schalter in einer Karte
- **Multi-Monitor-fähig** – Widgets auf Monitor 2/3 bleiben beim Zocken am Hauptmonitor sichtbar und aktualisieren weiter
- **Klick-Durchlässigkeit** – reine Anzeige-Widgets blockieren keine Klicks im Spiel/Programm (konfigurierbar pro Widget)
- **Widget-Editor** – Einstellungen → Widgets: anlegen, positionieren, testen

### 🌍 i18n
- Neue Keys (`ma_*`, `widget_*`, `stream_*`) in alle 16 Sprachen übernommen

### 🐛 Bugfixes & Maintenance
- **WebView-DLL-Versionskonflikt gefixt** – Avalonia auf 11.3.1 + kompatible WebView-Cross-Pakete (Start-Crash "IViewHandlerProvider" behoben)
- Null-Warnings in SettingsWindow bereinigt

## [v5.0.4] - 2026-09-08

### ✨ UI-Redesign (Port von Windows v5.0.1–v5.0.4)
- **Sidebar-Navigation** – 200px Sidebar links (Dashboard, Quick Actions, Einstellungen, Sensoren aktualisieren) + Content-Bereich + Bottom Bar — gleiche Struktur wie die Windows-Version, nativ in Avalonia (Grid/DockPanel statt SplitContainer; die Windows-SplitContainer-Abstürze v5.0.2/5.0.3 betreffen Avalonia nicht)
- **Einstellungen mit Erklärungstexten** – Neues Settings-Fenster mit 7 Kategorien (Verbindung, Allgemein, Erscheinungsbild, Benachrichtigungen, Tastenkombinationen, MQTT, Quick Actions). Jede Einstellung hat eine Beschreibung (24 neue `desc_*` i18n-Keys), 200px-Label-Spalte gegen Wortumbruch bei deutschen Begriffen, Bottom Bar mit Speichern/Neu verbinden immer sichtbar
- **Quick Actions Popup** – Hotkey-gesteuertes Popup zum Ein-/Ausschalten von HA-Entities (Escape/fokuslos = schließen, Auto-Close nach Toggle)
- **Konfigurierbare Hotkeys** – Quick Actions (Ctrl+Shift+H), Dashboard (Ctrl+Shift+D), Einstellungen (Ctrl+Shift+S) — gelten innerhalb der HA DeskLink Fenster (Linux/Wayland erlaubt keine systemweiten Hotkeys)
- **Actionable Notifications als echte Toasts** – GUI-Modus zeigt HA-Benachrichtigungen jetzt als Avalonia-Toasts mit Aktions-Buttons (Befehle in `command`/`actions`/`command_on_action` werden ausgeführt, Headless-Daemon nutzt weiterhin notify-send)
- **Benachrichtigungs-Position & Monitor** – Konfigurierbare Toast-Position (unten links/rechts, oben links/rechts) und Monitor-Auswahl
- **Dark/Light Theme** – Einstellung System/Hell/Dunkel für das gesamte UI
- **Autostart (XDG)** – Desktop-Eintrag in `~/.config/autostart/` statt Windows-Registry/Task Scheduler

### 🌍 i18n
- **24 neue `desc_*`-Keys** in ALLE 15 Sprachen übernommen (de, en, es, fr, it, ja, ko, nl, pl, pt, ru, sv, tr, zh, ar) — plus 13 Support-Keys (MQTT-Test, Validierungen, Section-Titel)

### 🐛 Bugfixes & Maintenance
- **GitHub Update-Repo-URL** – Auto-Updater prüft jetzt `TechFlipsi/ha-desklink-linux` Releases (wie schon v5.0.0; Windows-Fix v5.0.4 hier ohne Änderung, da korrekt)
- **Discord-Einladungslink** korrigiert (`discord.com/invite/zHPhQ7EaqH`)
- **Dokumentations-URLs** – alte `ha-desklink-dotnet`-Links durch `ha-desklink-windows` ersetzt (README, MANUAL)
- **GetEntitiesAsync** – HA-Entity-Liste für Quick Actions (gleiche API wie Windows)
- **ReRegisterSensors** – Sensoren können aus der GUI heraus neu registriert werden
- **Dashboard-Fenster** – 1300×850, Mindestgröße 800×600, Fokus-Aktivierung (Windows-Parität)
- **Versions-Fallback** – Assembly-Fallback im Versionsstring auf 5.0.4 korrigiert

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