namespace KeyLoad.Replication;

/// <summary>Identifies only the original successful first-Admit append after quorum and joined apply.</summary>
[Orleans.GenerateSerializer, Orleans.Alias(EventVectorFirstAdmitAppendResult.SerializerAlias)]
public sealed class EventVectorFirstAdmitAppendResult
{
    internal const string SerializerAlias = "keyload.replication.event-vector-first-admit-append-result.v1";

    internal static EventVectorFirstAdmitAppendResult FromAppliedEntry(ReplicaEntry entry,
        Guid originalParentRequestId, OperationResult originalOutcome) => new()
        {
            OperationId = entry.Operation!.Id,
            OriginalParentRequestId = originalParentRequestId,
            AppendedIndex = entry.Index,
            AppendedTerm = entry.Term,
            OriginalOutcome = originalOutcome
        };

    /// <summary>Gets the exact original operation ID from the appended native entry.</summary>
    [Orleans.Id(0)]
    public Guid OperationId
    {
        get; private init;
    }
    /// <summary>Gets the original request ID verified in the owning admission view.</summary>
    [Orleans.Id(1)]
    public Guid OriginalParentRequestId
    {
        get; private init;
    }
    /// <summary>Gets the actual appended entry index acknowledged and applied by the owner.</summary>
    [Orleans.Id(2)]
    public long AppendedIndex
    {
        get; private init;
    }
    /// <summary>Gets the actual term of that same original appended entry.</summary>
    [Orleans.Id(3)]
    public long AppendedTerm
    {
        get; private init;
    }
    /// <summary>Gets the unchanged genuine outcome resolved after joined apply.</summary>
    [Orleans.Id(4)] public OperationResult OriginalOutcome { get; private init; } = null!;
}
