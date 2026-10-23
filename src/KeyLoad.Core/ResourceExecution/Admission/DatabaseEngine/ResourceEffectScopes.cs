using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Core.Features.Search;
using KeyLoad.Storage;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Core;
/// <summary>Executes ResourceEffectScopes operations against the caller's database context.</summary>
public static class ResourceEffectScopes
{
    internal static void ObserveAdditionalResourceEffects(this DatabaseEngine engine, IKeyValueView view, PrincipalRecord principal, Command<DatabaseCommand> operation)
    {
        switch (operation.Value!.Kind)
        {
            case OperationKind.Batch:
                foreach (var mutation in OperationProtocol.Payload<CommandRequest>(operation).Mutations)
                {
                    if (mutation is AdvanceQueueTransferRepair repair)
                    {
                        engine.ObserveRemoteTransferRepairDestination(view, principal, repair);
                    }
                }

                return;
            case OperationKind.FileMetadata:
            case OperationKind.Receive:
            case OperationKind.Processing:
            case OperationKind.CommitInbox:
            case OperationKind.ConfigureSubscription:
            case OperationKind.SeekSubscription:
            case OperationKind.SetSubscriptionPaused:
            case OperationKind.ReceiveSubscription:
            case OperationKind.SubscriptionDelivery:
            case OperationKind.SubscriptionProcessing:
            case OperationKind.Bm25CorpusPublicationPhase:
                return; // The original authorizer resolves every modeled resource through BindResourceDefinition.
            case OperationKind.Delivery:
                var delivery = OperationProtocol.Payload<DeliveryCommand>(operation);
                _ = engine.Resource(view, delivery.Lane.Partition, delivery.Lane.Queue, ResourceKind.WorkQueue);
                return;
            case OperationKind.OnlineTextPublicationPhase:
                engine.ValidateOnlineTextCanonicalSource(view, principal, OperationProtocol.Payload<OnlineTextPublicationPhaseCommand>(operation));
                return;
            default:
                if (ResourceMetadataScopes.ObserveCatalogResourceEffect(view, operation) || engine.ObserveProjectionResourceEffects(view, principal, operation) || ResourceMetadataScopes.IsResourceFreeOperation(operation.Value!.Kind))
                {
                    return;
                }

                throw Errors.Fail(ErrorCode.UnsupportedCapability, ResourceEffectAdmissionProtocol.Unbound);
        }
    }
}
