using KeyLoad.Core;
using KeyLoad.Replication;

namespace KeyLoad.Orleans;

internal static class ReplicaAppendClassification
{
    internal static ReplicaReplayPool Classify(ReplicaInspectedValue<AppendRequest> inspected, ReplicaConfiguration configuration,
        int maximumControlPayloadBytes, bool requireEmpty, DatabaseEngine? canonicalDatabase)
    {
        var request = inspected.Value;
        var entries = request.Entries;
        ReplicaNativeAdmissionPolicy.Require(request.Term > 0 && request.PreviousIndex >= 0 && request.PreviousTerm >= 0
            && request.CommittedIndex >= 0 && request.PreviousTerm <= request.Term
            && (request.PreviousIndex == 0) == (request.PreviousTerm == 0)
            && !entries.IsDefault && entries.Length <= configuration.MaxAppendEntries);
        ReplicaNativeAdmissionPolicy.Require(inspected.MeasureEntries(entries) <= configuration.MaxAppendBytes);
        var controlOnly = true;
        var previousTerm = request.PreviousTerm;
        for (var index = 0; index < entries.Length; index++)
        {
            var entry = entries[index];
            ReplicaNativeAdmissionPolicy.Require(entry is not null);
            ReplicaNativeAdmissionPolicy.Require(request.PreviousIndex < long.MaxValue - index
                && entry.Index == request.PreviousIndex + index + 1 && entry.Term > 0
                && entry.Term >= previousTerm && entry.Term <= request.Term);
            previousTerm = entry.Term;
            if (entry.Operation is not null)
            {
                var control = ReplicaNativeOperationAdmission.Validate(inspected, entry.Operation, canonicalDatabase, maximumControlPayloadBytes);
                controlOnly = controlOnly && control;
            }
        }
        if (requireEmpty)
        {
            ReplicaNativeAdmissionPolicy.Require(entries.IsEmpty && request.PreviousIndex < long.MaxValue);
            return ReplicaReplayPool.ReadBarrier;
        }
        return controlOnly ? ReplicaReplayPool.Critical : ReplicaReplayPool.DataAppend;
    }
}
