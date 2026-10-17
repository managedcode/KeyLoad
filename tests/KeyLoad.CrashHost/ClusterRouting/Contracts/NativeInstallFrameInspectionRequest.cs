using System.Text.Json.Serialization;

namespace KeyLoad.CrashHost.Features.ClusterRouting;

internal sealed record NativeInstallFrameInspectionRequest(
    [property: JsonRequired] int Version,
    [property: JsonRequired] string Directory,
    [property: JsonRequired] Guid ExpectedNodeId,
    [property: JsonRequired] Guid Incarnation,
    [property: JsonRequired] string PrincipalId,
    [property: JsonRequired] Guid CommandId,
    [property: JsonRequired] PartitionRef Partition,
    [property: JsonRequired] int MaximumFrameBytes)
{
    public override string ToString() => nameof(NativeInstallFrameInspectionRequest);
}
