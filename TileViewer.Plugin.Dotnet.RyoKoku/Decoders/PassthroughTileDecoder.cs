using System;
using System.Collections.Generic;
using System.Text.Json;

namespace TileViewer.ManagedPlugin.Decoders
{
    /// <summary>
    /// Minimal managed decoder that interprets the incoming buffer as raw 32-bit RGBA pixels.
    /// </summary>
    public sealed class PassthroughTileDecoder : IConfigurableTileDecoder
    {
        private bool _opened;

        public bool SupportsDecodeAll => true;

        public string DefaultPluginName => "Managed Passthrough Decoder";

        public string DisplayVersion => "v1.0.0";

        public string Description => "Decodes raw RGBA tiles without modification.";

        public uint RequiredTileViewerVersion => 400;

        public PluginStatus Open(TileDecodeOpenRequest request)
        {
            _opened = true;
            return PluginStatus.Ok;
        }

        public PluginStatus Close(TileDecodeCloseRequest request)
        {
            _opened = false;
            return PluginStatus.Ok;
        }

        public PluginStatus DecodeOne(TileDecodeOneRequest request, out Pixel pixel)
        {
            if (!_opened)
            {
                pixel = default;
                return PluginStatus.OpenError;
            }

            if (request.Data.IsEmpty)
            {
                pixel = default;
                return PluginStatus.FormatError;
            }

            int bytesPerPixel = Math.Max(1, request.Format.BitsPerPixel / 8);
            int tileIndexOffset = request.Position.TileIndex * (int)request.Format.BytesPerTile;
            int pixelOffset = (request.Position.Y * (int)request.Format.Width + request.Position.X) * bytesPerPixel;
            int offset = tileIndexOffset + pixelOffset;

            if (offset < 0 || offset + bytesPerPixel > request.Data.Length)
            {
                pixel = default;
                return PluginStatus.RangeError;
            }

            ReadOnlySpan<byte> slice = request.Data.Span[offset..(offset + bytesPerPixel)];
            uint value = bytesPerPixel switch
            {
                4 => BitConverter.ToUInt32(slice),
                3 => (uint)(slice[0] | (slice[1] << 8) | (slice[2] << 16) | (0xFF << 24)),
                2 => (uint)(slice[0] | (slice[1] << 8) | (0xFF << 16) | (0xFF << 24)),
                1 => (uint)(slice[0] | (slice[0] << 8) | (slice[0] << 16) | (0xFF << 24)),
                _ => 0,
            };

            pixel = Pixel.FromArgb(value);
            return PluginStatus.Ok;
        }

        public PluginStatus DecodeAll(TileDecodeAllRequest request)
        {
            if (!_opened)
            {
                return PluginStatus.OpenError;
            }

            int bytesPerPixel = Math.Max(1, request.Format.BitsPerPixel / 8);
            int totalPixels = checked((int)(request.Data.Length / bytesPerPixel));

            Span<Pixel> destination = request.Destination.Memory.Span;
            if (destination.Length < totalPixels)
            {
                return PluginStatus.RangeError;
            }

            ReadOnlySpan<byte> source = request.Data.Span;
            for (int i = 0; i < totalPixels; i++)
            {
                ReadOnlySpan<byte> pixelBytes = source.Slice(i * bytesPerPixel, bytesPerPixel);
                uint value = bytesPerPixel switch
                {
                    4 => BitConverter.ToUInt32(pixelBytes),
                    3 => (uint)(pixelBytes[0] | (pixelBytes[1] << 8) | (pixelBytes[2] << 16) | (0xFF << 24)),
                    2 => (uint)(pixelBytes[0] | (pixelBytes[1] << 8) | (0xFF << 16) | (0xFF << 24)),
                    1 => (uint)(pixelBytes[0] | (pixelBytes[0] << 8) | (pixelBytes[0] << 16) | (0xFF << 24)),
                    _ => 0,
                };

                destination[i] = Pixel.FromArgb(value);
            }

            return PluginStatus.Ok;
        }

        public PluginStatus Preprocess(TileDecodePipelineRequest request)
        {
            return PluginStatus.Ok;
        }

        public PluginStatus Postprocess(TileDecodePipelineRequest request)
        {
            return PluginStatus.Ok;
        }

        public string BuildConfigurationJson(string pluginName)
        {
            PluginConfiguration configuration = new()
            {
                Name = string.IsNullOrWhiteSpace(pluginName) ? DefaultPluginName : pluginName,
                Description = Description,
                Version = DisplayVersion,
                RequiredTileViewerVersion = RequiredTileViewerVersion,
                Options = new List<PluginOption>()
            };

            return JsonSerializer.Serialize(configuration, PluginConfigurationJsonContext.Default.PluginConfiguration);
        }

        public void ApplyOptions(IReadOnlyList<PluginOption> options, Action<string> log)
        {
            // No options to apply for the passthrough decoder.
        }
    }
}
