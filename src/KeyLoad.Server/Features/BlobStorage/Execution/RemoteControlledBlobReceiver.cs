using KeyLoad.Clustering;
using KeyLoad.Core.Features.BlobStorage;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Server.Features.ClusterRouting;
using KeyLoad.Server.Features.DocumentStorage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.BlobStorage;

internal sealed class RemoteControlledBlobReceiver(OrleansNode node, PartitionHost partition,
    IOptions<NodeOptions> options, IOptions<OrleansMembershipOptions> membership,
    IOptions<GrainRoutingOptions> routing, TimeProvider clock)
{
    internal ReplicaSiloDiscovery Discovery() => node.Discovery?.Read()
        ?? throw Errors.Fail(ErrorCode.OwnershipLost, RemoteDocumentProtocol.Unavailable);

    internal void ValidateOutcomeV2(RemoteControlledBlobOutcomeCallV2 call, CancellationToken token)
    {
        if (call?.Original?.Request?.Frame is not { Purpose: ControlledBlobReadPurpose.Outcome } frame)
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, RemoteDocumentProtocol.InvalidProof);
        }
        Validate(call.Original, token);
        if (NativeSerialization.Measure(call) > partition.Database.Limits.MaxBatchBytes)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, RemoteDocumentProtocol.InvalidProof);
        }
        PartitionControlLifetimeCoverage.Require(call.Coverage, frame.Control.Partition,
            [frame.Resource], partition.Database.Limits.MaxBatchBytes);
    }

    internal async Task<ControlledBlobReadResult> ReadOutcomeV2Async(RemoteControlledBlobOutcomeCallV2 call,
        RemoteModelNativeEffectBoundary? boundary, CancellationToken token, IResourceLifetimeWorkBinding binding)
    {
        ValidateOutcomeV2(call, token);
        var principal = partition.Database.Store.Read(view => partition.Database.Principal(view,
            PartitionStoreProtocol.AdministratorId, clock.GetUtcNow()));
        GrainRequestAuthority.RequireAdministrator(principal);
        var original = call.Original.Request;
        var request = new ControlledBlobReadRequestV2(new(original.Frame, call.Coverage),
            original.MaximumReadBytes, original.MaximumExaminedRecords, original.MaximumResultBytes);
        var requestId = Guid.NewGuid();
        var signed = node.CatalogRequestCodec().CreateControlledBlobOutcomeReadV2(requestId, principal.Id, request);
        boundary?.Begin(token);
        var result = await NativeLifetimeReadChildExecution.ExecuteAsync<ControlledBlobReadResult>(
            node, principal, requestId, signed, null, token, binding).ConfigureAwait(false);
        ValidateOutcomeV2(call, token);
        if (!result.OutcomeValidated || result.NativeValue is not null)
        {
            throw Errors.Fail(ErrorCode.Corruption, RemoteDocumentProtocol.InvalidProof);
        }
        return result;
    }

    internal void Validate(RemoteControlledBlobCall call, CancellationToken token)
        => RemoteControlledBlobValidation.Require(node, partition, options, membership, routing, clock, call, token);

}
