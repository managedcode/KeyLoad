using System.Text.Json.Serialization;

namespace KeyLoad.CrashHost.Features.ClusterRouting;

internal sealed record C1SubmissionOutcomeInspectionReceipt(
    [property: JsonRequired] int Version,
    [property: JsonRequired] Guid NodeId,
    [property: JsonRequired] Guid Incarnation,
    [property: JsonRequired] int FormatVersion,
    [property: JsonRequired] long Position,
    [property: JsonRequired] byte[] OutcomeKey,
    [property: JsonRequired] byte[] NativeStoredOutcome)
{
    public override string ToString() => nameof(C1SubmissionOutcomeInspectionReceipt);
}
