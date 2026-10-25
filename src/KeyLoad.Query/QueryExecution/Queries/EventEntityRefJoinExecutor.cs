using System.Collections.Immutable;
using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Core.DocumentStorage;
using KeyLoad.Core.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.Query.QueryExecution;

/// <summary>Reads event reference candidates and execution typed documents through one authorized bounded view.</summary>
internal static class EventEntityRefJoinExecutor
{
    private const string CorruptReference = "The event join selected a document with an inconsistent stored identity.";

    private sealed record Candidate(DocumentRecord Left, DocumentRecord Right);

    private sealed class CandidateComparer : IComparer<Candidate>
    {
        internal static CandidateComparer Instance { get; } = new();

        public int Compare(Candidate? left, Candidate? right)
            => Nullable.Compare(left?.Left.Revision, right?.Left.Revision);
    }

    internal static QueryPage Execute(DatabaseEngine database, IKeyValueView view, PrincipalRecord principal,
        ResourceDefinition streamSet, AstQueryRequest request, ReadExecutionBudget budget)
    {
        var target = EventEntityRefJoinBinding.Bind(database, view, principal, streamSet, request, budget);
        var model = ModelQueryPolicies.Rebase(streamSet);
        var cut = database.Store.Position;
        var candidates = new SortedSet<Candidate>(CandidateComparer.Instance);
        database.VisitModelQueryRows(view, principal, request.Partition, model, request.Query.ModelSource!, budget, false,
            left => Consider(database, view, principal, target, request, budget, candidates, left));
        var rows = ImmutableArray.CreateBuilder<QueryRow>(candidates.Count);
        foreach (var candidate in candidates)
        {
            budget.Check();
            rows.Add(QueueEntityRefJoinProjection.Project(database, principal, model, target,
                request.Query, candidate.Left, candidate.Right));
        }
        var page = new QueryPage(rows.MoveToImmutable(), null, cut, EventEntityRefJoinBinding.AccessPath);
        budget.CheckResult(page);
        return page;
    }

    private static void Consider(DatabaseEngine database, IKeyValueView view, PrincipalRecord principal,
        ResourceDefinition target, AstQueryRequest request, ReadExecutionBudget budget,
        SortedSet<Candidate> candidates, DocumentRecord left)
    {
        budget.Check();
        using var json = JsonDocument.Parse(left.Json);
        var reference = EventEntityRefJoinReference.Read(json.RootElement, request.Partition, target.Name);
        if (request.Query.Filter is not null
            && PredicateEvaluator.Evaluate(request.Query.Filter, left, json.RootElement, request.Parameters) != true)
        {
            return;
        }
        if (reference is null)
        {
            return;
        }
        var lifetime = budget.CaptureResourceLifetimes(view, request.Partition, [target]).Lifetimes.Single();
        reference = EntityReferenceLifetime.Bind(reference, lifetime);
        var right = budget.ReadRecord<DocumentRecord>(view, DocumentStorageKeys.RecordKey(reference));
        if (right is null)
        {
            return;
        }
        if (right.Reference != reference)
        {
            throw Errors.Fail(ErrorCode.Corruption, CorruptReference);
        }
        if (right.Deleted || !database.Authorization.CanReadRow(principal, right.Access))
        {
            return;
        }
        Retain(candidates, request.Query.Limit, new(left, right), budget);
    }

    private static void Retain(SortedSet<Candidate> candidates, int limit, Candidate candidate, ReadExecutionBudget budget)
    {
        budget.Check();
        if (candidates.Count < limit)
        {
            candidates.Add(candidate);
            return;
        }
        var worst = candidates.Max ?? throw Errors.Fail(ErrorCode.Corruption, CorruptReference);
        if (candidate.Left.Revision.CompareTo(worst.Left.Revision) >= 0)
        {
            return;
        }
        candidates.Remove(worst);
        candidates.Add(candidate);
    }
}
