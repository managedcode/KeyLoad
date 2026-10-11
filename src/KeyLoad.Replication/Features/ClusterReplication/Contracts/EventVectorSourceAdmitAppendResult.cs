namespace KeyLoad.Replication;

/// <summary>Identifies only a genuine successful source-Admit append after quorum and joined apply.</summary>
[Orleans.GenerateSerializer, Orleans.Alias(EventVectorSourceAdmitAppendResult.SerializerAlias)]
public sealed class EventVectorSourceAdmitAppendResult
{
    internal const string SerializerAlias = "keyload.replication.event-vector-source-admit-append-result.v1";
    internal static EventVectorSourceAdmitAppendResult FromAppliedEntry(ReplicaEntry entry,
        Guid originalParentRequestId, Guid originalSourcePhaseCommandId, OperationResult originalOutcome) => new()
        {
            OperationId = entry.Operation!.Id,
            OriginalParentRequestId = originalParentRequestId,
            OriginalSourcePhaseCommandId = originalSourcePhaseCommandId,
            AppendedIndex = entry.Index,
            AppendedTerm = entry.Term,
            OriginalOutcome = originalOutcome
        };
    /// <summary>Gets the genuine appended native operation identity.</summary>
    [Orleans.Id(0)] public Guid OperationId { get; private init; }
    /// <summary>Gets the same verified authenticated original parent request identity.</summary>
    [Orleans.Id(1)] public Guid OriginalParentRequestId { get; private init; }
    /// <summary>Gets the exact admitted original source phase identity.</summary>
    [Orleans.Id(2)] public Guid OriginalSourcePhaseCommandId { get; private init; }
    /// <summary>Gets the actual appended entry index acknowledged and applied.</summary>
    [Orleans.Id(3)] public long AppendedIndex { get; private init; }
    /// <summary>Gets the actual term of that same appended entry.</summary>
    [Orleans.Id(4)] public long AppendedTerm { get; private init; }
    /// <summary>Gets the genuine unchanged successful domain outcome.</summary>
    [Orleans.Id(5)] public OperationResult OriginalOutcome { get; private init; } = null!;
}
