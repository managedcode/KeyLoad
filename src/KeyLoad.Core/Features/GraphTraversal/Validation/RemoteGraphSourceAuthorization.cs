using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.GraphTraversal;
using KeyLoad.Core.Features.GraphTraversal.Serialization;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private AtomicPartitionPlacementResolution? AuthorizeRemoteGraphSource(IKeyValueView view,
        PrincipalRecord principal, ReplicatedOperation operation)
    {
        var command = Payload<RemoteGraphSourceMutationV1>(operation);
        var placement = RequireRemoteGraphSource(view, principal, command);
        var digest = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
            NativeSerialization.Serialize(command.Original)));
        var catalog = PhysicalShardCatalogRecordSerialization.Read(view)
            ?? throw Errors.Fail(ErrorCode.OwnershipLost, RemoteGraphProtocol.MissingAuthority);
        foreach (var preparation in command.Preparations)
        {
            if (preparation?.Preparation is null || string.IsNullOrEmpty(preparation.Token))
            {
                throw Errors.Fail(ErrorCode.Unauthenticated, RemoteGraphProtocol.Invalid);
            }
            var claims = Verify<RemoteGraphPreparationClaimsV1>(preparation.Token);
            if (claims.Purpose != RemoteGraphProtocol.PreparationPurpose || claims.SourcePrincipalId != principal.Id
                || claims.SourcePolicyEpoch != principal.PolicyEpoch || claims.OriginalRequestDigest != digest
                || !NativeSerialization.Serialize(claims.Preparation).AsSpan().SequenceEqual(
                    NativeSerialization.Serialize(preparation.Preparation)))
            {
                throw Errors.Fail(ErrorCode.Unauthenticated, RemoteGraphProtocol.Invalid);
            }
            RequireRemoteGraphPreparation(command.Original, principal, catalog.DefaultShard, preparation.Preparation);
        }
        return placement;
    }

    private AtomicPartitionPlacementResolution RequireRemoteGraphSource(IKeyValueView view,
        PrincipalRecord principal, RemoteGraphSourceMutationV1 command)
    {
        if (command.Version != RemoteGraphProtocol.Version || command.Original is null
            || command.Original.CommandId == Guid.Empty || command.Preparations.IsDefault
            || command.Preparations.Length > Limits.MaxScanRecords
            || (command.Original.Upsert is null) == (command.Original.Delete is null))
        {
            throw Errors.Fail(ErrorCode.Validation, RemoteGraphProtocol.Invalid);
        }
        var original = command.Original;
        ValidatePartition(original.SourcePartition);
        var graphName = original.Upsert?.Graph ?? original.Delete!.Graph;
        var edgeId = original.Upsert?.EdgeId ?? original.Delete!.EdgeId;
        JsonData.Identifier(graphName);
        JsonData.Identifier(edgeId);
        Authorization.Require(principal, original.SourcePartition, graphName, Capability.GraphWrite);
        var graph = Resource(view, original.SourcePartition, graphName, ResourceKind.Graph);
        if (command.PreviousEdge is { } prior)
        {
            ValidateCanonicalEdge(prior, original.SourcePartition, edgeId);
        }
        var endpoint = original.Upsert?.From ?? command.PreviousEdge?.From
            ?? throw Errors.Fail(ErrorCode.NotFound, GraphEdgeUnavailable);
        if (endpoint.Partition != original.SourcePartition)
        {
            throw Errors.Fail(ErrorCode.Validation, RemoteGraphProtocol.Invalid);
        }
        _ = VisibleVertex(view, principal, endpoint);
        if (original.Upsert is { } upsert)
        {
            JsonData.Identifier(upsert.Label);
            if (upsert.To is null || upsert.To.Partition == original.SourcePartition)
            {
                throw Errors.Fail(ErrorCode.Validation, RemoteGraphProtocol.Invalid);
            }
            foreach (var policy in graph.FieldPolicies)
            {
                Authorization.RequireFieldWrite(principal, graph, policy.Path);
            }
            _ = JsonData.Validate(upsert.AttributesJson, Limits);
        }
        return ReadPlacementWitness(view, original.SourcePartition);
    }
}
