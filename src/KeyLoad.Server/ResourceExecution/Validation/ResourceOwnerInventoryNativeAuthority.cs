using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Core.Features.ResourceExecution.Execution;
using KeyLoad.Server.Features.ClusterRouting;
using KeyLoad.Storage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ResourceExecution;

/// <summary>Requires configured groups and actual ready Orleans generations; does not establish Core inventory completeness.</summary>
internal sealed class ResourceOwnerInventoryNativeAuthority(OrleansNode node, PartitionHost partition,
    IOptions<NodeOptions> options, PartitionMovementGrainServiceClient movement, SiloStorageIdentityProvider local)
{
    private readonly NodeOptions settings = options.Value;

    internal async Task<SiloAddress> ResolveAsync(RegisteredPhysicalOwnerV1 control,
        RegisteredPhysicalOwnerV1 destination, string destinationVoter, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        RequireConfigured(control, destination);
        var index = destination.Owner.VoterIds.IndexOf(destinationVoter);
        return await movement.ResolveAsync(destination, index, token).ConfigureAwait(false);
    }

    internal async Task RequireBeforeSendAsync(ResourceOwnerInventoryNativeRequest request, CancellationToken token)
    {
        await node.RequireDatabaseReadCutAsync(token).ConfigureAwait(false);
        RequireConfigured(request.Control, request.Destination);
        RequireLocal(request.Control, request.CallerVoter, request.CallerSiloAddress);
        var address = await ResolveAsync(request.Control, request.Destination, request.DestinationVoter, token)
            .ConfigureAwait(false);
        if (!string.Equals(address.ToParsableString(), request.DestinationSiloAddress, StringComparison.Ordinal))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, ResourceOwnerInventoryNativeProtocol.Unavailable);
        }
        token.ThrowIfCancellationRequested();
        RequireLocal(request.Control, request.CallerVoter, request.CallerSiloAddress);
    }

    internal async Task RequireReceivingAsync(ResourceOwnerInventoryNativeRequest request, CancellationToken token)
    {
        await node.RequireDatabaseReadCutAsync(token).ConfigureAwait(false);
        RequireConfigured(request.Control, request.Destination);
        RequireLocal(request.Destination, request.DestinationVoter, request.DestinationSiloAddress);
        await movement.RequireCallerAsync(request.CallerVoter, request.CallerSiloAddress, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        RequireLocal(request.Destination, request.DestinationVoter, request.DestinationSiloAddress);
    }

    internal Task RequireReplyAuthorityAsync(ResourceOwnerInventoryNativeRequest request, CancellationToken token)
        => RequireBeforeSendAsync(request, token);

    internal async Task RequireBeforeSendAsync(ResourceRetirementNativeRequest request, ReadExecutionBudget work,
        ReadExecutionBudgetReadGrant? sourceMeta, CancellationToken token)
    {
        await node.RequireDatabaseReadCutAsync(work, sourceMeta, token).ConfigureAwait(false);
        RequireConfigured(request.Control, request.Destination);
        RequireLocal(request.Control, request.CallerVoter, request.CallerSiloAddress);
        var address = await ResolveAsync(request.Control, request.Destination, request.DestinationVoter, token)
            .ConfigureAwait(false);
        if (!string.Equals(address.ToParsableString(), request.DestinationSiloAddress, StringComparison.Ordinal))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, ResourceRetirementNativeProtocol.Unavailable);
        }
        token.ThrowIfCancellationRequested();
        RequireLocal(request.Control, request.CallerVoter, request.CallerSiloAddress);
    }

    internal async Task RequireReceivingAsync(ResourceRetirementNativeRequest request, ReadExecutionBudget work,
        ReadExecutionBudgetReadGrant? sourceMeta, CancellationToken token)
    {
        await node.RequireDatabaseReadCutAsync(work, sourceMeta, token).ConfigureAwait(false);
        RequireConfigured(request.Control, request.Destination);
        RequireLocal(request.Destination, request.DestinationVoter, request.DestinationSiloAddress);
        await movement.RequireCallerAsync(request.CallerVoter, request.CallerSiloAddress, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        RequireLocal(request.Destination, request.DestinationVoter, request.DestinationSiloAddress);
    }

    internal Task RequireReplyAuthorityAsync(ResourceRetirementNativeRequest request, ReadExecutionBudget work,
        ReadExecutionBudgetReadGrant? sourceMeta, CancellationToken token)
        => RequireBeforeSendAsync(request, work, sourceMeta, token);

    private void RequireConfigured(RegisteredPhysicalOwnerV1 control, RegisteredPhysicalOwnerV1 destination)
    {
        if (!PhysicalOwnerEntryValidation.Valid(control) || !PhysicalOwnerEntryValidation.Valid(destination))
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, ResourceOwnerInventoryNativeProtocol.InvalidProof);
        }
        var expectedControl = PhysicalOwnerConfiguredTuples.Control(settings, partition);
        var expectedDestination = PhysicalOwnerConfiguredTuples.Destination(settings, partition);
        if (!PhysicalOwnerEntryValidation.Same(control, expectedControl))
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, ResourceOwnerInventoryNativeProtocol.InvalidProof);
        }
        if (!PhysicalOwnerEntryValidation.Same(destination, expectedControl)
            && !PhysicalOwnerEntryValidation.Same(destination, expectedDestination))
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, ResourceOwnerInventoryNativeProtocol.Unavailable);
        }
    }

    private void RequireLocal(RegisteredPhysicalOwnerV1 owner, string voter, string address)
    {
        var expected = PhysicalOwnerConfiguredTuples.Local(settings, partition);
        var actual = local.Read();
        if (!PhysicalOwnerEntryValidation.Same(owner, expected)
            || !actual.TransportReady || actual.VoterId != voter || actual.SiloAddress != address
            || actual.VoterId != partition.Configuration.LocalId || actual.ClusterId != settings.ClusterId
            || actual.Incarnation != expected.Owner.Incarnation
            || actual.ApplicationRpcVersion != GrainRoutingProtocol.RequestInterfaceVersion
            || actual.PeerEnvelopeVersion != ReplicaTransportProtocol.Version
            || actual.RuntimeJournalReaderContract != StoreReaderContract.RuntimeJournal)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, ResourceOwnerInventoryNativeProtocol.Unavailable);
        }
    }
}
