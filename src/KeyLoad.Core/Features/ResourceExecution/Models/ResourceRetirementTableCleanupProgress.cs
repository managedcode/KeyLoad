namespace KeyLoad.Core.Features.ResourceExecution;

/// <summary>Reports actual logical deletion work; it does not claim physical erasure.</summary>
internal sealed record ResourceRetirementTableCleanupProgress(
    ResourceRetirementTableCleanupCursor Next, int DeletedRecords, long ExaminedBytes,
    long DeletedBytes, bool HasMore)
{
    internal int ExaminedRecords
    {
        get; init;
    }
}
