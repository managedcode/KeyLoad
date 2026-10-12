using KeyLoad.Core.Features.Search;
using KeyLoad.Query.Features.QueryExecution;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal DistributedTextStatisticsV1? ReadBm25CorpusCurrent(IKeyValueView view, PrincipalRecord principal,
        ResourceDefinition resource, PartitionRef partition, string collection, string field,
        string tokenizer, IReadOnlyList<string> terms, ReadExecutionBudget work)
    {
        Authorization.Require(principal, partition, collection, Capability.Query | Capability.DocumentsRead);
        Authorization.RequireFieldUse(principal, resource, field);
        var bounded = work.CreateView(view);
        var key = Bm25CorpusKeys.Current(partition, collection, field, tokenizer, principal.Id);
        Bm25CorpusCurrentV1? current = null;
        bounded.ReadValue(key, bytes =>
        {
            try
            {
                current = Bm25CorpusShape.RequireRecord(key, bytes, partition, Limits);
            }
            catch (KeyLoadException invalid) when (invalid.Code is ErrorCode.Corruption or ErrorCode.FormatUnsupported)
            { current = null; }
        });
        if (current is null)
        { return null; }
        Bm25CorpusShape.Require(current, Limits);
        if (current.Partition != partition || current.Collection != collection || current.Field != field
            || current.TokenizerVersion != tokenizer || current.PrincipalId != principal.Id)
        { throw Errors.Fail(ErrorCode.Corruption, Bm25CorpusProtocol.Invalid); }
        var placement = ReadPlacementWitness(bounded, partition);
        if (!current.Statistics.Terms.SequenceEqual(terms, StringComparer.Ordinal)
            || !Bm25CorpusDependencies.Matches(this, bounded, principal, resource, current,
                placement, work.MaximumResultBytes, work))
        { return null; }
        work.ChargeBytes(NativeSerialization.Measure(current.Statistics));
        return current.Statistics;
    }
}
