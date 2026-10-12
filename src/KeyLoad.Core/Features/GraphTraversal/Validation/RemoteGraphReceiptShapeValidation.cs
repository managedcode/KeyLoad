using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Core.Features.GraphTraversal;
using KeyLoad.Core.Features.GraphTraversal.Serialization;
using KeyLoad.Core.Features.GraphTraversal.Validation;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private void RequireRemoteGraphReceiptShape(RemoteGraphReceiptV1 receipt, ErrorCode failure)
    {
        if (receipt?.Intent?.Content is null || receipt.Version != RemoteGraphProtocol.Version
            || receipt.Intent.Version != RemoteGraphProtocol.Version
            || !PhysicalOwnerEntryValidation.Valid(receipt.Intent.DestinationOwner)
            || receipt.TargetReceipt?.Token is null || receipt.TargetReceipt.CommandId == Guid.Empty)
        { throw Errors.Fail(failure, RemoteGraphProtocol.Invalid); }
        var intent = receipt.Intent;
        var content = intent.Content;
        GraphCrossPartitionValidation.ValidateIntent(content, content.SourcePartition, content.Graph,
            content.EdgeId, content.Destination, Limits);
        var token = receipt.TargetReceipt.Token;
        if (token.Incarnation != intent.DestinationOwner.Owner.Incarnation
            || token.AtomicPartitionId != content.Destination.Partition.AtomicPartitionId
            || token.Position < RemoteGraphFirstGeneration || token.OwnershipEpoch < RemoteGraphFirstGeneration
            || receipt.TargetReceipt.Mutations.IsDefault || receipt.TargetReceipt.Mutations.Length != RemoteGraphSinglePreparation)
        { throw Errors.Fail(failure, RemoteGraphProtocol.Invalid); }
        var mutation = receipt.TargetReceipt.Mutations[RemoteGraphFirstPreparation];
        if (mutation is null || mutation.Kind != MutationDiscriminatorNames.ApplyCrossPartitionReverseEdge
            || mutation.Resource != content.Graph || mutation.Id != content.EdgeId || mutation.Revision != content.Revision)
        { throw Errors.Fail(failure, RemoteGraphProtocol.Invalid); }
    }
}
