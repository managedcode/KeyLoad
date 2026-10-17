namespace KeyLoad.Core.Features.ResourceExecution;

/// <summary>Owns an immutable local continuation; the phase envelope persists its copied bytes.</summary>
internal sealed record ResourceRetirementTableCleanupCursor
{
    private readonly byte[] afterKey;

    internal ResourceRetirementTableCleanupCursor(int familyOrdinal, byte[]? afterKey = null)
    {
        FamilyOrdinal = familyOrdinal;
        this.afterKey = afterKey?.ToArray() ?? [];
    }

    internal int FamilyOrdinal
    {
        get;
    }
    internal byte[] AfterKey => afterKey.ToArray();
}
