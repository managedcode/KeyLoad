using System.Security.Cryptography;
using KeyLoad.Orleans;

namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Keeps original immutable claimed cleanup authoritative for both local and directed native notification.</summary>
internal sealed class RequestCqrsProbeProducerDisposal(RequestCqrsProbeFiles files, RequestCqrsProbeLifecycle lifecycle,
    Func<RequestCqrsProbeClaim, RequestCqrsProbePhase, RequestCqrsProbeOutcome, RequestCqrsProbeMarkerRecord> marker,
    RequestCqrsReceiverIssueAdjunct adjunct)
{
    private readonly Lock completion = new();
    private RequestCqrsProbeNativeRelay? relay;

    internal void Attach(RequestCqrsProbeNativeRelay value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (Interlocked.CompareExchange(ref relay, value, null) is not null)
        { throw Invalid(); }
    }

    internal ValueTask CompleteAsync(GrainRequestProbeIdentity identity, Guid connectionId, IGrainContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.GrainInstance is not ConnectionGrain producer || producer.GetPrimaryKey() != connectionId)
        { throw Invalid(); }
        var original = new RequestCqrsProbeNativeIdentity(identity.RequestId, identity.CommandId, identity.PrincipalId,
            identity.ReadKind, identity.CommandKind, identity.OriginalExpiresAt, connectionId);
        var producerAddress = context.Address.SiloAddress ?? throw Invalid();
        return (relay ?? throw Invalid()).NotifyDisposedAsync(original, producerAddress);
    }

    internal RequestCqrsProbeNativeClaim? Locate(RequestCqrsProbeNativeIdentity identity, PhysicalShardRecord owner,
        string voter, string address)
    {
        var claim = lifecycle.FindClaim(identity.RequestId);
        if (claim is null) { return null; }
        if (!Matches(claim, identity)) { throw Invalid(); }
        return new(claim.Arm.Record.SessionId, claim.Arm.Record.ArmId, identity, owner, voter, address,
            Convert.ToHexStringLower(SHA256.HashData(claim.Arm.ExactBytes)));
    }

    internal void Complete(RequestCqrsProbeNativeClaim receipt, PhysicalShardRecord owner, string voter, string address)
    {
        lock (completion)
        {
            var actual = Locate(receipt.Identity, owner, voter, address);
            if (!RequestCqrsProbeNativeAuthentication.SameClaim(actual, receipt)) { throw Invalid(); }
            var identity = receipt.Identity;
            Complete(new(identity.RequestId, identity.CommandId, identity.PrincipalId, identity.ReadKind, identity.CommandKind)
            { OriginalExpiresAt = identity.OriginalExpiresAt });
        }
    }

    internal void Complete(GrainRequestProbeIdentity identity)
    {
        lock (completion)
        {
            var claim = lifecycle.FindClaim(identity.RequestId);
            if (claim is null) { return; }
            lifecycle.EnterClaimedCallback();
            try
            {
                if (claim.Identity != identity) { throw Invalid(); }
                var failures = new List<Exception>();
                ServerFailureObserver.Observe(() => files.WriteClaimedProducerDisposed(
                    marker(claim, RequestCqrsProbePhase.ProducerDisposed, RequestCqrsProbeOutcome.Observed), claim.Arm), failures);
                ServerFailureObserver.Observe(() => adjunct.ProducerDisposed(claim), failures);
                ServerFailureObserver.ThrowIfAny(failures);
            }
            finally
            {
                lifecycle.RemoveClaim(identity.RequestId, claim);
                lifecycle.ExitCallback();
            }
        }
    }

    private static bool Matches(RequestCqrsProbeClaim claim, RequestCqrsProbeNativeIdentity identity)
        => claim.ConnectionId == identity.ConnectionId && claim.Identity.RequestId == identity.RequestId
            && claim.Identity.CommandId == identity.CommandId && claim.Identity.PrincipalId == identity.PrincipalId
            && claim.Identity.ReadKind == identity.ReadKind && claim.Identity.CommandKind == identity.CommandKind
            && claim.Identity.OriginalExpiresAt == identity.OriginalExpiresAt;
    private static InvalidOperationException Invalid() => new(RequestCqrsProbeProtocol.InvalidFiles);
}
