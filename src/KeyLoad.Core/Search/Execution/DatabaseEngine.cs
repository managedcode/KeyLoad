using KeyLoad.Core.Features.Search;
using KeyLoad.Query.Features.QueryExecution;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal Bm25CorpusPublicationPhaseCommand CaptureBm25CorpusMetadata(IKeyValueView raw,
        PrincipalRecord principal, ResourceDefinition resource, SearchRequest request,
        DistributedTextStatisticsV1 statistics, string tokenizer, Guid commandId,
        Guid? expectedGeneration, DateTimeOffset originalExpiry, ReadExecutionBudget work)
    {
        var view = work.CreateView(raw);
        var placement = ReadPlacementWitness(view, request.Partition);
        var appliedBytes = view.ReadOwnedValue(KeySpace.AppliedBytes)
            ?? throw Errors.Fail(ErrorCode.Corruption, Bm25CorpusProtocol.Invalid);
        var identity = Store.Identity;
        var current = new Bm25CorpusCurrentV1(Bm25CorpusProtocol.Format, request.Partition,
            request.Collection, request.TextField!, tokenizer, principal.Id, principal.PolicyEpoch,
            resource.SchemaVersion, Bm25CorpusDigest.Resource(resource, work), new(identity.NodeId, identity.Incarnation),
            ReadOutboxHead(view, request.Partition).Tail, Store.Position,
            NativeSerialization.Deserialize<long>(appliedBytes), identity.ReadGeneration,
            identity.Incarnation, statistics, commandId, string.Empty);
        current = current with
        {
            PayloadDigest = Bm25CorpusDigest.Compute(current, placement, work)
        };
        var phase = new Bm25CorpusPublicationPhaseCommand(commandId, current, expectedGeneration,
            originalExpiry, placement);
        Bm25CorpusShape.RequirePhase(phase, Limits);
        Bm25CorpusFamilyAdmission.Require(view, Bm25CorpusKeys.Current(current), current, Limits);
        return phase;
    }
}
