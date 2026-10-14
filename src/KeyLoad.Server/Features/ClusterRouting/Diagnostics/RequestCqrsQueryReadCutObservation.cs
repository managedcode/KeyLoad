using System.Security.Cryptography;
using System.Text;
using KeyLoad.Clustering;
using KeyLoad.Query;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed class RequestCqrsQueryReadCutObservation(RequestCqrsProbeFiles files,
    RequestCqrsProbeLifecycle lifecycle, string voter, string siloAddress) : IQueryReadCutObserver
{
    public void Observe(QueryReadCutObservation observation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (RequestContext.Get(GrainRequestStreamProtocol.ContextKey) is not GrainRequestContextState current)
        {
            return;
        }
        var claim = lifecycle.FindClaim(current.RequestId);
        if (claim is null || claim.Identity.ReadKind != GrainReadKind.AstQuery
            || claim.Arm.Record.Phase != RequestCqrsProbePhase.RequestStarted
            || claim.Arm.Record.Action != RequestCqrsProbeAction.Hold)
        {
            return;
        }
        var failures = new List<Exception>();
        lifecycle.EnterCallback();
        ServerFailureObserver.Observe(() => ObserveClaim(observation, current, claim, cancellationToken), failures);
        ServerFailureObserver.Observe(lifecycle.ExitCallback, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private void ObserveClaim(QueryReadCutObservation observation, GrainRequestContextState current,
        RequestCqrsProbeClaim claim, CancellationToken token)
    {
        if (current.CommandId != claim.Identity.CommandId || current.ConnectionId != claim.ConnectionId
            || current.ConnectionId == Guid.Empty || claim.Identity.CommandId != Guid.Empty
            || claim.Identity.PrincipalId != observation.PrincipalId
            || claim.QueryReadContext is not { } context || context.ActivationId.IsDefault)
        {
            throw new InvalidOperationException(RequestCqrsProbeProtocol.InvalidRecord);
        }
        var failures = new List<Exception>();
        lifecycle.EnterGate();
        ServerFailureObserver.Observe(() =>
        {
            token.ThrowIfCancellationRequested();
            var witness = new RequestCqrsQueryReadCutWitness(RequestCqrsProbeProtocol.Version,
                RequestCqrsQueryReadCutInventory.Kind, files.SessionId, claim.Arm.Record.ArmId,
                current.RequestId, current.CommandId, current.ConnectionId, observation.PrincipalId,
                voter, siloAddress, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(context.GrainId.ToString()))),
                context.ActivationId.ToParsableString(), observation.NodeId, observation.Incarnation,
                observation.ReadGeneration, observation.ResultCutPosition, observation.StorePosition,
                observation.PolicyEpoch, observation.SchemaVersion, observation.QueryHash,
                new(observation.Partition.TenantId, observation.Partition.DatabaseId,
                    observation.Partition.TransactionDomainId, observation.Partition.PartitionKey), observation.AccessPath);
            files.QueryCuts.Publish(witness, claim);
            token.ThrowIfCancellationRequested();
        }, failures);
        ServerFailureObserver.Observe(lifecycle.ExitGate, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    public void Observe(WaitForIndexReadCutObservation observation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (RequestContext.Get(GrainRequestStreamProtocol.ContextKey) is not GrainRequestContextState current)
        {
            return;
        }
        var claim = lifecycle.FindClaim(current.RequestId);
        if (claim is null || claim.Identity.ReadKind != GrainReadKind.WaitForIndex
            || claim.Arm.Record.Phase != RequestCqrsProbePhase.RequestStarted
            || claim.Arm.Record.Action != RequestCqrsProbeAction.Hold)
        {
            return;
        }
        var failures = new List<Exception>();
        lifecycle.EnterCallback();
        ServerFailureObserver.Observe(() => ObserveWaitClaim(observation, current, claim, cancellationToken), failures);
        ServerFailureObserver.Observe(lifecycle.ExitCallback, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private void ObserveWaitClaim(WaitForIndexReadCutObservation observation, GrainRequestContextState current,
        RequestCqrsProbeClaim claim, CancellationToken token)
    {
        if (current.CommandId != claim.Identity.CommandId || current.ConnectionId != claim.ConnectionId
            || current.ConnectionId == Guid.Empty || claim.Identity.CommandId != Guid.Empty
            || claim.Identity.PrincipalId != observation.PrincipalId
            || claim.QueryReadContext is not { } context || context.ActivationId.IsDefault)
        {
            throw new InvalidOperationException(RequestCqrsProbeProtocol.InvalidRecord);
        }
        var failures = new List<Exception>();
        lifecycle.EnterGate();
        ServerFailureObserver.Observe(() =>
        {
            token.ThrowIfCancellationRequested();
            var witness = new RequestCqrsWaitForIndexReadCutWitness(RequestCqrsProbeProtocol.Version,
                RequestCqrsWaitForIndexReadCutInventory.Kind, files.SessionId, claim.Arm.Record.ArmId,
                current.RequestId, current.CommandId, current.ConnectionId, observation.PrincipalId,
                voter, siloAddress, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(context.GrainId.ToString()))),
                context.ActivationId.ToParsableString(), observation.NodeId, observation.Incarnation,
                observation.ReadGeneration, observation.AppliedToken.Incarnation, observation.AppliedToken.AtomicPartitionId,
                observation.AppliedToken.Position, observation.AppliedToken.OwnershipEpoch, observation.StorePosition,
                observation.PolicyEpoch, observation.SchemaVersion, observation.NativeRequestDigest,
                new(observation.Partition.TenantId, observation.Partition.DatabaseId,
                    observation.Partition.TransactionDomainId, observation.Partition.PartitionKey));
            files.WaitCuts.Publish(witness, claim);
            token.ThrowIfCancellationRequested();
        }, failures);
        ServerFailureObserver.Observe(lifecycle.ExitGate, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
