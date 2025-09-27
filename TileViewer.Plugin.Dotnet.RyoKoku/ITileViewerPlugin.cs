using System;
using System.Collections.Generic;

namespace TileViewer.ManagedPlugin
{
    /// <summary>
    /// High level managed interface that mirrors the native plugin entry points exposed by TileViewer.
    /// </summary>
    public interface ITileViewerPlugin
    {
        string CfgJson { get; }
        uint Version { get; }
        bool IsDecodeAll { get; }

        void SetPluginName(string name);
        void Log(string message);
        void ApplyOptions(IReadOnlyList<PluginOption> options);

        PluginStatus DecodeOpen(TileDecodeOpenRequest request);
        PluginStatus DecodeClose(TileDecodeCloseRequest request);
        PluginStatus DecodeOne(TileDecodeOneRequest request, out Pixel pixel);
        PluginStatus DecodeAll(TileDecodeAllRequest request);
        PluginStatus DecodePre(TileDecodePipelineRequest request);
        PluginStatus DecodePost(TileDecodePipelineRequest request);

        bool TryDequeueLog(out string message);

        TileDecodeOpenRequest CreateOpenRequest(string sourceName);
        TileDecodeCloseRequest CreateCloseRequest();
        TileDecodePipelineRequest CreatePipelineRequest(ReadOnlyMemory<byte> data, TileConfiguration configuration);
        TileDecodeOneRequest CreateDecodeOneRequest(ReadOnlyMemory<byte> data, TileFormat format, TilePosition position, bool keepOriginalIndex);
        TileDecodeAllRequest CreateDecodeAllRequest(ReadOnlyMemory<byte> data, TileFormat format, int pixelCount, bool keepOriginalIndex);
    }
}
