using System.Text;

namespace KeyLoad.CrashHost.Features.ClusterReplication;

internal static class C1SubmissionJournalInspectionValidation
{
    private const int EmptyCount = 0;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    internal static InvalidDataException Invalid() => new(C1SubmissionJournalInspectionProtocol.Invalid);
    internal static C1SubmissionJournalInspectionRequest Request(C1SubmissionJournalInspectionRequest? request)
    {
        if (request is null || request.Version != C1SubmissionJournalInspectionProtocol.Version
            || request.ExpectedCanonicalNodeId == Guid.Empty || request.Incarnation == Guid.Empty
            || request.EntryIndex <= EmptyCount || request.EntryTerm <= EmptyCount
            || request.NodeDirectory is null || !DirectoryValid(request.NodeDirectory)
            || !VoterValid(request.LocalVoter) || request.Voters is null
            || request.Voters.Length != C1SubmissionJournalInspectionProtocol.RequiredVoters
            || request.Voters.Any(voter => !VoterValid(voter))
            || request.Voters.Distinct(StringComparer.Ordinal).Count() != request.Voters.Length
            || !request.Voters.Contains(request.LocalVoter, StringComparer.Ordinal))
        {
            throw Invalid();
        }
        return request;
    }
    internal static C1SubmissionJournalInspectionReceipt Receipt(C1SubmissionJournalInspectionReceipt? receipt)
    {
        if (receipt is null || receipt.Version != C1SubmissionJournalInspectionProtocol.Version
            || receipt.CanonicalNodeId == Guid.Empty || receipt.ReplicaNodeId == Guid.Empty
            || receipt.CanonicalNodeId == receipt.ReplicaNodeId || receipt.Incarnation == Guid.Empty
            || receipt.CanonicalPosition < EmptyCount || receipt.ReplicaPosition < EmptyCount
            || !NativeBytesValid(receipt.NativeHardState) || !NativeBytesValid(receipt.NativeEntry))
        {
            throw Invalid();
        }
        return receipt;
    }
    private static bool NativeBytesValid(byte[]? bytes)
        => bytes is { Length: > EmptyCount and <= C1SubmissionJournalInspectionProtocol.MaximumNativeRecordBytes };
    private static bool DirectoryValid(string directory)
    {
        try
        {
            return Path.IsPathFullyQualified(directory) && string.Equals(Path.GetFullPath(directory), directory,
            StringComparison.Ordinal) && Directory.Exists(directory);
        }
        catch (ArgumentException) { return false; }
        catch (IOException) { return false; }
    }
    private static bool VoterValid(string? voter)
    {
        if (string.IsNullOrWhiteSpace(voter))
        {
            return false;
        }
        try
        {
            return StrictUtf8.GetByteCount(voter) is > EmptyCount and <= C1SubmissionJournalInspectionProtocol.MaximumVoterBytes;
        }
        catch (EncoderFallbackException) { return false; }
    }
}
