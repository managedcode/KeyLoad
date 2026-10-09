using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.CrashHost.Features.ClusterRouting;

/// <summary>Validates the explicit offline inspection frame cap through the native storage policy.</summary>
[ConfigurationBinding]
internal static class NativeInstallFrameInspectionOptions
{
    internal static void ValidateMaximumFrameBytes(int maximumFrameBytes)
        => new ZoneTreeStorageExecutionOptions { MaxFrameBytes = maximumFrameBytes }.Validate();
}
