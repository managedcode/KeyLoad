using KeyLoad.Core;

namespace KeyLoad.Clustering;

/// <summary>Freezes the ordinary factory dispatch subset whose immutable intent has the strict stored-outcome contract.</summary>
internal static class NativeCommandObservationScope
{
    internal static bool IsEligible(DecodedGrainRequest request)
        => request.Envelope.Purpose == GrainNativeContracts.RequestPurpose
            && request.Envelope.CommandKind is OperationKind.ConfigurePlacementPolicy
                or OperationKind.Batch or OperationKind.Receive or OperationKind.Delivery
                or OperationKind.Processing or OperationKind.CommitInbox or OperationKind.ConfigureResource
                or OperationKind.CreateTable or OperationKind.SetQueueDispatch
                or OperationKind.BootstrapPhysicalShardCatalog or OperationKind.BindAtomicPartitionPlacement
                or OperationKind.RegisterPhysicalOwner or OperationKind.ConfigurePrincipal
                or OperationKind.ConfigureApiKey or OperationKind.SetDispatch or OperationKind.RuntimeJournal
                or OperationKind.ConfigureSubscription or OperationKind.SeekSubscription
                or OperationKind.ReceiveSubscription or OperationKind.SubscriptionDelivery
                or OperationKind.SubscriptionProcessing or OperationKind.SetSubscriptionPaused
                or OperationKind.ConfigureProjectionConsumer or OperationKind.ReleaseProjectionConsumer
                or OperationKind.PurgeOutbox or OperationKind.BeginBlobUpload or OperationKind.WriteBlobPart
                or OperationKind.CompleteBlobUpload or OperationKind.AbortBlobUpload
                or OperationKind.DeleteBlob or OperationKind.ReclaimBlob;
}
