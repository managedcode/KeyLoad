using KeyLoad.Core.Features.GraphTraversal;
using KeyLoad.Core.Features.GraphTraversal.Serialization;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private OperationResult ExecuteRemoteGraphSource(IAtomicTransaction tx, PrincipalRecord principal,
        ReplicatedOperation operation, long position)
    {
        var command = Payload<RemoteGraphSourceMutationV1>(operation);
        var previous = RequireRemoteGraphSourceFirstRun(tx, command, operation);
        var original = command.Original;
        var graph = original.Upsert?.Graph ?? original.Delete!.Graph;
        var edgeId = original.Upsert?.EdgeId ?? original.Delete!.EdgeId;
        var revision = NextRevision(command.ExpectedOwnerRevision);
        var next = original.Upsert is { } upsert
            ? new EdgeRecord(edgeId, upsert.From, upsert.To, upsert.Label,
                JsonData.Validate(upsert.AttributesJson, Limits), revision) : null;
        var token = Token(tx, original.SourcePartition, position);
        var intents = BuildRemoteGraphSourceIntents(tx, principal, command, previous, next, token, revision);
        var mutation = new MutationReceipt(next is null ? MutationDiscriminatorNames.DeleteEdge
            : MutationDiscriminatorNames.UpsertEdge, graph, edgeId, revision);
        var receipt = new CommitReceipt(operation.Id, token, [mutation], Store.Identity.Durability);
        var complete = new RemoteGraphSourceResultV1(receipt, intents, command);
        if (NativeSerialization.Measure(complete) > Limits.MaxBatchBytes)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, RemoteGraphProtocol.Invalid);
        }
        PersistRemoteGraphSource(tx, original, previous, next, graph, edgeId, revision, intents);
        AppendOutbox(tx, original.SourcePartition, new(RemoteGraphInitialOutboxSequence,
            RemoteGraphInitialMutationIndex, token, operation.EvaluatedAt,
            original.Upsert is { } actual ? actual : original.Delete!, mutation, null, null), false);
        return Result(complete);
    }

    private const long RemoteGraphInitialOutboxSequence = 0;
    private const int RemoteGraphInitialMutationIndex = 0;
}
