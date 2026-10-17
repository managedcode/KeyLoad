using System.Text.Json.Serialization;

namespace KeyLoad.CrashHost.Features.ClusterRouting;

internal sealed record NativeInstallFrameInspectionReceipt(
    [property: JsonRequired] int Version,
    [property: JsonRequired] Guid NodeId,
    [property: JsonRequired] Guid Incarnation,
    [property: JsonRequired] Guid CommandId,
    [property: JsonRequired] int FormatVersion,
    [property: JsonRequired] long FrameSequence,
    [property: JsonRequired] int PayloadBytes,
    [property: JsonRequired] string PayloadSha256,
    [property: JsonRequired] int MutationCount,
    [property: JsonRequired] long RawMutationBytes,
    [property: JsonRequired] long ExaminedFrames,
    [property: JsonRequired] long ExaminedBytes,
    [property: JsonRequired] int MaximumObservedPayloadBytes,
    [property: JsonRequired] int OutcomeError,
    [property: JsonRequired] int OutcomeStage,
    [property: JsonRequired] bool Installed,
    [property: JsonRequired] bool EncodedFrameLimitRejected)
{
    public override string ToString() => nameof(NativeInstallFrameInspectionReceipt);
}
