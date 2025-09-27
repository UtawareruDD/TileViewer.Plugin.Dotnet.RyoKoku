namespace TileViewer.ManagedPlugin
{
    /// <summary>
    /// Managed representation of the plugin execution status codes understood by TileViewer.
    /// The numeric values align with the native header so the results remain interoperable.
    /// </summary>
    public enum PluginStatus : uint
    {
        Ok = 0,
        Fail = 1,
        OpenError = 2,
        ScriptError = 3,
        CallbackError = 4,
        FormatError = 5,
        RangeError = 6,
        Unknown = 7
    }
}
