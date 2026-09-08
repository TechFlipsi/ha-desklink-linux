// HA DeskLink - Home Assistant Companion App
// Copyright (C) 2026 Fabian Kirchweger
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License v3 as published
// by the Free Software Foundation.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
// GNU General Public License for more details.
#nullable enable
using Avalonia.Input;
using System;

namespace HaDeskLink;

/// <summary>
/// Configurable hotkey handler — Avalonia/Linux port of the Windows QuickActionHandler.
/// Windows registers global hotkeys via RegisterHotKey(); Linux/Wayland does not allow
/// global hotkeys from user apps, so the hotkey is handled while any HA DeskLink window
/// has keyboard focus (KeyDown on the active window). Same modifier/key config values
/// ("ctrl_shift" + "H") as on Windows are supported.
/// </summary>
public class QuickActionHandler
{
    private readonly Action _onHotkey;
    private readonly string _modifiers;
    private readonly string _key;
    private DateTime _lastTrigger = DateTime.MinValue;
    private bool _enabled;

    public QuickActionHandler(Action onHotkey, string modifiers = "ctrl_shift", string key = "H")
    {
        _onHotkey = onHotkey;
        _modifiers = modifiers;
        _key = key;
        _enabled = modifiers != "none";
    }

    /// <summary>
    /// Linux: no OS-level registration — hotkeys work while an HA DeskLink window is focused.
    /// Kept for API parity with Windows (no-op).
    /// </summary>
    public void Start()
    {
        _enabled = _modifiers != "none";
    }

    public void Stop()
    {
        _enabled = false;
    }

    /// <summary>
    /// Feed a KeyDown event from a focused HA DeskLink window into the handler.
    /// Returns true when the configured hotkey matched (event considered handled).
    /// </summary>
    public bool HandleKey(Key key, KeyModifiers modifiers)
    {
        if (!_enabled) return false;

        // Debounce: ignore if triggered within 300ms (same as Windows)
        if ((DateTime.UtcNow - _lastTrigger).TotalMilliseconds < 300)
            return false;

        var wantedKey = ParseKey(_key);
        if (key != wantedKey) return false;

        var ctrl = modifiers.HasFlag(KeyModifiers.Control);
        var alt = modifiers.HasFlag(KeyModifiers.Alt);
        var shift = modifiers.HasFlag(KeyModifiers.Shift);

        var match = _modifiers switch
        {
            "ctrl_shift" => ctrl && shift && !alt,
            "ctrl_alt" => ctrl && alt && !shift,
            "ctrl" => ctrl && !alt && !shift,
            "alt" => alt && !ctrl && !shift,
            "shift" => shift && !ctrl && !alt,
            "none" => !ctrl && !alt && !shift,
            _ => false,
        };
        if (!match) return false;

        _lastTrigger = DateTime.UtcNow;
        _onHotkey.Invoke();
        return true;
    }

    /// <summary>
    /// Get the display string for the current hotkey (e.g. "Ctrl+Shift+H").
    /// </summary>
    public string GetHotkeyDisplay()
    {
        var modStr = _modifiers switch
        {
            "ctrl_shift" => "Ctrl+Shift",
            "ctrl_alt" => "Ctrl+Alt",
            "ctrl" => "Ctrl",
            "alt" => "Alt",
            "shift" => "Shift",
            "none" => "",
            _ => "Ctrl+Shift"
        };
        return string.IsNullOrEmpty(modStr) ? _key.ToUpper() : $"{modStr}+{_key.ToUpper()}";
    }

    private static Key ParseKey(string key)
    {
        return key.ToUpperInvariant() switch
        {
            "A" => Key.A, "B" => Key.B, "C" => Key.C, "D" => Key.D,
            "E" => Key.E, "F" => Key.F, "G" => Key.G, "H" => Key.H,
            "I" => Key.I, "J" => Key.J, "K" => Key.K, "L" => Key.L,
            "M" => Key.M, "N" => Key.N, "O" => Key.O, "P" => Key.P,
            "Q" => Key.Q, "R" => Key.R, "S" => Key.S, "T" => Key.T,
            "U" => Key.U, "V" => Key.V, "W" => Key.W, "X" => Key.X,
            "Y" => Key.Y, "Z" => Key.Z,
            "F1" => Key.F1, "F2" => Key.F2, "F3" => Key.F3, "F4" => Key.F4,
            "F5" => Key.F5, "F6" => Key.F6, "F7" => Key.F7, "F8" => Key.F8,
            "F9" => Key.F9, "F10" => Key.F10, "F11" => Key.F11, "F12" => Key.F12,
            "SPACE" => Key.Space,
            "ENTER" => Key.Return,
            "TAB" => Key.Tab,
            "ESC" => Key.Escape,
            _ => Key.H  // Default: H
        };
    }
}