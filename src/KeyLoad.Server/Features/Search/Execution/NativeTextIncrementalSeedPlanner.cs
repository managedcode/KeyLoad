using KeyLoad.Core;
using KeyLoad.Core.Features.Search;
using KeyLoad.Query;
using KeyLoad.Query.Features.Search;

namespace KeyLoad.Server.Features.Search;

internal static class NativeTextIncrementalSeedPlanner
{
    private const ulong RecordStep = 1;

    internal static NativeTextIncrementalSeedPlan Plan(NativeTextSeedCapture seed,
        TextProjectionScope scope, int maximumRecords, ReadExecutionBudget budget,
        QueryExecutionOptions execution)
    {
        budget.Check();
        if (seed.Documents is null || seed.Documents.Length > maximumRecords
            || scope.Incarnation != seed.Incarnation || scope.DataEpoch != seed.DataEpoch
            || scope.ReadGeneration != seed.ReadGeneration
            || scope.Position != seed.Position || scope.PrincipalId != seed.PrincipalId
            || scope.PolicyEpoch != seed.PolicyEpoch || scope.SchemaVersion != seed.SchemaVersion)
        { throw NativeTextErrors.Corrupt(); }
        var records = new List<NativeTextIncrementalRecord>();
        var postings = new List<NativeTextIncrementalPosting[]>();
        var references = new HashSet<EntityRef>();
        var next = NativeTextIncrementalProtocol.InitialRecord;
        foreach (var document in seed.Documents)
        {
            budget.Check();
            if (document.Reference.Partition != scope.Partition
                || document.Reference.Collection != scope.Collection || !references.Add(document.Reference))
            { throw NativeTextErrors.Corrupt(); }
            var record = new NativeTextIncrementalRecord(next, document.Reference, document.Revision,
                document.Deleted, NativeTextIncrementalRevision.Digest(document, budget));
            postings.Add(NativeTextIncrementalTokens.Capture(document, scope.Field, next, budget, execution));
            records.Add(record);
            next += RecordStep;
        }
        budget.Check();
        return new(records.ToArray(), postings.ToArray(), next);
    }
}
