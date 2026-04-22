# Changelog

Alle nennenswerten Änderungen an diesem Projekt werden hier dokumentiert.

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