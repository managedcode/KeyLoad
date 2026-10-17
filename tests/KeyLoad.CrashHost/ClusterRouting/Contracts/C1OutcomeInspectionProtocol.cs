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
