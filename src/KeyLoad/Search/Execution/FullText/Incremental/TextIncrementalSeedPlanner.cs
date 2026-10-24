using KeyLoad.Core;
using KeyLoad.Core.Search;
using KeyLoad.Query;
using KeyLoad.Query.Search;

namespace KeyLoad.Server.Search;

internal static class TextIncrementalSeedPlanner
{
    private const ulong RecordStep = 1;

    internal static TextIncrementalSeedPlan Plan(TextSeedCapture seed,
        TextProjectionScope scope, int maximumRecords, ReadExecutionBudget budget,
        QueryExecutionOptions execution)
    {
        budget.Check();
        if (seed.Documents is null || seed.Documents.Length > maximumRecords
            || scope.Incarnation != seed.Incarnation || scope.DataEpoch != seed.DataEpoch
            || scope.ReadGeneration != seed.ReadGeneration
            || scope.Position != seed.Position || scope.PrincipalId != seed.PrincipalId
            || scope.PolicyEpoch != seed.PolicyEpoch || scope.SchemaVersion != seed.SchemaVersion)
        {
            throw TextErrors.Corrupt();
        }
        var records = new List<TextIncrementalRecord>();
        var postings = new List<TextIncrementalPosting[]>();
        var references = new HashSet<EntityRef>();
        var next = TextIncrementalProtocol.InitialRecord;
        foreach (var document in seed.Documents)
        {
            budget.Check();
            if (document.Reference.Partition != scope.Partition
                || document.Reference.Collection != scope.Collection || !references.Add(document.Reference))
            {
                throw TextErrors.Corrupt();
            }
            var record = new TextIncrementalRecord(next, document.Reference, document.Revision,
                document.Deleted, TextIncrementalRevision.Digest(document, budget));
            postings.Add(TextIncrementalTokens.Capture(document, scope.Field, next, budget, execution));
            records.Add(record);
            next += RecordStep;
        }
        budget.Check();
        return new(records.ToArray(), postings.ToArray(), next);
    }
}
