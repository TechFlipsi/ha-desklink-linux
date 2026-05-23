# Changelog

## [v4.0.0] - 2026-05-23
- 🆕 **Neu:** Embedded HA Dashboard mit WebView.Avalonia (WebKitGTK) + external_auth Auto-Login
- 🆕 **Neu:** AuthGuard – IP-Ban-Schutz mit Rate-Limiting und Retry-Backoff
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