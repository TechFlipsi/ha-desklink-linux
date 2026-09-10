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
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace HaDeskLink;

/// <summary>
/// Widget configuration entry (persisted inside Config.Widgets as a JSON array).
/// Type: "sensor" | "toggle" | "multi_toggle".
/// Position = monitor index + offset inside that monitor's working area
/// (stable across monitor layout changes and rearranging — Phase D).
/// </summary>
public class WidgetConfig
{
    public string Type { get; set; } = "sensor";
    public string Name { get; set; } = "";
    public string EntityId { get; set; } = "";
    /// <summary>Only for multi_toggle: 2-4 entity IDs.</summary>
    public List<string> Entities { get; set; } = new();
    public int Monitor { get; set; } = 0;
    public int OffsetX { get; set; } = 20;
    public int OffsetY { get; set; } = 20;
    public bool ClickThrough { get; set; } = false;
}

/// <summary>
/// Owns the desktop-widget lifecycle (Phase D, Linux/X11):
///  - builds one WidgetWindow per configured widget
///  - pins each window BELOW all normal windows via EWMH
///    (_NET_WM_STATE_BELOW + SKIP_TASKBAR + SKIP_PAGER + STICKY,
///    alternative hint _NET_WM_WINDOW_TYPE_DESKTOP)
///  - polls HA entity states and pushes them into the cards
///  - re-applies the X11 atoms after every Show() because re-mapping
///    resets window properties (Avalonia issue #16115)
/// Path: TopLevel.TryGetPlatformHandle() → X11 window ID (XWayland exposes the
/// same X11 handles) → P/Invoke on libX11. NOT override-redirect — keeps
/// compositing, stacking and multi-monitor positioning sane.
/// </summary>
public static class WidgetManager
{
    private static readonly List<WidgetWindow> _windows = new();
    private static readonly List<WidgetConfig> _configs = new();
    private static Timer? _pollTimer;
    private static HaApiClient? _api;
    private static bool _started;
    private static bool _polling;

    private const int PollIntervalMs = 5000;

    // ═══════════════════════════════════════════════════════
    // LIFECYCLE
    // ═══════════════════════════════════════════════════════

    /// <summary>
    /// Create + show all configured widgets and start state polling.
    /// Call once at GUI startup or after the widget list changed in Settings.
    /// Thread-safe: marshals to the UI thread if needed.
    /// </summary>
    public static void Start(Config config)
    {
        if (Dispatcher.UIThread.CheckAccess())
            StartCore(config);
        else
            Dispatcher.UIThread.Post(() => StartCore(config));
    }

    /// <summary>Close all widgets and stop polling.</summary>
    public static void Stop()
    {
        if (Dispatcher.UIThread.CheckAccess())
            StopCore();
        else
            Dispatcher.UIThread.Post(StopCore);
    }

    /// <summary>Full restart with a (possibly changed) config — used after saving in the widget editor.</summary>
    public static void Restart(Config config)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            StopCore();
            StartCore(config);
        }
        else
        {
            Dispatcher.UIThread.Post(() =>
            {
                StopCore();
                StartCore(config);
            });
        }
    }

    private static void StartCore(Config config)
    {
        StopCore();

        var widgets = LoadWidgets(config);
        if (widgets.Count == 0) return;

        var api = new HaApiClient(Config.GetConfigDir(), config.VerifySsl);
        try { api.LoadRegistration(); } catch { }
        api.SetToken(config.HaToken);
        _api = api;

        _configs.Clear();
        _configs.AddRange(widgets);
        _started = true;

        foreach (var cfg in _configs)
        {
            try
            {
                var win = new WidgetWindow(cfg, api);
                // Re-Apply nach Hide/Show (Avalonia #16115): Re-Mapping setzt die
                // X11-Window-Properties zurück → Hints bei jedem erneuten Anzeigen
                // neu setzen (Opened), nicht nur einmalig nach dem ersten Show.
                win.Opened += (s, e) => WidgetManagerX11.ApplyHints(win, cfg);
                win.Show();
                win.ApplyPosition();
                // After Show() the native X11 window exists → apply EWMH hints now.
                WidgetManagerX11.ApplyHints(win, cfg);
                _windows.Add(win);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WidgetManager] Failed to create widget '{cfg.Name}': {ex.Message}");
            }
        }

        _pollTimer = new Timer(async _ => await PollStatesAsync(), null,
            TimeSpan.Zero, TimeSpan.FromMilliseconds(PollIntervalMs));

        Console.WriteLine($"[WidgetManager] Started {_windows.Count} widget(s)");
    }

    private static void StopCore()
    {
        _pollTimer?.Dispose();
        _pollTimer = null;
        _started = false;

        foreach (var w in _windows.ToList())
        {
            try { w.Hide(); } catch { }
            try { w.Close(); } catch { }
        }
        _windows.Clear();
        _configs.Clear();
    }

    /// <summary>
    /// Show one widget as an interactive desktop preview from the Settings editor.
    /// The preview runs standalone (own API client, not the manager lifecycle)
    /// so it works even while the real widget set is already running.
    /// </summary>
    public static WidgetWindow ShowTestWidget(WidgetConfig cfg, Config config)
    {
        var api = new HaApiClient(Config.GetConfigDir(), config.VerifySsl);
        try { api.LoadRegistration(); } catch { }
        api.SetToken(config.HaToken);

        var win = new WidgetWindow(cfg, api, testMode: true);
        win.Opened += (s, e) => WidgetManagerX11.ApplyHints(win, cfg); // re-apply on every (re-)show
        win.Show();
        win.ApplyPosition();
        WidgetManagerX11.ApplyHints(win, cfg); // preview sits on the desktop layer too
        _ = RefreshStatesAsync(win, cfg, api);
        return win;
    }

    // ═══════════════════════════════════════════════════════
    // CONFIG (QuickActions JSON pattern)
    // ═══════════════════════════════════════════════════════

    /// <summary>
    /// Parse the Config.Widgets JSON array. Tolerant against corrupt input → empty list.
    /// </summary>
    public static List<WidgetConfig> LoadWidgets(Config config)
    {
        var result = new List<WidgetConfig>();
        if (string.IsNullOrWhiteSpace(config.Widgets)) return result;
        try
        {
            using var doc = JsonDocument.Parse(config.Widgets);
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                var cfg = new WidgetConfig
                {
                    Type = item.TryGetProperty("type", out var t) ? t.GetString() ?? "sensor" : "sensor",
                    Name = item.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                    EntityId = item.TryGetProperty("entityId", out var eid) ? eid.GetString() ?? "" : "",
                    Monitor = item.TryGetProperty("monitor", out var m) && m.TryGetInt32(out var mi) ? mi : 0,
                    OffsetX = item.TryGetProperty("offsetX", out var ox) && ox.TryGetInt32(out var oxi) ? oxi : 20,
                    OffsetY = item.TryGetProperty("offsetY", out var oy) && oy.TryGetInt32(out var oyi) ? oyi : 20,
                    ClickThrough = item.TryGetProperty("clickThrough", out var ct) && ct.GetBoolean(),
                };
                if (item.TryGetProperty("entities", out var ents))
                {
                    foreach (var e in ents.EnumerateArray())
                    {
                        var id = e.GetString();
                        if (!string.IsNullOrEmpty(id)) cfg.Entities.Add(id);
                    }
                }
                // multi_toggle without entities falls back to the single entityId
                if (cfg.Type == "multi_toggle" && cfg.Entities.Count == 0 && !string.IsNullOrEmpty(cfg.EntityId))
                    cfg.Entities.Add(cfg.EntityId);
                if (string.IsNullOrEmpty(cfg.EntityId) && cfg.Entities.Count > 0)
                    cfg.EntityId = cfg.Entities[0];

                if (!IsValid(cfg)) continue;
                result.Add(cfg);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WidgetManager] Widget config parse error: {ex.Message}");
        }
        return result;
    }

    /// <summary>Serialize a widget list into the Config.Widgets JSON field.</summary>
    public static string SerializeWidgets(List<WidgetConfig> widgets) =>
        JsonSerializer.Serialize(widgets, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        });

    private static bool IsValid(WidgetConfig cfg) => cfg.Type switch
    {
        "multi_toggle" => cfg.Entities.Count is >= 2 and <= 4,
        "toggle" or "sensor" => !string.IsNullOrEmpty(cfg.EntityId),
        _ => false,
    };

    // ═══════════════════════════════════════════════════════
    // STATE POLLING (shared HA client, fan-out to all cards)
    // ═══════════════════════════════════════════════════════

    private static async Task PollStatesAsync()
    {
        if (!_started || _api == null || _configs.Count == 0 || _polling) return;
        _polling = true;
        try
        {
            var states = await _api.GetEntityStatesAsync();
            if (states == null) return;
            Dispatcher.UIThread.Post(() =>
            {
                foreach (var win in _windows.ToList())
                {
                    try { PushStates(win, states); }
                    catch { }
                }
            });
        }
        catch
        {
            // offline → keep last values, retry on next tick
        }
        finally
        {
            _polling = false;
        }
    }

    private static async Task RefreshStatesAsync(WidgetWindow win, WidgetConfig cfg, HaApiClient api)
    {
        try
        {
            var states = await api.GetEntityStatesAsync();
            if (states == null) return;
            Dispatcher.UIThread.Post(() => { try { PushStates(win, states); } catch { } });
        }
        catch { }
    }

    private static void PushStates(WidgetWindow win, Dictionary<string, (string state, string friendlyName, string unit)> states)
    {
        var cfg = win.Config;
        if (cfg.Type == "multi_toggle")
        {
            foreach (var entityId in cfg.Entities)
                if (states.TryGetValue(entityId, out var s))
                    win.UpdateEntity(entityId, s.state, s.friendlyName, s.unit);
        }
        else if (states.TryGetValue(cfg.EntityId, out var s))
        {
            win.UpdateEntity(cfg.EntityId, s.state, s.friendlyName, s.unit);
        }
    }

    // ═══════════════════════════════════════════════════════
    // X11 / EWMH PINNED-BELOW LAYER
    // ═══════════════════════════════════════════════════════

    /// <summary>
    /// X11 interop: reads the X11 window ID from Avalonia's platform handle
    /// (descriptor "X11" on the Linux X11/XWayland backend) and applies the
    /// EWMH desktop-layer hints via P/Invoke on libX11.
    /// </summary>
    internal static class WidgetManagerX11
    {
        private const string LibX11 = "libX11.so.6";
        private const string LibXext = "libXext.so.6";

        // ── libX11 ──
        [DllImport(LibX11, EntryPoint = "XOpenDisplay")]
        private static extern IntPtr XOpenDisplay(IntPtr display);

        [DllImport(LibX11, EntryPoint = "XInternAtom")]
        private static extern IntPtr XInternAtom(IntPtr display, [MarshalAs(UnmanagedType.LPStr)] string name, bool onlyIfExists);

        // Achtung: data muss long[] sein — bei format=32 interpretiert Xlib den
        // Puffer als native long (8 Bytes je Element auf 64-Bit). int[] wäre der
        // klassische X11-32-Bit-Property-Bug (Xlib liest 8 Bytes pro Element →
        // Heap-Overread, kaputte Atoms).
        [DllImport(LibX11, EntryPoint = "XChangeProperty")]
        private static extern int XChangeProperty(IntPtr display, IntPtr window, IntPtr property, IntPtr type,
            int format, int mode, long[] data, int nelements);

        [DllImport(LibX11, EntryPoint = "XSendEvent")]
        private static extern int XSendEvent(IntPtr display, IntPtr window, bool propagate, long eventMask, ref XClientMessageEvent ev);

        [DllImport(LibX11, EntryPoint = "XFlush")]
        private static extern int XFlush(IntPtr display);

        [DllImport(LibX11, EntryPoint = "XDefaultRootWindow")]
        private static extern IntPtr XDefaultRootWindow(IntPtr display);

        // ── libXext (XShape for click-through input regions) ──
        private const int ShapeInput = 2;   // ShapeType
        private const int ShapeSet = 0;     // ShapeOperation

        [DllImport(LibXext, EntryPoint = "XShapeCombineRectangles")]
        private static extern void XShapeCombineRectangles(IntPtr display, IntPtr window, int shapeKind,
            int xoffset, int yoffset, IntPtr rectangles, int nRects, int op, int ordering);

        [StructLayout(LayoutKind.Sequential)]
        private struct XClientMessageEvent
        {
            public int Type;            // ClientMessage = 33
            public IntPtr Serial;
            public IntPtr SendEvent;
            public IntPtr Display;
            public IntPtr Window;
            public IntPtr Message_Type;
            public int Format;
            public IntPtr Data0;
            public IntPtr Data1;
            public IntPtr Data2;
            public IntPtr Data3;
            public IntPtr Data4;
        }

        private const int ClientMessage = 33;
        private const long SubstructureNotifyMask = 1L << 19;
        private const long SubstructureRedirectMask = 1L << 20;
        private const int PropModeReplace = 0;
        private const int NetWmStateAdd = 1; // EWMH _NET_WM_STATE_ADD

        private static IntPtr _display;
        private static readonly object _displayLock = new();
        private static bool _x11Available = true;

        // ── Internierte Atoms (Cache) ──
        // XInternAtom ist eine Server-Round-Trip pro Aufruf; Atom-Werte sind pro
        // Display stabil → einmal internieren und cachen (ApplyHints läuft nach
        // jedem Hide/Show erneut, Atoms dürfen nicht hardcoded sein).
        private static readonly Dictionary<string, IntPtr> _atomCache = new();

        private static IntPtr InternAtom(IntPtr display, string name)
        {
            lock (_atomCache)
            {
                if (_atomCache.TryGetValue(name, out var atom) && atom != IntPtr.Zero)
                    return atom;
                atom = XInternAtom(display, name, false);
                if (atom != IntPtr.Zero)
                    _atomCache[name] = atom;
                return atom;
            }
        }

        private static IntPtr GetDisplay()
        {
            if (!_x11Available) return IntPtr.Zero;
            lock (_displayLock)
            {
                if (_display != IntPtr.Zero) return _display;
                try
                {
                    _display = XOpenDisplay(IntPtr.Zero);
                    if (_display == IntPtr.Zero)
                        Console.WriteLine("[WidgetManager] XOpenDisplay failed — headless or no X11/XWayland session");
                }
                catch (DllNotFoundException)
                {
                    _x11Available = false;
                    Console.WriteLine("[WidgetManager] libX11 not found — widgets stay regular top-level windows");
                }
                catch (Exception ex)
                {
                    _x11Available = false;
                    Console.WriteLine($"[WidgetManager] X11 init error: {ex.Message}");
                }
                return _display;
            }
        }

        /// <summary>
        /// Apply all desktop-layer hints to a SHOWN widget window:
        ///   _NET_WM_WINDOW_TYPE = DESKTOP (alternative EWMH below-hint)
        ///   _NET_WM_STATE = BELOW + SKIP_TASKBAR + SKIP_PAGER + STICKY
        /// plus per-atom client messages to the root window (the canonical EWMH
        /// path most WMs honor). MUST be re-run after every Hide/Show cycle —
        /// re-mapping resets the properties (Avalonia issue #16115).
        /// </summary>
        public static void ApplyHints(Window window, WidgetConfig cfg)
        {
            var display = GetDisplay();
            if (display == IntPtr.Zero) return;

            var handle = window.TryGetPlatformHandle();
            if (handle == null)
            {
                Console.WriteLine("[WidgetManager] No platform handle (window not shown?) — X11 hints skipped");
                return;
            }
            var descriptor = handle.HandleDescriptor;
            var xid = handle.Handle;
            if (xid == IntPtr.Zero) return;
            if (!string.IsNullOrEmpty(descriptor) && !descriptor.Contains("X11", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"[WidgetManager] Platform handle '{descriptor}' is not X11 — hints skipped");
                return;
            }

            try
            {
                SetNetWmWindowTypeDesktop(display, xid);
                SetNetWmStateList(display, xid, new[]
                {
                    "_NET_WM_STATE_BELOW",
                    "_NET_WM_STATE_SKIP_TASKBAR",
                    "_NET_WM_STATE_SKIP_PAGER",
                    "_NET_WM_STATE_STICKY"
                });
                foreach (var state in new[] { "_NET_WM_STATE_BELOW", "_NET_WM_STATE_SKIP_TASKBAR", "_NET_WM_STATE_SKIP_PAGER", "_NET_WM_STATE_STICKY" })
                    SendNetWmStateMessage(display, xid, state, NetWmStateAdd);
                XFlush(display);

                if (cfg.ClickThrough)
                    ApplyClickThrough(display, xid);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WidgetManager] X11 EWMH hints failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Click-through: give the window an EMPTY XShape input region so all
        /// pointer events fall through to whatever is underneath (XShape from
        /// libXext — the standard X11 click-through trick, like conky/desklets).
        /// </summary>
        private static void ApplyClickThrough(IntPtr display, IntPtr xid)
        {
            try
            {
                XShapeCombineRectangles(display, xid, ShapeInput, 0, 0, IntPtr.Zero, 0, ShapeSet, 0);
                XFlush(display);
            }
            catch (DllNotFoundException)
            {
                Console.WriteLine("[WidgetManager] libXext not found — click-through disabled");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WidgetManager] XShape click-through failed: {ex.Message}");
            }
        }

        /// <summary>
        /// _NET_WM_WINDOW_TYPE = _NET_WM_WINDOW_TYPE_DESKTOP.
        /// EWMH type atoms are 32-bit values; with format=32 XChangeProperty
        /// takes them as native longs, so one long per atom.
        /// </summary>
        private static void SetNetWmWindowTypeDesktop(IntPtr display, IntPtr xid)
        {
            var typeProp = InternAtom(display, "_NET_WM_WINDOW_TYPE");
            var desktopAtom = InternAtom(display, "_NET_WM_WINDOW_TYPE_DESKTOP");
            var atomType = InternAtom(display, "ATOM");
            if (typeProp == IntPtr.Zero || desktopAtom == IntPtr.Zero) return;

            // format=32 = "32-Bit-Werte", die Xlib als native long (8 Bytes auf
            // 64-Bit) übergibt → long[] ist der kanonische Weg.
            var data = new long[] { desktopAtom.ToInt64() };
            XChangeProperty(display, xid, typeProp, atomType, 32, PropModeReplace, data, 1);
        }

        /// <summary>
        /// Write the full _NET_WM_STATE list (replaces any previous list — we
        /// always set the complete known set, so replace is exactly right here).
        /// </summary>
        private static void SetNetWmStateList(IntPtr display, IntPtr xid, string[] stateNames)
        {
            var stateProp = InternAtom(display, "_NET_WM_STATE");
            var atomType = InternAtom(display, "ATOM");
            if (stateProp == IntPtr.Zero) return;

            var atoms = stateNames
                .Select(n => InternAtom(display, n))
                .Where(a => a != IntPtr.Zero)
                .ToList();
            if (atoms.Count == 0) return;

            // ein native long je Atom (format 32 = 8 Bytes je Element auf 64-Bit)
            var data = atoms.Select(a => a.ToInt64()).ToArray();
            XChangeProperty(display, xid, stateProp, atomType, 32, PropModeReplace, data, data.Length);
        }

        /// <summary>
        /// EWMH client message on the ROOT window (WMs select
        /// SubstructureRedirectMask there): window = target client, Data0 =
        /// action (_ADD), Data1 = the state atom.
        /// </summary>
        private static void SendNetWmStateMessage(IntPtr display, IntPtr xid, string stateName, int action)
        {
            var netWmState = InternAtom(display, "_NET_WM_STATE");
            var stateAtom = InternAtom(display, stateName);
            if (netWmState == IntPtr.Zero || stateAtom == IntPtr.Zero) return;

            var ev = new XClientMessageEvent
            {
                Type = ClientMessage,
                Display = display,
                Window = xid,
                Message_Type = netWmState,
                Format = 32,
                Data0 = new IntPtr(action),
                Data1 = stateAtom,
                Data2 = IntPtr.Zero,
                Data3 = IntPtr.Zero,
                Data4 = IntPtr.Zero,
            };
            _ = XSendEvent(display, XDefaultRootWindow(display), false,
                SubstructureNotifyMask | SubstructureRedirectMask, ref ev);
        }
    }
}