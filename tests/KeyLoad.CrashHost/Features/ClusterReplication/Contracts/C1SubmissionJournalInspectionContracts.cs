using System.Text.Json.Serialization;

namespace KeyLoad.CrashHost.Features.ClusterReplication;

internal static class C1SubmissionJournalInspectionProtocol
{
    internal const string Mode = "c1-submission-journal-inspect";
    internal const int Version = 1;
    internal const int MaximumRequestBytes = 8192;
    internal const int MaximumNativeRecordBytes = 8192;
    internal const int MaximumReadBytes = 32768;
    internal const int MaximumReceiptBytes = 32768;
    internal const int MaximumJsonDepth = 4;
    internal const int MaximumMetadataBytes = 1024;
    internal const int RequiredVoters = 3;
    internal const int MaximumVoterBytes = 512;
    internal const string Invalid = "The original native submission journal inspection is invalid.";
    internal static byte[] SerializeRequest(C1SubmissionJournalInspectionRequest request)
        => C1SubmissionJournalInspectionJson.SerializeRequest(request);
    internal static C1SubmissionJournalInspectionReceipt DeserializeReceipt(ReadOnlySpan<byte> bytes)
        => C1SubmissionJournalInspectionJson.ReadReceipt(bytes);
}

internal sealed record C1SubmissionJournalInspectionRequest(
    [property: JsonRequired] int Version,
    [property: JsonRequired] string NodeDirectory,
    [property: JsonRequired] Guid ExpectedCanonicalNodeId,
    [property: JsonRequired] Guid Incarnation,
    [property: JsonRequired] string LocalVoter,
    [property: JsonRequired] string[] Voters,
    [property: JsonRequired] long EntryIndex,
    [property: JsonRequired] long EntryTerm)
{
    public override string ToString() => nameof(C1SubmissionJournalInspectionRequest);
}

internal sealed record C1SubmissionJournalInspectionReceipt(
    [property: JsonRequired] int Version,
    [property: JsonRequired] Guid CanonicalNodeId,
    [property: JsonRequired] Guid ReplicaNodeId,
    [property: JsonRequired] Guid Incarnation,
    [property: JsonRequired] long CanonicalPosition,
    [property: JsonRequired] long ReplicaPosition,
    [property: JsonRequired] byte[] NativeHardState,
    [property: JsonRequired] byte[] NativeEntry)
{
    public override string ToString() => nameof(C1SubmissionJournalInspectionReceipt);
}
