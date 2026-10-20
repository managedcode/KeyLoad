using KeyLoad.Core.Features.Search;
using KeyLoad.Query.Features.QueryExecution;
using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes SearchExecution operations against the caller's database context.</summary>
public static class SearchExecution
{
    internal static Bm25CorpusPublicationPhaseCommand CaptureBm25CorpusMetadata(this DatabaseEngine engine, IKeyValueView raw, PrincipalRecord principal, ResourceDefinition resource, SearchRequest request, DistributedTextStatistics statistics, string tokenizer, Guid commandId, Guid? expectedGeneration, DateTimeOffset originalExpiry, ReadExecutionBudget work)
    {
        var view = work.CreateView(raw);
        var placement = engine.ReadPlacementWitness(view, request.Partition);
        var appliedBytes = view.ReadOwnedValue(KeySpace.AppliedBytes) ?? throw Errors.Fail(ErrorCode.Corruption, Bm25CorpusProtocol.Invalid);
        var identity = engine.Store.Identity;
        var current = new Bm25CorpusCurrent(Bm25CorpusProtocol.Format, request.Partition, request.Collection, request.TextField!, tokenizer, principal.Id, principal.PolicyEpoch, resource.SchemaVersion, Bm25CorpusDigest.Resource(resource, work), new(identity.NodeId, identity.Incarnation), engine.ReadOutboxHead(view, request.Partition).Tail, engine.Store.Position, OrleansSerialization.Deserialize<long>(appliedBytes), identity.ReadGeneration, identity.Incarnation, statistics, commandId, string.Empty);
        current = current with
        {
            PayloadDigest = Bm25CorpusDigest.Compute(current, placement, work)
        };
        var phase = new Bm25CorpusPublicationPhaseCommand(commandId, current, expectedGeneration, originalExpiry, placement);
        Bm25CorpusShape.RequirePhase(phase, engine.Limits);
        Bm25CorpusFamilyAdmission.Require(view, Bm25CorpusKeys.Current(current), current, engine.Limits);
        return phase;
    }
}
