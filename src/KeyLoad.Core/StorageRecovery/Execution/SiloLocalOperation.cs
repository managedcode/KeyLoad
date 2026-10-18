using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const string LocalProjectionEffectsUnsupported = "Silo-local projection checkpoints cannot publish canonical database mutations.";

    /// <summary>Classifies native node-bound derived work using its typed operation and persisted consumer definition.</summary>
    /// <param name="operation">The original Core-issued operation.</param>
    /// <returns>Whether the operation belongs only to the actual local silo's derived files.</returns>
    public bool IsSiloLocalOperation(ReplicatedOperation operation)
    {
        var verified = VerifyOperationAuthority(operation);
        var payload = NativeSerialization.Deserialize<NativeCommandPayload>(verified.NativePayload.Span);
        if (payload.Error is not null)
        {
            return false;
        }
        return verified.Kind switch
        {
            OperationKind.Bm25CorpusPublicationPhase or OperationKind.OnlineTextPublicationPhase => true,
            OperationKind.ConfigureProjectionConsumer => IsSiloLocalConsumer(
                Payload<ConfigureProjectionConsumerRequest>(verified).Definition),
            OperationKind.CommitProjectionBatch or OperationKind.ReleaseProjectionConsumer
                => Store.Read(view => IsSiloLocalProjection(view, verified)),
            _ => false
        };
    }

    private bool IsSiloLocalProjection(IKeyValueView view, ReplicatedOperation operation)
    {
        var principal = Principal(view, operation.PrincipalId, Clock.GetUtcNow());
        AuthorizeOperation(view, principal, operation);
        return operation.Kind == OperationKind.CommitProjectionBatch
            ? IsSiloLocalCheckpoint(view, Payload<CommitProjectionBatchRequest>(operation))
            : IsSiloLocalConsumer(ProjectionConsumer(view,
                Payload<ReleaseProjectionConsumerRequest>(operation).Consumer).Definition);
    }

    private static bool IsSiloLocalConsumer(ProjectionConsumerDefinition definition)
        => definition.AnnMaintenance is not null;

    private static bool IsSiloLocalCheckpoint(IKeyValueView view, CommitProjectionBatchRequest request)
    {
        var local = IsSiloLocalConsumer(ProjectionConsumer(view, request.Consumer).Definition);
        if (local && !request.Effects.IsDefaultOrEmpty)
        {
            throw Errors.Fail(ErrorCode.UnsupportedCapability, LocalProjectionEffectsUnsupported);
        }
        return local;
    }
}
