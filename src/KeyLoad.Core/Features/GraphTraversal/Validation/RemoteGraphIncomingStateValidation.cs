using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Core.Features.GraphTraversal;
using KeyLoad.Core.Features.GraphTraversal.Serialization;
using KeyLoad.Core.Features.GraphTraversal.Validation;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private void RequireRemoteGraphIncomingState(IKeyValueView view,
        RemoteGraphReceiverCandidatesRequestV1 request, RemoteGraphReceiverStateV1 state,
        ReadExecutionBudget work)
    {
        if (state?.Content is null || state.Version != RemoteGraphProtocol.Version
            || state.Intent?.Content is null || state.Intent.SourceOwner is null || state.Receipt is null)
        { throw Errors.Fail(ErrorCode.Corruption, RemoteGraphProtocol.Invalid); }
        var content = state.Content;
        GraphCrossPartitionValidation.ValidateReceiver(content, content.SourcePartition, request.Graph,
            content.EdgeId, request.Target);
        RequireRemoteGraphReceiptShape(state.Receipt, ErrorCode.Corruption);
        if (!RemoteGraphIncomingBytesEqual(state.Intent, state.Receipt.Intent, work)
            || state.Intent.Content.SourcePartition != content.SourcePartition
            || state.Intent.Content.Graph != content.Graph || state.Intent.Content.EdgeId != content.EdgeId
            || state.Intent.Content.Destination != content.Destination
            || state.Intent.Content.Revision != content.Revision
            || state.Intent.Content.Deleted != content.Deleted
            || state.Intent.Content.Fingerprint != content.Fingerprint)
        { throw Errors.Fail(ErrorCode.Corruption, RemoteGraphProtocol.Invalid); }
        ValidateCommitToken(view, request.Target.Partition, state.Receipt.TargetReceipt.Token,
            ErrorCode.Corruption, RemoteGraphProtocol.Invalid);
        var catalog = PhysicalShardCatalogRecordSerialization.Read(view)
            ?? throw Errors.Fail(ErrorCode.OwnershipLost, RemoteGraphProtocol.MissingAuthority);
        var directory = ReadRemoteGraphControlDirectory(view, catalog.DefaultShard);
        var registeredSource = directory.Owners.SingleOrDefault(value =>
            PhysicalOwnerEntryValidation.SameOwner(value.Owner, state.Intent.SourceOwner));
        var registeredTarget = directory.Owners.SingleOrDefault(value =>
            PhysicalOwnerEntryValidation.Same(value, state.Intent.DestinationOwner));
        if (registeredSource is null || registeredTarget is null
            || !PhysicalOwnerEntryValidation.SameOwner(catalog.DefaultShard, registeredTarget.Owner)
            || PhysicalOwnerEntryValidation.SameOwner(registeredSource.Owner, registeredTarget.Owner))
        { throw Errors.Fail(ErrorCode.OwnershipLost, RemoteGraphProtocol.MissingAuthority); }
    }
}
