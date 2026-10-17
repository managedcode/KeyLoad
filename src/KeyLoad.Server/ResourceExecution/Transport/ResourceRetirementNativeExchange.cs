using System.Security.Cryptography;
using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution.Execution;
using KeyLoad.Server.Features.ClusterRouting;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ResourceExecution;

internal sealed class ResourceRetirementNativeExchange(PartitionMovementGrainServiceClient movement,
    ResourceOwnerInventoryNativeAuthority authority, ResourceOwnerInventoryNativeTrust trust,
    ResourceRetirementSignedExecution execution,
    PartitionHost partition, IOptions<NodeOptions> options, IOptions<DatabaseLimits> limits,
    IOptions<GrainRoutingOptions> routing, ReplicaSiloDiscoveryState local, TimeProvider clock)
{
    private const long UnassignedPolicyEpoch = 0;

    internal Task RequireSourceAsync(string principalId, long policyEpoch, ReadExecutionBudget work, ReadExecutionBudgetReadGrant metadata, CancellationToken token)
        => execution.RequireSourceAsync(principalId, policyEpoch, work, metadata, token);

    internal async Task<ResourceRetirementPhaseResultV1> PhaseAsync(RegisteredPhysicalOwnerV1 destination,
        Guid commandId, ResourceRetirementNativePhaseRequestV1 request, string sourcePrincipalId,
        long sourcePolicyEpoch, ReadExecutionBudget work, ReadExecutionBudgetReadGrant metadata, CancellationToken token)
    {
        var (voter, address) = await ResourceRetirementNativeDestination.ResolveAsync(authority, options, partition, destination, token).ConfigureAwait(false);
        request = request with
        {
            DestinationVoter = voter,
            DestinationSiloAddress = address.ToParsableString(),
            OriginalPolicyEpoch = UnassignedPolicyEpoch
        };
        var payload = NativeSerialization.Serialize(request);
        try
        {
            var current = local.Read();
            var outer = new ResourceRetirementNativeRequest(ResourceRetirementNativeProtocol.Version,
                Guid.NewGuid(), Guid.NewGuid(), sourcePrincipalId, sourcePolicyEpoch,
                PhysicalOwnerConfiguredTuples.Control(options.Value, partition.Configuration), destination,
                current.VoterId, current.SiloAddress, voter, address.ToParsableString(), clock.GetUtcNow(),
                request.ExpiresAt, request.MaximumReadBytes, request.MaximumExaminedRecords,
                request.MaximumResultBytes, payload, string.Empty, commandId, true);
            return await SendAsync<ResourceRetirementPhaseResultV1>(outer, work, metadata, token).ConfigureAwait(false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
        }
    }

    internal Task<ResourceRetirementReadReplyV1> ReadAsync(RegisteredPhysicalOwnerV1 destination,
        ResourceRetirementReadRequestV1 query, string sourcePrincipalId, long sourcePolicyEpoch, ReadExecutionBudget work, ReadExecutionBudgetReadGrant metadata, CancellationToken token)
    {
        if (query.Operation == ResourceRetirementReadOperation.OwnerStatus)
        {
            throw Errors.Fail(ErrorCode.Validation, ResourceRetirementNativeProtocol.InvalidProof);
        }
        return ReadCoreAsync(destination, null, query, sourcePrincipalId, sourcePolicyEpoch, work, metadata, token);
    }

    internal Task<ResourceRetirementReadReplyV1> ReadToVoterAsync(RegisteredPhysicalOwnerV1 destination,
        string voter, ResourceRetirementReadRequestV1 query, string sourcePrincipalId, long sourcePolicyEpoch, ReadExecutionBudget work, ReadExecutionBudgetReadGrant metadata,
        CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(voter))
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, ResourceRetirementNativeProtocol.InvalidProof);
        }
        return ReadCoreAsync(destination, voter, query, sourcePrincipalId, sourcePolicyEpoch, work, metadata, token);
    }

    internal async Task<ResourceRetirementWorkJoinEvidenceV1> ReadWorkAsync(RegisteredPhysicalOwnerV1 destination,
        string voter, ResourceRetirementReadRequestV1 query, string principalId, long epoch, ReadExecutionBudget work, ReadExecutionBudgetReadGrant metadata, CancellationToken token)
    {
        if (query.Operation != ResourceRetirementReadOperation.ActualOwnerWorkJoin)
        {
            throw Errors.Fail(ErrorCode.Validation, ResourceRetirementNativeProtocol.InvalidProof);
        }
        byte[]? requestBytes = null;
        byte[]? replyBytes = null;
        var reply = await ReadCoreAsync(destination, voter, query, principalId, epoch, work, metadata, token, (request, response) =>
        {
            requestBytes = NativeSerialization.Serialize(request);
            replyBytes = NativeSerialization.Serialize(response);
        }).ConfigureAwait(false);
        var witness = reply.WorkJoinWitness
            ?? throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementNativeProtocol.InvalidProof);
        return new(witness, requestBytes ?? throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementNativeProtocol.InvalidProof),
            replyBytes ?? throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementNativeProtocol.InvalidProof));
    }

    private async Task<ResourceRetirementReadReplyV1> ReadCoreAsync(RegisteredPhysicalOwnerV1 destination,
        string? requestedVoter, ResourceRetirementReadRequestV1 query, string sourcePrincipalId,
        long sourcePolicyEpoch, ReadExecutionBudget work, ReadExecutionBudgetReadGrant metadata, CancellationToken token,
        Action<ResourceRetirementNativeRequest, ResourceRetirementNativeReply>? capture = null)
    {
        var (voter, address) = requestedVoter is null
            ? await ResourceRetirementNativeDestination.ResolveAsync(authority, options, partition, destination, token).ConfigureAwait(false)
            : await ResourceRetirementNativeDestination.ResolveAsync(authority, options, partition, destination, requestedVoter, token).ConfigureAwait(false);
        query = query with
        {
            DestinationVoter = voter,
            DestinationSiloAddress = address.ToParsableString(),
            OriginalPolicyEpoch = UnassignedPolicyEpoch
        };
        var payload = NativeSerialization.Serialize(query);
        try
        {
            var current = local.Read();
            var outer = new ResourceRetirementNativeRequest(ResourceRetirementNativeProtocol.Version,
                Guid.NewGuid(), Guid.NewGuid(), sourcePrincipalId, sourcePolicyEpoch,
                PhysicalOwnerConfiguredTuples.Control(options.Value, partition.Configuration), destination,
                current.VoterId, current.SiloAddress, voter, address.ToParsableString(), clock.GetUtcNow(),
                query.ExpiresAt, query.MaximumReadBytes, query.MaximumExaminedRecords,
                query.MaximumResultBytes, payload, string.Empty, Guid.Empty, false);
            var value = await SendAsync<ResourceRetirementReadReplyV1>(outer, work, metadata, token, capture).ConfigureAwait(false);
            ResourceRetirementNativeReadValidation.RequireReply(query, value, limits.Value);
            return value;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
        }
    }

    private async Task<T> SendAsync<T>(ResourceRetirementNativeRequest request, ReadExecutionBudget work, ReadExecutionBudgetReadGrant metadata, CancellationToken token,
        Action<ResourceRetirementNativeRequest, ResourceRetirementNativeReply>? capture = null) where T : class
    {
        using var mac = trust.CreateMac();
        request = request with
        {
            Signature = ResourceRetirementMac.SignRequest(mac, request, limits)
        };
        ResourceRetirementEnvelopeValidation.RequireRequest(mac, request, limits, routing, clock);
        await authority.RequireBeforeSendAsync(request, work, metadata, token).ConfigureAwait(false);
        work.Check();
        byte[]? rewritten = null;
        try
        {
            request = ResourceRetirementMetadataFrame.Constrain(request, metadata, limits.Value, out rewritten);
            request = request with
            {
                Signature = ResourceRetirementMac.SignRequest(mac, request, limits)
            };
            ResourceRetirementEnvelopeValidation.RequireRequest(mac, request, limits, routing, clock);
            return await SendAdmittedAsync<T>(mac, request, work, metadata, capture, token).ConfigureAwait(false);
        }
        finally
        {
            if (rewritten is not null)
            {
                CryptographicOperations.ZeroMemory(rewritten);
            }
        }
    }

    private async Task<T> SendAdmittedAsync<T>(PartitionMovementMac mac, ResourceRetirementNativeRequest request,
        ReadExecutionBudget work, ReadExecutionBudgetReadGrant metadata,
        Action<ResourceRetirementNativeRequest, ResourceRetirementNativeReply>? capture, CancellationToken token) where T : class
    {
        var address = SiloAddress.FromParsableString(request.DestinationSiloAddress);
        token.ThrowIfCancellationRequested();
        ResourceRetirementNativeReply reply;
        var dispatched = false;
        var received = false;
        var imported = false;
        Exception? authenticatedFailure = null;
        try
        {
            dispatched = true;
            reply = await movement.ExecuteResourceRetirementPacketAsync(address, request, token).ConfigureAwait(false);
            received = true;
            ResourceRetirementEnvelopeValidation.RequireReply(mac, request, reply, limits, clock);
            if (reply.Error is { } authenticatedCode)
            {
                authenticatedFailure = Errors.Fail(authenticatedCode, ResourceRetirementNativeProtocol.Unavailable);
            }

            var observed = reply.Metadata ?? throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementNativeProtocol.InvalidProof);
            if (observed.ReadBytes < ResourceRetirementReadContract.NoReadBytes
                || observed.ExaminedRecords < ResourceRetirementReadContract.NoRecords
                || observed.ResultBytes != reply.Payload.Length
                || observed.ReadBytes > request.MaximumReadBytes || observed.ExaminedRecords > request.MaximumExaminedRecords)
            {
                throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementNativeProtocol.InvalidProof);
            }

            work.ImportSettledReadGrant(metadata, observed.ReadBytes, observed.ExaminedRecords);
            imported = true;
            await authority.RequireReplyAuthorityAsync(request, work, metadata, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            var original = authenticatedFailure ?? (request.Command && !received
                ? Errors.Fail(ErrorCode.UnknownWriteOutcome, ResourceRetirementNativeProtocol.Unavailable, error) : error);
            if (dispatched && !imported)
            {
                work.FailIndeterminateMetadata(original);
            }

            if (ReferenceEquals(original, error))
            {
                throw;
            }

            throw original;
        }
        if (reply.Error is { } code)
        {
            throw Errors.Fail(code, ResourceRetirementNativeProtocol.Unavailable);
        }
        return ResourceRetirementMetadataFrame.Decode<T>(request, reply, capture);
    }
}
