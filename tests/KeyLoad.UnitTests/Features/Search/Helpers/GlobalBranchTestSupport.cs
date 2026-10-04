using System.Collections.Immutable;
using KeyLoad.Core;
using KeyLoad.Query.Features.Search;

namespace KeyLoad.UnitTests.Features.Search;

internal static class GlobalBranchTestSupport
{
    internal const string Branch = "vector-main";
    internal const string Profile = "profile-v1";
    internal const string Corpus = "tenant-corpus-v1";
    internal const string Statistics = "stats-17";
    internal const string Collection = "documents";
    internal const int Limit = 10;

    internal static GlobalBranchScope Scope(string profile = Profile, string corpus = Corpus,
        string statistics = Statistics) => new(profile, corpus, statistics);

    internal static GlobalBranchMergeRequest Request(ImmutableArray<string> expected, int limit = Limit,
        bool allowIncomplete = false, GlobalBranchKind kind = GlobalBranchKind.Vector,
        GlobalBranchScope? scope = null) => new(Branch, kind, scope ?? Scope(), expected, limit, allowIncomplete);

    internal static GlobalBranchCandidate Candidate(string id, double score, long revision = 1,
        string tenant = "tenant", string database = "database", string domain = "orders",
        string partition = "p1", string collection = Collection)
        => new(new(new(tenant, database, domain, partition), collection, id), revision, score);

    internal static GlobalBranchWindow Window(string sourceId, IEnumerable<GlobalBranchCandidate> candidates,
        bool complete = true, bool approximate = false, bool truncated = false, bool exhaustive = true,
        GlobalBranchKind kind = GlobalBranchKind.Vector, GlobalBranchScope? scope = null)
        => new(Branch, kind, sourceId, scope ?? Scope(), candidates.ToImmutableArray(), complete, approximate,
            truncated, exhaustive);

    internal static GlobalBranchMergeResult Merge(GlobalBranchMergeRequest request,
        ImmutableArray<GlobalBranchWindow> windows, DatabaseLimits? limits = null,
        ReadExecutionBudget? budget = null)
        => GlobalBranchWindowMerger.Merge(request, windows, limits ?? new DatabaseLimits(),
            budget ?? new ReadExecutionBudget(new DatabaseLimits()));

    internal static GlobalBranchCandidate[] IndependentOrder(IEnumerable<GlobalBranchCandidate> candidates)
        => candidates.OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Reference.Partition.TenantId, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.Reference.Partition.DatabaseId, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.Reference.Partition.TransactionDomainId, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.Reference.Partition.PartitionKey, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.Reference.Collection, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.Reference.Id, StringComparer.Ordinal).ToArray();

    internal static long NativeInputBytes(GlobalBranchMergeRequest request,
        ImmutableArray<GlobalBranchWindow> windows)
    {
        var bytes = NativeSerialization.Measure(request);
        foreach (var window in windows)
        {
            bytes = checked(bytes + NativeSerialization.Measure(window with { Candidates = [] }));
            foreach (var candidate in window.Candidates)
            {
                bytes = checked(bytes + NativeSerialization.Measure(candidate));
            }
        }
        return bytes;
    }
}
