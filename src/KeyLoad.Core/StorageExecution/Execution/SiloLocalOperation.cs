using KeyLoad.Storage;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Core;
/// <summary>Executes SiloLocalOperation operations against the caller's database context.</summary>
public static class SiloLocalOperation
{
    internal const string LocalProjectionEffectsUnsupported = "Silo-local projection checkpoints cannot publish canonical database mutations.";
    /// <summary>Classifies execution node-bound derived work using its typed operation and persisted consumer definition.</summary>
    /// <param name = "operation">The original Core-issued operation.</param>
    /// <returns>Whether the operation belongs only to the actual local silo's derived files.</returns>

    /// <param name = "engine">The shared database execution context.</param>
    public static bool IsSiloLocalOperation(this DatabaseEngine engine, Command<DatabaseCommand> operation)
    {
        ArgumentNullException.ThrowIfNull(engine);
        var verified = engine.OwnCommand(operation);
        return verified.Value!.Kind switch
        {
            OperationKind.Bm25CorpusPublicationPhase or OperationKind.OnlineTextPublicationPhase => true,
            OperationKind.ConfigureProjectionConsumer => IsSiloLocalConsumer(OperationProtocol.Payload<ConfigureProjectionConsumerRequest>(verified).Definition),
            OperationKind.CommitProjectionBatch or OperationKind.ReleaseProjectionConsumer => engine.Store.Read(view => engine.IsSiloLocalProjection(view, verified)),
            _ => false
        };
    }

    internal static bool IsSiloLocalProjection(this DatabaseEngine engine, IKeyValueView view, Command<DatabaseCommand> operation)
    {
        var principal = engine.Principal(view, operation.UserId!, engine.Clock.GetUtcNow());
        _ = (engine.AuthorizeOperation(view, principal, operation)).Value;
        return operation.Value!.Kind == OperationKind.CommitProjectionBatch ? IsSiloLocalCheckpoint(view, OperationProtocol.Payload<CommitProjectionBatchRequest>(operation)) : IsSiloLocalConsumer(ProjectionConsumerIdentity.ProjectionConsumer(view, OperationProtocol.Payload<ReleaseProjectionConsumerRequest>(operation).Consumer).Definition);
    }

    internal static bool IsSiloLocalConsumer(ProjectionConsumerDefinition definition) => definition.AnnMaintenance is not null;
    internal static bool IsSiloLocalCheckpoint(IKeyValueView view, CommitProjectionBatchRequest request)
    {
        var local = IsSiloLocalConsumer(ProjectionConsumerIdentity.ProjectionConsumer(view, request.Consumer).Definition);
        if (local && !request.Effects.IsDefaultOrEmpty)
        {
            throw Errors.Fail(ErrorCode.UnsupportedCapability, LocalProjectionEffectsUnsupported);
        }

        return local;
    }
}
