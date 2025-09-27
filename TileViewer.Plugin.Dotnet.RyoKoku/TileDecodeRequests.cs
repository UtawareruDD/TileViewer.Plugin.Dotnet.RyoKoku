using System;
using System.Buffers;

namespace TileViewer.ManagedPlugin
{
    /// <summary>
    /// Base type for decode requests that provides shared metadata.
    /// </summary>
    public abstract class TileDecodeRequest
    {
        protected TileDecodeRequest(string pluginName)
        {
            PluginName = pluginName;
        }

        public string PluginName { get; }
    }

    public sealed class TileDecodeOpenRequest : TileDecodeRequest
    {
        public TileDecodeOpenRequest(string pluginName, string sourceName)
            : base(pluginName)
        {
            SourceName = sourceName;
        }

        public string SourceName { get; }
    }

    public sealed class TileDecodeCloseRequest : TileDecodeRequest
    {
        public TileDecodeCloseRequest(string pluginName)
            : base(pluginName)
        {
        }
    }

    public sealed class TileDecodePipelineRequest : TileDecodeRequest
    {
        public TileDecodePipelineRequest(string pluginName, ReadOnlyMemory<byte> rawData, TileConfiguration configuration)
            : base(pluginName)
        {
            RawData = rawData;
            Configuration = configuration;
        }

        public ReadOnlyMemory<byte> RawData { get; }
        public TileConfiguration Configuration { get; }
    }

    public sealed class TileDecodeOneRequest : TileDecodeRequest
    {
        public TileDecodeOneRequest(
            string pluginName,
            ReadOnlyMemory<byte> data,
            TileFormat format,
            TilePosition position,
            bool keepOriginalIndex)
            : base(pluginName)
        {
            Data = data;
            Format = format;
            Position = position;
            KeepOriginalIndex = keepOriginalIndex;
        }

        public ReadOnlyMemory<byte> Data { get; }
        public TileFormat Format { get; }
        public TilePosition Position { get; }
        public bool KeepOriginalIndex { get; }
    }

    public sealed class TileDecodeAllRequest : TileDecodeRequest, IDisposable
    {
        private bool _disposed;

        public TileDecodeAllRequest(
            string pluginName,
            ReadOnlyMemory<byte> data,
            TileFormat format,
            IMemoryOwner<Pixel> destination,
            bool keepOriginalIndex)
            : base(pluginName)
        {
            Data = data;
            Format = format;
            Destination = destination;
            KeepOriginalIndex = keepOriginalIndex;
        }

        public ReadOnlyMemory<byte> Data { get; }
        public TileFormat Format { get; }
        public IMemoryOwner<Pixel> Destination { get; }
        public bool KeepOriginalIndex { get; }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            PixelBufferPool.Return(Destination);
        }
    }
}
