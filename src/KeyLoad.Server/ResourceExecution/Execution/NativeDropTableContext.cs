using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution.Execution;

namespace KeyLoad.Server.Features.ResourceExecution;

/// <summary>Call-local borrowed native composition; it owns neither the physical host nor native transport.</summary>
internal sealed record NativeDropTableContext(PartitionHost Partition, ResourceOwnerInventoryNativeClient Client,
    ServerRuntimeOptions Options, TimeProvider Clock, RegisteredPhysicalOwnerV1 Source, string PrincipalId,
    long PolicyEpoch, DateTimeOffset Expiry, ReadExecutionBudget Work, ReadExecutionBudgetReadGrant Metadata, CancellationToken Token)
{
    internal const int NoFailures = 0;
    internal ResourceRetirementWorkBarrierExecution? WorkExecution
    {
        get; set;
    }
    internal ResourceRetirementPhaseExecution Phases { get; } = new(Client);
    internal ResourceRetirementReads Reads { get; } = new(Client);

    internal async Task<ResourceRetirementStateV1?> ReadStateAsync(ResourceRetirementRequestV1 request)
    {
        var reply = await Reads.ExecuteAsync(global::KeyLoad.Clustering.ResourceRetirementReadOperation.State,
            request, null, Source, Guid.Empty, NoFailures, Expiry, PrincipalId, PolicyEpoch, Work, Metadata, Token).ConfigureAwait(false);
        return reply.State;
    }

    internal async Task<ResourceRetirementPhaseResultV1> PhaseAsync<T>(ResourceRetirementControlV1 control,
        ResourceRetirementPhase phase, T body, RegisteredPhysicalOwnerV1 receiver, Guid identityOwner)
        => await Phases.ExecuteAsync(control.Request, phase, body, receiver, identityOwner,
            PrincipalId, PolicyEpoch, Expiry, Work, Metadata, Token).ConfigureAwait(false);
}
