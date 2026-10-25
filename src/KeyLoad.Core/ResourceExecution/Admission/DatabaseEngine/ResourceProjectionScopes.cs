using KeyLoad.Storage;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Core;
/// <summary>Executes ResourceProjectionScopes operations against the caller's database context.</summary>
public static class ResourceProjectionScopes
{
    internal static bool ObserveProjectionResourceEffects(this DatabaseEngine engine, IKeyValueView view, PrincipalRecord principal, Command<DatabaseCommand> operation)
    {
        switch (operation.Value!.Kind)
        {
            case OperationKind.PurgeOutbox:
                engine.ObservePurgedOutboxResources(view, OperationProtocol.Payload<PurgeOutboxRequest>(operation));
                return true;
            case OperationKind.ConfigureProjectionConsumer:
                var configure = OperationProtocol.Payload<ConfigureProjectionConsumerRequest>(operation);
                engine.ObserveProjectionResources(view, configure.Consumer, configure.Definition);
                return true;
            case OperationKind.CommitProjectionBatch:
                var commit = OperationProtocol.Payload<CommitProjectionBatchRequest>(operation);
                engine.ObserveProjectionResources(view, commit.Consumer, ProjectionConsumerIdentity.ProjectionConsumer(view, commit.Consumer).Definition);
                engine.AuthorizeBatch(view, principal, new(commit.CommandId, commit.Consumer.Partition, commit.Effects), allowEmpty: true);
                return true;
            case OperationKind.ReleaseProjectionConsumer:
                var release = OperationProtocol.Payload<ReleaseProjectionConsumerRequest>(operation);
                engine.ObserveProjectionResources(view, release.Consumer, ProjectionConsumerIdentity.ProjectionConsumer(view, release.Consumer).Definition);
                return true;
            default:
                return false;
        }
    }

    internal static void ObserveProjectionResources(this DatabaseEngine engine, IKeyValueView view, ProjectionConsumerRef consumer, ProjectionConsumerDefinition definition)
    {
        if (definition.Resources.IsDefault)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, CommandAuthorization.MutationCountBudgetMessage);
        }

        foreach (var resource in definition.Resources)
        {
            _ = engine.Resource(view, consumer.Partition, resource);
        }

        if (definition.AnnMaintenance is { } maintenance)
        {
            _ = engine.Resource(view, consumer.Partition, maintenance.Collection, ResourceKind.Collection);
        }
    }
}
