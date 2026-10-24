using Microsoft.Extensions.Options;

namespace KeyLoad.Query.Search;

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

    internal VectorSpace Space
    {
        get;
    }
    internal string[] Ids
    {
        get;
    }
    internal long[] Revisions
    {
        get;
    }
    internal byte[] Levels
    {
        get;
    }
    internal PackedAnnGraph Graph
    {
        get;
    }
    internal PackedAnnVectors Vectors
    {
        get;
    }
    internal int EntryPoint
    {
        get;
    }
    internal int MaximumLevel
    {
        get;
    }
    private readonly PackedAnnOptions policy;
    internal PackedAnnOptions Options => policy;
    internal int Count => Ids.Length;
    internal long RetainedBytesUpperBound
    {
        get;
    }
    internal long BuildScratchBytesUpperBound
    {
        get;
    }
}
