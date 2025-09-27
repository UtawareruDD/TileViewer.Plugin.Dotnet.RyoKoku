using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TileViewer.ManagedPlugin
{
    /// <summary>
    /// Describes the JSON metadata expected by TileViewer for managed plugins.
    /// </summary>
    public class PluginConfiguration
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("version")]
        public string Version { get; set; } = "v1.0.0";

        [JsonPropertyName("tileviewerVersion")]
        public uint RequiredTileViewerVersion { get; set; }

        [JsonPropertyName("plugincfg")]
        public List<PluginOption> Options { get; set; } = new();
    }

    /// <summary>
    /// Options that TileViewer can surface as UI widgets.
    /// </summary>
    public class PluginOption
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("options")]
        public List<string> Choices { get; set; } = new();

        [JsonPropertyName("help")]
        public string Help { get; set; } = string.Empty;

        [JsonPropertyName("value")]
        public object Value { get; set; } = string.Empty;
    }

    /// <summary>
    /// Source generation context so the configuration can be serialized when the assembly is AOT compiled.
    /// </summary>
    [JsonSerializable(typeof(PluginConfiguration))]
    [JsonSerializable(typeof(List<PluginOption>))]
    [JsonSerializable(typeof(PluginOption))]
    [JsonSerializable(typeof(List<string>))]
    [JsonSerializable(typeof(object))]
    [JsonSerializable(typeof(string))]
    [JsonSerializable(typeof(bool))]
    [JsonSerializable(typeof(int))]
    internal sealed partial class PluginConfigurationJsonContext : JsonSerializerContext
    {
    }
}
