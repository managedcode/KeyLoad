using System.Text.Json.Serialization;

namespace KeyLoad.CrashHost.Features.ClusterRouting;

internal static class C1OutcomeInspectionProtocol
{
    internal const string Mode = "c1-outcome-inspect";
    internal const int Version = 2;
    internal const int MaximumRequestBytes = 8_192;
    internal const int MaximumReceiptBytes = 4_096;
    internal const int MaximumJsonDepth = 4;
    internal const int MaximumPrincipalBytes = 256;
    internal const int MaximumPartitionComponentBytes = 256;
    internal const int FailureExitCode = 2;
    internal const string InvalidRequest = "The C1 outcome inspection request is invalid.";
    internal const string InvalidReceipt = "The C1 outcome inspection receipt is invalid.";

    internal static byte[] SerializeRequest(C1OutcomeInspectionRequest request)
        => C1OutcomeInspectionJson.SerializeRequest(request);

    internal static C1OutcomeInspectionReceipt DeserializeReceipt(ReadOnlySpan<byte> bytes)
        => C1OutcomeInspectionJson.ReadReceipt(bytes);
}

internal sealed record C1OutcomeInspectionRequest(
    [property: JsonRequired] int Version,
    [property: JsonRequired] string Directory,
    [property: JsonRequired] Guid ExpectedNodeId,
    [property: JsonRequired] Guid Incarnation,
    [property: JsonRequired] string PrincipalId,
    [property: JsonRequired] Guid CommandId,
    [property: JsonRequired] PartitionRef Partition)
{
    public override string ToString() => nameof(C1OutcomeInspectionRequest);
}

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
