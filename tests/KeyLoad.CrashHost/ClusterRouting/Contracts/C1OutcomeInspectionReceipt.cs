using System.Text.Json.Serialization;

namespace KeyLoad.CrashHost.Features.ClusterRouting;

internal sealed record C1OutcomeInspectionReceipt(
    [property: JsonRequired] int Version,
    [property: JsonRequired] Guid NodeId,
    [property: JsonRequired] Guid Incarnation,
    [property: JsonRequired] int FormatVersion,
    [property: JsonRequired] long Position,
    [property: JsonRequired] bool OutcomePresent)
{
    public override string ToString() => nameof(C1OutcomeInspectionReceipt);
}
