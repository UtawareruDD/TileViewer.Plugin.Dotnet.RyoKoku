using System;
using System.Collections.Generic;

namespace TileViewer.ManagedPlugin
{
    /// <summary>
    /// Strategy interface implemented by concrete decoders.
    /// </summary>
    public interface IManagedTileDecoder
    {
        bool SupportsDecodeAll { get; }

        PluginStatus Open(TileDecodeOpenRequest request);
        PluginStatus Close(TileDecodeCloseRequest request);
        PluginStatus DecodeOne(TileDecodeOneRequest request, out Pixel pixel);
        PluginStatus DecodeAll(TileDecodeAllRequest request);
        PluginStatus Preprocess(TileDecodePipelineRequest request);
        PluginStatus Postprocess(TileDecodePipelineRequest request);
    }

    /// <summary>
    /// Optional interface implemented by decoders that surface configurable options.
    /// </summary>
    public interface IConfigurableTileDecoder : IManagedTileDecoder
    {
        string DefaultPluginName { get; }
        string DisplayVersion { get; }
        string Description { get; }
        uint RequiredTileViewerVersion { get; }

        string BuildConfigurationJson(string pluginName);
        void ApplyOptions(IReadOnlyList<PluginOption> options, Action<string> log);
    }
}
