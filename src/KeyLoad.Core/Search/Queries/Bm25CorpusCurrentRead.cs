using KeyLoad.Core.Search;
using KeyLoad.Query.QueryExecution;
using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes Bm25CorpusCurrentRead operations against the caller's database context.</summary>
public static class Bm25CorpusCurrentRead
{
    internal static global::ManagedCode.Communication.Result<DistributedTextStatistics?> ReadBm25CorpusCurrent(this DatabaseEngine engine, IKeyValueView view, PrincipalRecord principal, ResourceDefinition resource, PartitionRef partition, string collection, string field, string tokenizer, IReadOnlyList<string> terms, ReadExecutionBudget work)
    {
        engine.Authorization.Require(principal, partition, collection, Capability.Query | Capability.DocumentsRead);
        engine.Authorization.RequireFieldUse(principal, resource, field);
        var bounded = work.CreateView(view);
        var key = Bm25CorpusKeys.Current(partition, collection, field, tokenizer, principal.Id);
        Bm25CorpusCurrent? current = null;
        bounded.ReadValue(key, bytes =>
        {
            try
            {
                current = Bm25CorpusShape.RequireRecord(key, bytes, partition);
            }
            catch (KeyLoadException invalid) when (invalid.Code is ErrorCode.Corruption or ErrorCode.FormatUnsupported)
            {
                current = null;
            }
        });
        if (current is null)
        {
            return global::ManagedCode.Communication.Result<DistributedTextStatistics?>.FailNotFound(nameof(ReadBm25CorpusCurrent));
        }

        Bm25CorpusShape.Require(current);
        if (current.Partition != partition || current.Collection != collection || current.Field != field || current.TokenizerVersion != tokenizer || current.PrincipalId != principal.Id)
        {
            throw Errors.Fail(ErrorCode.Corruption, Bm25CorpusProtocol.Invalid);
        }

        var placement = engine.ReadPlacementWitness(bounded, partition);
        if (!current.Statistics.Terms.SequenceEqual(terms, StringComparer.Ordinal) || !Bm25CorpusDependencies.Matches(engine, bounded, principal, resource, current, placement, work))
        {
            return global::ManagedCode.Communication.Result<DistributedTextStatistics?>.FailNotFound(nameof(ReadBm25CorpusCurrent));
        }

        work.ChargeBytes(OrleansSerialization.Measure(current.Statistics));
        return global::ManagedCode.Communication.Result<DistributedTextStatistics?>.Succeed(((DistributedTextStatistics?)(current.Statistics)));
    }
}
