using KeyLoad.Core;
using KeyLoad.Replication;

namespace KeyLoad.Orleans;

internal static class ReplicaAppendClassification
{
    internal static ReplicaReplayPool Classify(ReplicaInspectedValue<AppendRequest> inspected, ReplicaConfiguration configuration,
        int maximumControlPayloadBytes, bool requireEmpty, DatabaseEngine? canonicalDatabase)
    {
        const int TermValidationBoundary = 0;
        const int PreviousIndexValidationBoundary = 0;
        const int PreviousTermValidationBoundary = 0;
        const int CommittedIndexValidationBoundary = 0;
        const int EmptyPreviousIndex = 0;
        const int EmptyPreviousTerm = 0;
        const int IndexInitialValue = 0;
        const int RequestPreviousIndexIndexStep = 1;

        var request = inspected.Value;
        var entries = request.Entries;
        ReplicaNativeAdmissionPolicy.Require(request.Term > TermValidationBoundary && request.PreviousIndex >= PreviousIndexValidationBoundary && request.PreviousTerm >= PreviousTermValidationBoundary
            && request.CommittedIndex >= CommittedIndexValidationBoundary && request.PreviousTerm <= request.Term
            && (request.PreviousIndex == EmptyPreviousIndex) == (request.PreviousTerm == EmptyPreviousTerm)
            && !entries.IsDefault && entries.Length <= configuration.MaxAppendEntries);
        ReplicaNativeAdmissionPolicy.Require(inspected.MeasureEntries(entries) <= configuration.MaxAppendBytes);
        var controlOnly = true;
        var previousTerm = request.PreviousTerm;
        for (var index = IndexInitialValue; index < entries.Length; index++)
        {
            var entry = entries[index];
            ReplicaNativeAdmissionPolicy.Require(entry is not null);
            ReplicaNativeAdmissionPolicy.Require(request.PreviousIndex < long.MaxValue - index
                && entry.Index == request.PreviousIndex + index + RequestPreviousIndexIndexStep && entry.Term > TermValidationBoundary
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
