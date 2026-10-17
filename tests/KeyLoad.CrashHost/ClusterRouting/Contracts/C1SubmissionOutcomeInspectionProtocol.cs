namespace KeyLoad.CrashHost.Features.ClusterRouting;

internal static class C1SubmissionOutcomeInspectionProtocol
{
    internal const string Mode = "c1-submission-outcome-inspect";
    internal const int Version = 1;
    internal const int MaximumRequestBytes = 8192;
    internal const int MaximumNativeRecordBytes = 8192;
    internal const int MaximumReadBytes = 32768;
    internal const int MaximumReceiptBytes = 32768;
    internal const int MaximumJsonDepth = 4;
    internal const int MaximumMetadataBytes = 1024;
    internal const string PrincipalPrefix = "c1-probe-";
    internal const string Invalid = "The full native submission outcome inspection is invalid.";

    internal static byte[] SerializeRequest(C1SubmissionOutcomeInspectionRequest request)
        => C1SubmissionOutcomeInspectionJson.SerializeRequest(request);
    internal static C1SubmissionOutcomeInspectionReceipt DeserializeReceipt(ReadOnlySpan<byte> bytes)
        => C1SubmissionOutcomeInspectionJson.ReadReceipt(bytes);
}
