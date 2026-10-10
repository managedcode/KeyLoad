using System.Security.Cryptography;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.Search;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private void ValidateOnlineTextCanonicalSource(IKeyValueView view, PrincipalRecord principal,
        OnlineTextPublicationPhaseCommand command)
    {
        OnlineTextPublicationShape.Require(command, Limits.MaxScanRecords);
        var request = command.Request;
        var source = command.Result.PublishedCut;
        var consumer = RequireActiveProjectionConsumer(view, principal, request.Consumer, request.ConsumerGeneration);
        if (!consumer.Definition.Resources.SequenceEqual([request.Collection], StringComparer.Ordinal)
            || !consumer.Definition.MutationKinds.SequenceEqual([
                MutationDiscriminatorNames.PutDocument, MutationDiscriminatorNames.PatchDocument,
                MutationDiscriminatorNames.DeleteDocument], StringComparer.Ordinal))
        { throw Errors.Fail(ErrorCode.TokenInvalidated, OnlineTextPublicationProtocol.InvalidPublication); }
        Authorization.Require(principal, request.Consumer.Partition, request.Collection,
            Capability.Query | Capability.DocumentsRead);
        var resource = Resource(view, request.Consumer.Partition, request.Collection, ResourceKind.Collection);
        Authorization.RequireFieldUse(principal, resource, request.Field);
        ValidateOnlineTextCheckpoint(view, principal, request, command.Result.Checkpoint, null);
        var head = ReadOutboxHead(view, request.Consumer.Partition);
        RequireNativeTextProjectionHistory(head, consumer.Checkpoint);
        var appliedBytes = view.ReadOwnedValue(KeySpace.AppliedBytes)
            ?? throw Errors.Fail(ErrorCode.Corruption, OnlineTextPublicationProtocol.InvalidPublication);
        var placement = ReadAtomicPartitionPlacement(view, principal.Id,
            new(AtomicPartitionPlacementProtocol.CurrentVersion, request.Consumer.Partition));
        var resourceSha = Convert.ToHexStringLower(SHA256.HashData(NativeSerialization.Serialize(resource)));
        if (command.DataEpoch != Store.Identity.FormatVersion || source.Incarnation != Store.Identity.Incarnation
            || source.AppliedPosition != NativeSerialization.Deserialize<long>(appliedBytes)
            || source.ThroughSequence != head.Tail || consumer.Checkpoint != source.ThroughSequence
            || source.PolicyEpoch != principal.PolicyEpoch || source.SchemaVersion != resource.SchemaVersion
            || source.ResourceSha256 != resourceSha
            || placement.PhysicalShardId != request.Placement.PhysicalShardId
            || placement.Incarnation != request.Placement.Incarnation
            || placement.PlacementEpoch != request.Placement.PlacementEpoch
            || !placement.VoterIds.SequenceEqual(request.Placement.VoterIds, StringComparer.Ordinal))
        { throw Errors.Fail(ErrorCode.OwnershipLost, OnlineTextPublicationProtocol.InvalidPublication); }
    }
}
