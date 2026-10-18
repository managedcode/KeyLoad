using KeyLoad.Core.Features.BlobStorage;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Core.Features.Search;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private void ObserveAdditionalResourceEffects(IKeyValueView view, PrincipalRecord principal,
        ReplicatedOperation operation)
    {
        if (BlobStorageOperations.Handles(operation.Kind))
        {
            return;
        }
        switch (operation.Kind)
        {
            case OperationKind.Batch:
                return;
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
                var delivery = Payload<DeliveryCommand>(operation);
                _ = Resource(view, delivery.Lane.Partition, delivery.Lane.Queue, ResourceKind.WorkQueue);
                return;
            case OperationKind.OnlineTextPublicationPhase:
                ValidateOnlineTextCanonicalSource(view, principal, Payload<OnlineTextPublicationPhaseCommand>(operation));
                return;
            default:
                if (ObserveCatalogResourceEffect(view, operation) || ObserveProjectionResourceEffects(view, principal, operation)
                    || IsResourceFreeNativeOperation(operation.Kind))
                {
                    return;
                }
                throw Errors.Fail(ErrorCode.UnsupportedCapability, ResourceEffectAdmissionProtocol.Unbound);
        }
    }
}
