using Microsoft.Extensions.Options;

namespace KeyLoad.Query.Features.Search;

internal sealed class PackedAnnIndex
{
    private readonly PackedAnnState state;

    private PackedAnnIndex(PackedAnnState state) => this.state = state;

    internal VectorSpace Space => state.Space;
    internal int Count => state.Count;
    internal long RetainedBytesUpperBound => state.RetainedBytesUpperBound;
    internal long BuildScratchBytesUpperBound => state.BuildScratchBytesUpperBound;

    internal static PackedAnnIndex Build(VectorSpace space, IReadOnlyList<VectorRecord> records,
        IOptions<PackedAnnOptions> options, AnnWorkBudget budget)
        => new(PackedAnnBuilder.Build(space, records, options, budget));

    internal AnnSearchResult Search(ReadOnlyMemory<float> query, int limit, ReadOnlyMemory<ulong>? eligibility,
        AnnWorkBudget budget)
        => PackedAnnSearch.Run(state, query, limit, eligibility, budget);
}

internal sealed class PackedAnnState
{
    internal PackedAnnState(VectorSpace space, string[] ids, long[] revisions, byte[] levels,
        PackedAnnGraph graph, PackedAnnVectors vectors, int entryPoint, int maximumLevel,
        IOptions<PackedAnnOptions> options, long retainedBytesUpperBound, long buildScratchBytesUpperBound)
    {
        Space = space;
        Ids = ids;
        Revisions = revisions;
        Levels = levels;
        Graph = graph;
        Vectors = vectors;
        EntryPoint = entryPoint;
        MaximumLevel = maximumLevel;
        policy = options.Value;
        policy.Validate();
        RetainedBytesUpperBound = retainedBytesUpperBound;
        BuildScratchBytesUpperBound = buildScratchBytesUpperBound;
    }

    internal VectorSpace Space { get; }
    internal string[] Ids { get; }
    internal long[] Revisions { get; }
    internal byte[] Levels { get; }
    internal PackedAnnGraph Graph { get; }
    internal PackedAnnVectors Vectors { get; }
    internal int EntryPoint { get; }
    internal int MaximumLevel { get; }
    private readonly PackedAnnOptions policy;
    internal PackedAnnOptions Options => policy;
    internal int Count => Ids.Length;
    internal long RetainedBytesUpperBound { get; }
    internal long BuildScratchBytesUpperBound { get; }
}
