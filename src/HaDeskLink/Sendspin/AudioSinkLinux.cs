// HA DeskLink - Sendspin streaming client (Phase E)
// Linux audio output via libpulse-simple (PulseAudio / PipeWire's pulse
// compatibility layer). P/Invoke through System.Runtime.InteropServices.
// NativeLibrary: pa_simple_new / pa_simple_write / pa_simple_drain /
// pa_simple_free. The soname is discovered at first use (LD_LIBRARY_PATH,
// bare soname, then common absolute paths).
//
// This program is licensed under the GNU General Public License v3.
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace HaDeskLink.Sendspin;

/// <summary>
/// Thin wrapper over libpulse-simple for S16LE playback.
/// pa_simple_write buffers internally; the caller paces writes against
/// the scheduled play times.
/// </summary>
public sealed class AudioSinkLinux : IDisposable
{
    private const int PaSampleS16Le = 3;   // pa_sample_format_t
    private const int PaStreamPlayback = 0; // pa_stream_direction_t
    private const int PaSampleSpecSize = 12; // int format + uint32 rate + byte channels (+pad)

    private static readonly string[] LibraryNames =
    {
        "pulse-simple",
        "libpulse-simple.so.0",
        "/usr/lib/x86_64-linux-gnu/libpulse-simple.so.0",
        "/usr/lib64/libpulse-simple.so.0",
        "/usr/lib/libpulse-simple.so.0",
        "/lib/x86_64-linux-gnu/libpulse-simple.so.0",
    };

    private static bool _resolved;
    private static string? _resolvedName;
    private static IntPtr _library = IntPtr.Zero;

    private static PaSimpleNewDelegate? _paSimpleNew;
    private static PaSimpleWriteDelegate? _paSimpleWrite;
    private static PaSimpleDrainDelegate? _paSimpleDrain;
    private static PaSimpleFreeDelegate? _paSimpleFree;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr PaSimpleNewDelegate(IntPtr server, IntPtr name, int dir,
        IntPtr dev, IntPtr streamName, IntPtr ss, IntPtr map, IntPtr attr,
        IntPtr errorCb, IntPtr error);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int PaSimpleWriteDelegate(IntPtr s, IntPtr data, UIntPtr bytes, IntPtr error);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int PaSimpleDrainDelegate(IntPtr s, IntPtr error);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void PaSimpleFreeDelegate(IntPtr s);

    static AudioSinkLinux()
    {
        // Eager resolve so IsAvailable answers without a first-instance penalty.
        _ = IsAvailable;
    }

    private static void ResolveLibrary()
    {
        if (_resolved) return;

        var candidates = new List<string>();
        // LD_LIBRARY_PATH entries first (nix store etc.).
        try
        {
            string? ldPaths = Environment.GetEnvironmentVariable("LD_LIBRARY_PATH");
            if (!string.IsNullOrEmpty(ldPaths))
            {
                foreach (string dir in ldPaths.Split(':', StringSplitOptions.RemoveEmptyEntries))
                {
                    candidates.Add(Path.Combine(dir, "libpulse-simple.so.0"));
                    candidates.Add(Path.Combine(dir, "libpulse-simple.so"));
                }
            }
        }
        catch { }
        candidates.AddRange(LibraryNames);

        foreach (string name in candidates)
        {
            try
            {
                IntPtr lib = NativeLibrary.Load(name);
                _paSimpleNew = Marshal.GetDelegateForFunctionPointer<PaSimpleNewDelegate>(
                    NativeLibrary.GetExport(lib, "pa_simple_new"));
                _paSimpleWrite = Marshal.GetDelegateForFunctionPointer<PaSimpleWriteDelegate>(
                    NativeLibrary.GetExport(lib, "pa_simple_write"));
                _paSimpleDrain = Marshal.GetDelegateForFunctionPointer<PaSimpleDrainDelegate>(
                    NativeLibrary.GetExport(lib, "pa_simple_drain"));
                _paSimpleFree = Marshal.GetDelegateForFunctionPointer<PaSimpleFreeDelegate>(
                    NativeLibrary.GetExport(lib, "pa_simple_free"));
                _library = lib;
                _resolvedName = name;
                _resolved = true;
                return;
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }
        // Leave _resolved false: IsAvailable stays false, constructor throws.
    }

    /// <summary>True when libpulse-simple could be loaded (audio output possible).</summary>
    public static bool IsAvailable
    {
        get
        {
            try
            {
                ResolveLibrary();
                return _resolved;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>The soname the sink bound to, for diagnostics.</summary>
    public static string? ResolvedLibraryName => _resolvedName;

    public int SampleRate { get; }
    public int Channels { get; }
    public int BytesPerFrame { get; }

    private IntPtr _stream;
    private bool _disposed;

    /// <summary>Open a playback stream (S16LE interleaved).</summary>
    public AudioSinkLinux(int sampleRate = 48000, int channels = 2, int bitDepth = 16)
    {
        if (!IsAvailable)
            throw new DllNotFoundException(
                "libpulse-simple not found (tried: " + string.Join(", ", LibraryNames) + ")");
        if (bitDepth != 16)
            throw new NotSupportedException("AudioSinkLinux only supports 16-bit output");
        SampleRate = sampleRate;
        Channels = channels;
        BytesPerFrame = channels * 2;

        // pa_sample_spec { int format; uint32 rate; byte channels; } — manually
        // laid out at a fixed offset so packing is explicit.
        IntPtr spec = Marshal.AllocHGlobal(PaSampleSpecSize);
        IntPtr errPtr = Marshal.AllocHGlobal(4);
        IntPtr appName = Marshal.StringToHGlobalAnsi("HA DeskLink");
        IntPtr streamName = Marshal.StringToHGlobalAnsi("Music");
        try
        {
            Marshal.WriteInt32(spec, 0, PaSampleS16Le);
            Marshal.WriteInt32(spec, 4, sampleRate);
            Marshal.WriteByte(spec, 8, (byte)channels);

            Marshal.WriteInt32(errPtr, 0);
            _stream = _paSimpleNew!(
                IntPtr.Zero,   // server: default
                appName,
                PaStreamPlayback,
                IntPtr.Zero,   // dev: default sink
                streamName,
                spec,
                IntPtr.Zero,   // channel map: default for spec
                IntPtr.Zero,   // buffering attr: default
                IntPtr.Zero,   // no error callback
                errPtr);
            if (_stream == IntPtr.Zero)
            {
                int error = Marshal.ReadInt32(errPtr);
                throw new InvalidOperationException($"pa_simple_new failed (error {error})");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(spec);
            Marshal.FreeHGlobal(errPtr);
            Marshal.FreeHGlobal(appName);
            Marshal.FreeHGlobal(streamName);
        }
    }

    /// <summary>Write interleaved S16LE PCM. Internally buffered by PulseAudio.</summary>
    public void Write(byte[] pcm, int offset, int count)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_stream == IntPtr.Zero || count <= 0) return;

        IntPtr errPtr = Marshal.AllocHGlobal(4);
        IntPtr data = Marshal.AllocHGlobal(count);
        try
        {
            Marshal.Copy(pcm, offset, data, count);
            Marshal.WriteInt32(errPtr, 0);
            int rc = _paSimpleWrite!(_stream, data, (UIntPtr)(nuint)count, errPtr);
            if (rc != 0)
            {
                int error = Marshal.ReadInt32(errPtr);
                throw new InvalidOperationException($"pa_simple_write failed (error {error})");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(data);
            Marshal.FreeHGlobal(errPtr);
        }
    }

    /// <summary>Play all buffered samples to completion.</summary>
    public void Drain()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_stream == IntPtr.Zero) return;
        IntPtr errPtr = Marshal.AllocHGlobal(4);
        try
        {
            Marshal.WriteInt32(errPtr, 0);
            _paSimpleDrain!(_stream, errPtr);
        }
        finally
        {
            Marshal.FreeHGlobal(errPtr);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        if (_stream != IntPtr.Zero)
        {
            _paSimpleFree!(_stream);
            _stream = IntPtr.Zero;
        }
        _disposed = true;
    }
}