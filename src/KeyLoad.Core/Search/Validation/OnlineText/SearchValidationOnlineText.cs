using System.Security.Cryptography;
using KeyLoad.Core.Search;
using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes SearchValidationOnlineText operations against the caller's database context.</summary>
public static class SearchValidationOnlineText
{
    internal static void ValidateOnlineTextCanonicalSource(this DatabaseEngine engine, IKeyValueView view, PrincipalRecord principal, OnlineTextPublicationPhaseCommand command)
    {
        OnlineTextPublicationShape.Require(command);
        var request = command.Request;
        var source = command.Result.PublishedCut;
        var consumer = ProjectionConsumerAuthority.RequireActiveProjectionConsumer(view, principal, request.Consumer, request.ConsumerGeneration);
        if (!consumer.Definition.Resources.SequenceEqual([request.Collection], StringComparer.Ordinal) || !consumer.Definition.MutationKinds.SequenceEqual([MutationDiscriminatorNames.PutDocument, MutationDiscriminatorNames.PatchDocument, MutationDiscriminatorNames.DeleteDocument], StringComparer.Ordinal))
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, OnlineTextPublicationProtocol.InvalidPublication);
        }

        engine.Authorization.Require(principal, request.Consumer.Partition, request.Collection, Capability.Query | Capability.DocumentsRead);
        var resource = engine.Resource(view, request.Consumer.Partition, request.Collection, ResourceKind.Collection);
        engine.Authorization.RequireFieldUse(principal, resource, request.Field);
        engine.ValidateOnlineTextCheckpoint(view, principal, request, command.Result.Checkpoint, null);
        var head = engine.ReadOutboxHead(view, request.Consumer.Partition);
        SearchValidation.RequireTextProjectionHistory(head, consumer.Checkpoint);
        var appliedBytes = view.ReadOwnedValue(KeySpace.AppliedBytes) ?? throw Errors.Fail(ErrorCode.Corruption, OnlineTextPublicationProtocol.InvalidPublication);
        var placement = engine.ReadAtomicPartitionPlacement(view, principal.Id, request.Consumer.Partition);
        var resourceSha = Convert.ToHexStringLower(SHA256.HashData(OrleansSerialization.Serialize(resource)));
        if (command.DataEpoch != engine.Store.Identity.FormatVersion || source.Incarnation != engine.Store.Identity.Incarnation || source.AppliedPosition != OrleansSerialization.Deserialize<long>(appliedBytes) || source.ThroughSequence != head.Tail || consumer.Checkpoint != source.ThroughSequence || source.PolicyEpoch != principal.PolicyEpoch || source.SchemaVersion != resource.SchemaVersion || source.ResourceSha256 != resourceSha || placement.PhysicalShardId != request.Placement.PhysicalShardId || placement.Incarnation != request.Placement.Incarnation)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, OnlineTextPublicationProtocol.InvalidPublication);
        }
    }
}
