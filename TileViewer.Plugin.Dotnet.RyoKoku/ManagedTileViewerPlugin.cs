using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text.Json;
using TileViewer.ManagedPlugin.Decoders;

namespace TileViewer.ManagedPlugin
{
    /// <summary>
    /// Default managed plugin implementation that exposes a safe API resembling the native TileViewer callbacks.
    /// </summary>
    public sealed class ManagedTileViewerPlugin : ITileViewerPlugin
    {
        private readonly IManagedTileDecoder _decoder;
        private readonly IConfigurableTileDecoder? _configurableDecoder;
        private readonly Queue<string> _logs = new();
        private readonly object _logSync = new();

        private string _pluginName;
        private string _description;
        private string _displayVersion;
        private uint _requiredTileViewerVersion;

        public ManagedTileViewerPlugin() : this(new MainTileDecoder()) { }

        public ManagedTileViewerPlugin(IManagedTileDecoder decoder)
        {
            _decoder = decoder ?? throw new ArgumentNullException(nameof(decoder));
            _configurableDecoder = decoder as IConfigurableTileDecoder;

            if (_configurableDecoder is not null)
            {
                _pluginName = _configurableDecoder.DefaultPluginName;
                _description = _configurableDecoder.Description;
                _displayVersion = _configurableDecoder.DisplayVersion;
                _requiredTileViewerVersion = _configurableDecoder.RequiredTileViewerVersion;
            }
            else
            {
                _pluginName = "Managed Tile Decoder";
                _description = "Managed decoder implementation.";
                _displayVersion = "v1.0.0";
                _requiredTileViewerVersion = 400;
            }
        }

        public string CfgJson => BuildConfigurationJson();

        public uint Version => _requiredTileViewerVersion;

        public bool IsDecodeAll => _decoder.SupportsDecodeAll;

        public void SetPluginName(string name)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            _pluginName = name;
            Log($"Plugin name set to '{name}'.");
        }

        public void Log(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            lock (_logSync)
            {
                _logs.Enqueue(message);
            }
        }

        public PluginStatus DecodeOpen(TileDecodeOpenRequest request)
        {
            return _decoder.Open(request);
        }

        public PluginStatus DecodeClose(TileDecodeCloseRequest request)
        {
            return _decoder.Close(request);
        }

        public PluginStatus DecodeOne(TileDecodeOneRequest request, out Pixel pixel)
        {
            return _decoder.DecodeOne(request, out pixel);
        }

        public PluginStatus DecodeAll(TileDecodeAllRequest request)
        {
            return _decoder.DecodeAll(request);
        }

        public PluginStatus DecodePre(TileDecodePipelineRequest request)
        {
            return _decoder.Preprocess(request);
        }

        public PluginStatus DecodePost(TileDecodePipelineRequest request)
        {
            return _decoder.Postprocess(request);
        }

        public void ApplyOptions(IReadOnlyList<PluginOption> options)
        {
            if (_configurableDecoder is null || options is null)
            {
                return;
            }

            _configurableDecoder.ApplyOptions(options, Log);
        }

        public bool TryDequeueLog(out string message)
        {
            lock (_logSync)
            {
                if (_logs.Count == 0)
                {
                    message = string.Empty;
                    return false;
                }

                message = _logs.Dequeue();
                return true;
            }
        }

        public static ITileViewerPlugin CreateDefault()
        {
            return new ManagedTileViewerPlugin();
        }

        public TileDecodeOpenRequest CreateOpenRequest(string sourceName)
            => new(_pluginName, sourceName);

        public TileDecodeCloseRequest CreateCloseRequest()
            => new(_pluginName);

        public TileDecodePipelineRequest CreatePipelineRequest(ReadOnlyMemory<byte> data, TileConfiguration configuration)
            => new(_pluginName, data, configuration);

        public TileDecodeOneRequest CreateDecodeOneRequest(ReadOnlyMemory<byte> data, TileFormat format, TilePosition position, bool keepOriginalIndex)
            => new(_pluginName, data, format, position, keepOriginalIndex);

        public TileDecodeAllRequest CreateDecodeAllRequest(ReadOnlyMemory<byte> data, TileFormat format, int pixelCount, bool keepOriginalIndex)
        {
            int length = Math.Max(pixelCount, 0);
            IMemoryOwner<Pixel> owner = PixelBufferPool.Rent(length);
            return new TileDecodeAllRequest(_pluginName, data, format, owner, keepOriginalIndex);
        }

        private string BuildConfigurationJson()
        {
            if (_configurableDecoder is not null)
            {
                return _configurableDecoder.BuildConfigurationJson(_pluginName);
            }

            PluginConfiguration configuration = new()
            {
                Name = _pluginName,
                Description = _description,
                Version = _displayVersion,
                RequiredTileViewerVersion = _requiredTileViewerVersion,
                Options = new List<PluginOption>()
            };

            return JsonSerializer.Serialize(configuration, PluginConfigurationJsonContext.Default.PluginConfiguration);
        }
    }
}
