using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace TileViewer.ManagedPlugin.Native
{
    internal static unsafe class TileDecoderNativeExports
    {
        private static readonly tile_decoder_t* s_decoder;
        private static readonly byte* s_defaultConfigBuffer;
        private static readonly nuint s_defaultConfigLength;
        private static readonly object s_logSync = new();
        private static byte* s_logBuffer;
        private static nuint s_logCapacity;

        static TileDecoderNativeExports()
        {
            ITileViewerPlugin template = ManagedTileViewerPlugin.CreateDefault();
            tile_decoder_t decoder = new()
            {
                version = template.Version,
                size = (uint)sizeof(tile_decoder_t),
                context = null,
                msg = null,
                open = &DecodeOpen,
                close = &DecodeClose,
                decodeone = &DecodeOne,
                decodeall = template.IsDecodeAll ? &DecodeAll : null,
                pre = &DecodePre,
                post = &DecodePost,
                sendui = &SendUi,
                recvui = &RecvUi,
            };

            s_decoder = (tile_decoder_t*)NativeMemory.Alloc((nuint)sizeof(tile_decoder_t));
            *s_decoder = decoder;

            byte[] json = Encoding.UTF8.GetBytes(template.CfgJson);
            s_defaultConfigLength = (nuint)json.Length;
            s_defaultConfigBuffer = (byte*)NativeMemory.Alloc((nuint)json.Length + 1);
            Span<byte> dest = new Span<byte>(s_defaultConfigBuffer, json.Length + 1);
            json.CopyTo(dest);
            dest[json.Length] = 0;
        }

        [UnmanagedCallersOnly(EntryPoint = "get_decoder", CallConvs = new[] { typeof(CallConvCdecl) })]
        internal static tile_decoder_t* GetDecoder() => s_decoder;

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static PluginStatus DecodeOpen(byte* name, void** context)
        {
            if (context is null)
            {
                ClearLogBuffer();
                return PluginStatus.Fail;
            }

            try
            {
                string pluginName = PtrToString(name);
                ManagedDecoderSession session = ManagedDecoderSession.Create(pluginName);
                TileDecodeOpenRequest request = session.Plugin.CreateOpenRequest(pluginName);
                PluginStatus status = session.Plugin.DecodeOpen(request);
                PublishLogs(session.Plugin);
                if (status != PluginStatus.Ok)
                {
                    session.Dispose();
                    return status;
                }

                GCHandle handle = GCHandle.Alloc(session, GCHandleType.Normal);
                *context = (void*)GCHandle.ToIntPtr(handle);
                return status;
            }
            catch
            {
                ClearLogBuffer();
                return PluginStatus.Fail;
            }
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static PluginStatus DecodeClose(void* context)
        {
            if (context is null)
            {
                ClearLogBuffer();
                return PluginStatus.Fail;
            }

            GCHandle handle = default;
            ManagedDecoderSession? session = null;
            try
            {
                handle = GCHandle.FromIntPtr((nint)context);
                if (!handle.IsAllocated || handle.Target is not ManagedDecoderSession managed)
                {
                    return PluginStatus.Fail;
                }

                session = managed;
                TileDecodeCloseRequest request = session.Plugin.CreateCloseRequest();
                PluginStatus status = session.Plugin.DecodeClose(request);
                PublishLogs(session.Plugin);
                session.Dispose();
                handle.Free();
                return status;
            }
            catch
            {
                if (handle.IsAllocated)
                {
                    handle.Free();
                }

                session?.Dispose();
                ClearLogBuffer();
                return PluginStatus.Fail;
            }
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static PluginStatus DecodeOne(
            void* context,
            byte* data,
            nuint dataSize,
            tilepos_t* position,
            tilefmt_t* format,
            pixel_t* output,
            byte remainIndex)
        {
            if (output is null || !TryGetSession(context, out ManagedDecoderSession session))
            {
                ClearLogBuffer();
                return PluginStatus.Fail;
            }

            if (!TryCreateReadOnlyMemory(data, dataSize, out ReadOnlyMemory<byte> buffer, out PluginStatus status))
            {
                ClearLogBuffer();
                return status;
            }

            if (!TryConvert(format, out TileFormat managedFormat) || position is null)
            {
                ClearLogBuffer();
                return PluginStatus.FormatError;
            }

            TilePosition managedPosition = new(position->i, position->x, position->y);
            bool keepIndex = remainIndex != 0;
            TileDecodeOneRequest request = session.Plugin.CreateDecodeOneRequest(buffer, managedFormat, managedPosition, keepIndex);
            status = session.Plugin.DecodeOne(request, out Pixel pixel);
            PublishLogs(session.Plugin);
            if (status == PluginStatus.Ok)
            {
                *output = pixel_t.FromPixel(pixel);
            }

            return status;
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static PluginStatus DecodeAll(
            void* context,
            byte* data,
            nuint dataSize,
            tilefmt_t* format,
            pixel_t** pixels,
            nuint* pixelCount,
            byte remainIndex)
        {
            if (pixels is null || pixelCount is null || !TryGetSession(context, out ManagedDecoderSession session))
            {
                ClearLogBuffer();
                return PluginStatus.Fail;
            }

            if (!TryCreateReadOnlyMemory(data, dataSize, out ReadOnlyMemory<byte> buffer, out PluginStatus status))
            {
                ClearLogBuffer();
                return status;
            }

            if (!TryConvert(format, out TileFormat managedFormat))
            {
                ClearLogBuffer();
                return PluginStatus.FormatError;
            }

            if (!TryCalculatePixelCount(managedFormat, dataSize, out int totalPixels))
            {
                ClearLogBuffer();
                return PluginStatus.FormatError;
            }

            using TileDecodeAllRequest request = session.Plugin.CreateDecodeAllRequest(buffer, managedFormat, totalPixels, remainIndex != 0);
            Span<Pixel> destination = request.Destination.Memory.Span;
            if (destination.Length < totalPixels)
            {
                ClearLogBuffer();
                return PluginStatus.RangeError;
            }

            status = session.Plugin.DecodeAll(request);
            PublishLogs(session.Plugin);
            if (status == PluginStatus.Ok)
            {
                session.CopyPixelsToUnmanaged(destination[..totalPixels], pixels, pixelCount);
            }

            return status;
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static PluginStatus DecodePre(void* context, byte* rawData, nuint rawSize, tilecfg_t* configuration)
        {
            if (!TryGetSession(context, out ManagedDecoderSession session))
            {
                ClearLogBuffer();
                return PluginStatus.Fail;
            }

            if (!TryCreateReadOnlyMemory(rawData, rawSize, out ReadOnlyMemory<byte> buffer, out PluginStatus status))
            {
                ClearLogBuffer();
                return status;
            }

            if (!TryConvert(configuration, out TileConfiguration managedConfig))
            {
                ClearLogBuffer();
                return PluginStatus.FormatError;
            }

            TileDecodePipelineRequest request = session.Plugin.CreatePipelineRequest(buffer, managedConfig);
            status = session.Plugin.DecodePre(request);
            PublishLogs(session.Plugin);
            if (configuration is not null)
            {
                WriteConfiguration(configuration, request.Configuration);
            }

            return status;
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static PluginStatus DecodePost(void* context, byte* rawData, nuint rawSize, tilecfg_t* configuration)
        {
            if (!TryGetSession(context, out ManagedDecoderSession session))
            {
                ClearLogBuffer();
                return PluginStatus.Fail;
            }

            if (!TryCreateReadOnlyMemory(rawData, rawSize, out ReadOnlyMemory<byte> buffer, out PluginStatus status))
            {
                ClearLogBuffer();
                return status;
            }

            if (!TryConvert(configuration, out TileConfiguration managedConfig))
            {
                ClearLogBuffer();
                return PluginStatus.FormatError;
            }

            TileDecodePipelineRequest request = session.Plugin.CreatePipelineRequest(buffer, managedConfig);
            status = session.Plugin.DecodePost(request);
            PublishLogs(session.Plugin);
            if (configuration is not null)
            {
                WriteConfiguration(configuration, request.Configuration);
            }

            return status;
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static PluginStatus SendUi(void* context, byte** buffer, nuint* bufferSize)
        {
            if (buffer is null || bufferSize is null)
            {
                ClearLogBuffer();
                return PluginStatus.Fail;
            }

            if (TryGetSession(context, out ManagedDecoderSession session))
            {
                return session.ProvideConfiguration(buffer, bufferSize);
            }

            *buffer = s_defaultConfigBuffer;
            *bufferSize = s_defaultConfigLength;
            ClearLogBuffer();
            return PluginStatus.Ok;
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static PluginStatus RecvUi(void* context, byte* buffer, nuint bufferSize)
        {
            if (!TryGetSession(context, out ManagedDecoderSession session))
            {
                ClearLogBuffer();
                return PluginStatus.Fail;
            }

            if (buffer is null || bufferSize == 0)
            {
                return PluginStatus.Ok;
            }

            try
            {
                ReadOnlySpan<byte> span = new(buffer, checked((int)bufferSize));
                session.AcceptConfiguration(span);
                PublishLogs(session.Plugin);
                return PluginStatus.Ok;
            }
            catch
            {
                ClearLogBuffer();
                return PluginStatus.Fail;
            }
        }

        private static bool TryGetSession(void* context, out ManagedDecoderSession session)
        {
            session = null!;
            if (context is null)
            {
                return false;
            }

            try
            {
                GCHandle handle = GCHandle.FromIntPtr((nint)context);
                if (!handle.IsAllocated || handle.Target is not ManagedDecoderSession managed)
                {
                    return false;
                }

                session = managed;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void PublishLogs(ITileViewerPlugin plugin)
        {
            if (plugin is null)
            {
                ClearLogBuffer();
                return;
            }

            string? lastMessage = null;
            while (plugin.TryDequeueLog(out string message))
            {
                if (!string.IsNullOrEmpty(message))
                {
                    lastMessage = message;
                }
            }

            if (lastMessage is null)
            {
                ClearLogBuffer();
                return;
            }

            WriteLogBuffer(lastMessage);
        }

        private static void WriteLogBuffer(string message)
        {
            lock (s_logSync)
            {
                int byteCount = Encoding.UTF8.GetByteCount(message);
                nuint required = (nuint)(byteCount + 1);
                if (required > s_logCapacity)
                {
                    if (s_logBuffer is not null)
                    {
                        NativeMemory.Free(s_logBuffer);
                    }

                    s_logBuffer = (byte*)NativeMemory.Alloc(required);
                    s_logCapacity = required;
                }

                if (s_logBuffer is null)
                {
                    return;
                }

                Span<byte> destination = new Span<byte>(s_logBuffer, checked((int)s_logCapacity));
                int written = Encoding.UTF8.GetBytes(message, destination);
                destination[written] = 0;
                if (s_decoder is not null)
                {
                    s_decoder->msg = s_logBuffer;
                }
            }
        }

        private static void ClearLogBuffer()
        {
            lock (s_logSync)
            {
                if (s_decoder is not null)
                {
                    s_decoder->msg = null;
                }
            }
        }

        private static bool TryCreateReadOnlyMemory(byte* data, nuint size, out ReadOnlyMemory<byte> memory, out PluginStatus status)
        {
            memory = ReadOnlyMemory<byte>.Empty;
            status = PluginStatus.Ok;
            if (data is null || size == 0)
            {
                return true;
            }

            if (size > int.MaxValue)
            {
                status = PluginStatus.RangeError;
                return false;
            }

            int length = (int)size;
            byte[] buffer = new byte[length];
            new ReadOnlySpan<byte>(data, length).CopyTo(buffer.AsSpan());
            memory = buffer;
            return true;
        }

        private static bool TryConvert(tilefmt_t* format, out TileFormat managed)
        {
            if (format is null)
            {
                managed = default;
                return false;
            }

            managed = new TileFormat(format->w, format->h, format->bpp, format->nbytes);
            return true;
        }

        private static bool TryConvert(tilecfg_t* cfg, out TileConfiguration configuration)
        {
            if (cfg is null)
            {
                configuration = new TileConfiguration();
                return false;
            }

            configuration = new TileConfiguration
            {
                Start = cfg->start,
                Size = cfg->size,
                Rows = cfg->nrow,
                Format = new TileFormat(cfg->fmt.w, cfg->fmt.h, cfg->fmt.bpp, cfg->fmt.nbytes)
            };
            return true;
        }

        private static void WriteConfiguration(tilecfg_t* destination, TileConfiguration configuration)
        {
            if (destination is null)
            {
                return;
            }

            destination->start = configuration.Start;
            destination->size = configuration.Size;
            destination->nrow = configuration.Rows;
            destination->fmt.w = configuration.Format.Width;
            destination->fmt.h = configuration.Format.Height;
            destination->fmt.bpp = configuration.Format.BitsPerPixel;
            destination->fmt.nbytes = configuration.Format.BytesPerTile;
        }

        private static bool TryCalculatePixelCount(TileFormat format, nuint dataSize, out int count)
        {
            count = 0;
            if (format.BitsPerPixel == 0)
            {
                return false;
            }

            ulong totalBits = (ulong)dataSize * 8UL;
            ulong pixels = totalBits / format.BitsPerPixel;
            if (pixels > int.MaxValue)
            {
                return false;
            }

            count = (int)pixels;
            return true;
        }

        private static string PtrToString(byte* value)
        {
            if (value is null)
            {
                return string.Empty;
            }

            return Marshal.PtrToStringUTF8((nint)value) ?? string.Empty;
        }

        private sealed class ManagedDecoderSession : IDisposable
        {
            private nint _pixelBuffer;
            private nuint _pixelCapacity;
            private nint _configBuffer;
            private nuint _configLength;
            private string? _lastConfigurationName;
            private readonly Dictionary<string, string> _optionSnapshot = new(StringComparer.Ordinal);

            private ManagedDecoderSession(ITileViewerPlugin plugin)
            {
                Plugin = plugin;
            }

            public ITileViewerPlugin Plugin { get; }

            public static ManagedDecoderSession Create(string pluginName)
            {
                ITileViewerPlugin plugin = ManagedTileViewerPlugin.CreateDefault();
                if (!string.IsNullOrWhiteSpace(pluginName))
                {
                    plugin.SetPluginName(pluginName);
                }

                return new ManagedDecoderSession(plugin)
                {
                    _lastConfigurationName = string.IsNullOrWhiteSpace(pluginName) ? null : pluginName,
                };
            }

            public PluginStatus ProvideConfiguration(byte** buffer, nuint* bufferSize)
            {
                if (_configBuffer == 0)
                {
                    string json = Plugin.CfgJson;
                    byte[] payload = Encoding.UTF8.GetBytes(json);
                    _configLength = (nuint)payload.Length;
                    _configBuffer = (nint)NativeMemory.Alloc((nuint)payload.Length + 1);
                    Span<byte> destination = new((void*)_configBuffer, payload.Length + 1);
                    payload.CopyTo(destination);
                    destination[payload.Length] = 0;
                }

                *buffer = (byte*)_configBuffer;
                *bufferSize = _configLength;
                return PluginStatus.Ok;
            }

            public void CopyPixelsToUnmanaged(ReadOnlySpan<Pixel> source, pixel_t** destination, nuint* count)
            {
                if (destination is null)
                {
                    return;
                }

                nuint required = (nuint)source.Length;
                EnsurePixelCapacity(required);
                if (required == 0)
                {
                    *destination = (pixel_t*)_pixelBuffer;
                    if (count is not null)
                    {
                        *count = 0;
                    }

                    return;
                }

                Span<pixel_t> target = new((void*)_pixelBuffer, source.Length);
                for (int i = 0; i < source.Length; i++)
                {
                    target[i] = pixel_t.FromPixel(source[i]);
                }

                *destination = (pixel_t*)_pixelBuffer;
                if (count is not null)
                {
                    *count = required;
                }
            }

            public void AcceptConfiguration(ReadOnlySpan<byte> jsonUtf8)
            {
                if (jsonUtf8.IsEmpty)
                {
                    return;
                }

                try
                {
                    PluginConfiguration? configuration = JsonSerializer.Deserialize(jsonUtf8, PluginConfigurationJsonContext.Default.PluginConfiguration);
                    if (configuration is null)
                    {
                        return;
                    }

                    if (!string.IsNullOrWhiteSpace(configuration.Name) &&
                        !string.Equals(configuration.Name, _lastConfigurationName, StringComparison.Ordinal))
                    {
                        Plugin.SetPluginName(configuration.Name);
                        _lastConfigurationName = configuration.Name;
                    }

                    IReadOnlyList<PluginOption>? options = configuration.Options;
                    if (options is null || options.Count == 0)
                    {
                        if (_optionSnapshot.Count == 0)
                        {
                            return;
                        }

                        foreach (string removed in _optionSnapshot.Keys)
                        {
                            Plugin.Log($"Plugin option '{removed}' cleared.");
                        }

                        _optionSnapshot.Clear();
                        return;
                    }

                    LogOptionUpdates(options);
                    Plugin.ApplyOptions(options);
                }
                catch
                {
                    // ignore parsing failures and keep existing configuration
                }
            }

            private void LogOptionUpdates(IReadOnlyList<PluginOption> options)
            {
                HashSet<string> seen = new(StringComparer.Ordinal);
                foreach (PluginOption option in options)
                {
                    if (string.IsNullOrWhiteSpace(option.Name))
                    {
                        continue;
                    }

                    seen.Add(option.Name);
                    string newValue = NormalizeOptionValue(option.Value);
                    if (_optionSnapshot.TryGetValue(option.Name, out string? previousValue))
                    {
                        if (!AreOptionValuesEqual(previousValue, newValue))
                        {
                            Plugin.Log($"Plugin option '{option.Name}' changed from '{FormatValue(previousValue)}' to '{FormatValue(newValue)}'.");
                        }
                    }
                    else
                    {
                        Plugin.Log($"Plugin option '{option.Name}' set to '{FormatValue(newValue)}'.");
                    }

                    _optionSnapshot[option.Name] = newValue;
                }

                if (_optionSnapshot.Count == seen.Count)
                {
                    return;
                }

                List<string>? removals = null;
                foreach (string existing in _optionSnapshot.Keys)
                {
                    if (!seen.Contains(existing))
                    {
                        (removals ??= new List<string>()).Add(existing);
                    }
                }

                if (removals is null)
                {
                    return;
                }

                foreach (string removed in removals)
                {
                    Plugin.Log($"Plugin option '{removed}' cleared.");
                    _optionSnapshot.Remove(removed);
                }
            }

            private static bool AreOptionValuesEqual(string? left, string? right)
            {
                if (ReferenceEquals(left, right))
                {
                    return true;
                }

                return string.Equals(left, right, StringComparison.Ordinal);
            }

            private static string FormatValue(string? value)
            {
                return string.IsNullOrEmpty(value) ? "<empty>" : value;
            }

            private static string NormalizeOptionValue(object? value)
            {
                return value switch
                {
                    null => string.Empty,
                    string s => s,
                    JsonElement element => element.ValueKind switch
                    {
                        JsonValueKind.String => element.GetString() ?? string.Empty,
                        JsonValueKind.Null => string.Empty,
                        _ => element.ToString(),
                    },
                    IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty,
                    _ => value.ToString() ?? string.Empty,
                };
            }

            public void Dispose()
            {
                if (_pixelBuffer != 0)
                {
                    NativeMemory.Free((void*)_pixelBuffer);
                    _pixelBuffer = 0;
                    _pixelCapacity = 0;
                }

                if (_configBuffer != 0)
                {
                    NativeMemory.Free((void*)_configBuffer);
                    _configBuffer = 0;
                    _configLength = 0;
                }
            }

            private void EnsurePixelCapacity(nuint count)
            {
                if (count == 0)
                {
                    if (_pixelBuffer != 0)
                    {
                        NativeMemory.Free((void*)_pixelBuffer);
                        _pixelBuffer = 0;
                        _pixelCapacity = 0;
                    }

                    return;
                }

                if (_pixelCapacity >= count && _pixelBuffer != 0)
                {
                    return;
                }

                if (_pixelBuffer != 0)
                {
                    NativeMemory.Free((void*)_pixelBuffer);
                }

                nuint byteSize = count * (nuint)sizeof(pixel_t);
                _pixelBuffer = (nint)NativeMemory.Alloc(byteSize);
                _pixelCapacity = count;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct pixel_t
        {
            public byte r;
            public byte g;
            public byte b;
            public byte a;

            public static pixel_t FromPixel(Pixel pixel)
            {
                return new pixel_t
                {
                    r = pixel.R,
                    g = pixel.G,
                    b = pixel.B,
                    a = pixel.A,
                };
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct tilepos_t
        {
            public int i;
            public int x;
            public int y;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct tilefmt_t
        {
            public uint w;
            public uint h;
            public byte bpp;
            public uint nbytes;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct tilecfg_t
        {
            public uint start;
            public uint size;
            public ushort nrow;
            public tilefmt_t fmt;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct tile_decoder_t
        {
            public uint version;
            public uint size;
            public void* context;
            public byte* msg;
            public delegate* unmanaged[Cdecl]<byte*, void**, PluginStatus> open;
            public delegate* unmanaged[Cdecl]<void*, PluginStatus> close;
            public delegate* unmanaged[Cdecl]<void*, byte*, nuint, tilepos_t*, tilefmt_t*, pixel_t*, byte, PluginStatus> decodeone;
            public delegate* unmanaged[Cdecl]<void*, byte*, nuint, tilefmt_t*, pixel_t**, nuint*, byte, PluginStatus> decodeall;
            public delegate* unmanaged[Cdecl]<void*, byte*, nuint, tilecfg_t*, PluginStatus> pre;
            public delegate* unmanaged[Cdecl]<void*, byte*, nuint, tilecfg_t*, PluginStatus> post;
            public delegate* unmanaged[Cdecl]<void*, byte**, nuint*, PluginStatus> sendui;
            public delegate* unmanaged[Cdecl]<void*, byte*, nuint, PluginStatus> recvui;
        }
    }
}
