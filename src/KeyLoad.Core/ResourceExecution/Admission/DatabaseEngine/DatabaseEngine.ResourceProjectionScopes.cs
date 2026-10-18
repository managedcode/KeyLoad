using ManagedCode.Communication.Commands;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private bool ObserveProjectionResourceEffects(IKeyValueView view, PrincipalRecord principal,
        Command<DatabaseCommand> operation)
    {
        switch (operation.Value!.Kind)
        {
            case OperationKind.PurgeOutbox:
                ObservePurgedOutboxResources(view, Payload<PurgeOutboxRequest>(operation));
                return true;
            case OperationKind.ConfigureProjectionConsumer:
                var configure = Payload<ConfigureProjectionConsumerRequest>(operation);
                ObserveProjectionResources(view, configure.Consumer, configure.Definition);
                return true;
            case OperationKind.CommitProjectionBatch:
                var commit = Payload<CommitProjectionBatchRequest>(operation);
                ObserveProjectionResources(view, commit.Consumer, ProjectionConsumer(view, commit.Consumer).Definition);
                AuthorizeBatch(view, principal, new(commit.CommandId, commit.Consumer.Partition, commit.Effects), allowEmpty: true);
                return true;
            case OperationKind.ReleaseProjectionConsumer:
                var release = Payload<ReleaseProjectionConsumerRequest>(operation);
                ObserveProjectionResources(view, release.Consumer, ProjectionConsumer(view, release.Consumer).Definition);
                return true;
            default:
                return false;
        }
    }

    private void ObserveProjectionResources(IKeyValueView view, ProjectionConsumerRef consumer,
        ProjectionConsumerDefinition definition)
    {
        if (definition.Resources.IsDefault || definition.Resources.Length > Limits.MaxBatchMutations)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, MutationCountBudgetMessage);
        }
        foreach (var resource in definition.Resources)
        {
            _ = Resource(view, consumer.Partition, resource);
        }
    }
}
