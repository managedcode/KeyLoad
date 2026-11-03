using KeyLoad.Core;
using KeyLoad.Query.Search;
using ZoneTree;
using ZoneTree.FullTextSearch;

namespace KeyLoad.Server.Search;

/// <summary>Owns the ordered canonical records and the posting proof of one bootstrap generation.</summary>
internal sealed class TextGenerationCorpus(TextProjectionScope scope)
{
    private const int FirstOrdinal = 1;

    private readonly List<TextRecord> records = [];
    private HashSet<EntityRef>? references = [];
    private TextPostingProofBuilder? postingBuilder;
    private TextPostingProof? postingProof;
    private byte[]? recordDigest;

    internal IReadOnlyList<TextRecord> Records => records;
    internal TextPostingProof PostingProof => postingProof ?? throw TextErrors.Corrupt();
    internal byte[] RecordDigest => recordDigest ?? throw TextErrors.Corrupt();

    internal void AddRecord(EntityRef reference, long revision)
    {
        if (revision < 0 || reference.Partition != scope.Partition || reference.Collection != scope.Collection
            || string.IsNullOrEmpty(reference.Id) || references?.Add(reference) != true)
        {
            throw TextErrors.Corrupt();
        }
        (postingBuilder ??= new()).BeginRecord();
        records.Add(new((ulong)records.Count + FirstOrdinal, reference, revision));
    }

    internal void ObservePosting(ulong token, ulong record, ulong previousToken)
    {
        if (record == 0 || record != (ulong)records.Count || postingBuilder is not { } builder)
        {
            throw TextErrors.Corrupt();
        }
        builder.Observe(token, record, previousToken);
    }

    internal void Seal(IZoneTree<CompositeKeyOfTokenRecordPrevious<ulong, ulong>, byte> index,
        long maximumDiskBytes, ReadExecutionBudget budget)
    {
        var builder = postingBuilder ??= new();
        postingProof = builder.Seal(index, records.Count, maximumDiskBytes, budget);
        recordDigest = TextRecordDigest.Calculate(records, budget);
        references = null;
    }

    internal void ReleasePostingBuilder()
    {
        postingBuilder?.Dispose();
        postingBuilder = null;
    }
}
