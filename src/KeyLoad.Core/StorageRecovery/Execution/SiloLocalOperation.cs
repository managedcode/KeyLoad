using KeyLoad.Storage;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const string LocalProjectionEffectsUnsupported = "Silo-local projection checkpoints cannot publish canonical database mutations.";

    /// <summary>Classifies native node-bound derived work using its typed operation and persisted consumer definition.</summary>
    /// <param name="operation">The original Core-issued operation.</param>
    /// <returns>Whether the operation belongs only to the actual local silo's derived files.</returns>
    public bool IsSiloLocalOperation(Command<DatabaseCommand> operation)
    {
        var verified = OwnCommand(operation);
        return verified.Value!.Kind switch
        {
            OperationKind.Bm25CorpusPublicationPhase or OperationKind.OnlineTextPublicationPhase => true,
            OperationKind.ConfigureProjectionConsumer => IsSiloLocalConsumer(
                Payload<ConfigureProjectionConsumerRequest>(verified).Definition),
            OperationKind.CommitProjectionBatch or OperationKind.ReleaseProjectionConsumer
                => Store.Read(view => IsSiloLocalProjection(view, verified)),
            _ => false
        };
    }

    private bool IsSiloLocalProjection(IKeyValueView view, Command<DatabaseCommand> operation)
    {
        var principal = Principal(view, operation.UserId!, Clock.GetUtcNow());
        AuthorizeOperation(view, principal, operation);
        return operation.Value!.Kind == OperationKind.CommitProjectionBatch
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
