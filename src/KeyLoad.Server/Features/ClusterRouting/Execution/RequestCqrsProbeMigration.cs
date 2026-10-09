using KeyLoad.Orleans;
using KeyLoad.Replication;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Opt-in post-executor migration; borrows real observer admission and signed discovery.</summary>
internal sealed class RequestCqrsProbeMigration(RequestCqrsProbeFiles files, RequestCqrsProbeLifecycle lifecycle,
    IOptions<ReplicaConfiguration> replicaOptions, string siloAddress, ReplicaSiloDiscoveryClient discovery, CancellationToken stopping)
    : IGrainActivationMigrationObserver
{
    private readonly ReplicaConfiguration replica = replicaOptions.Value;

    public async ValueTask<SiloAddress?> PrepareMigrationAsync(GrainRequestProbeIdentity identity,
        IGrainContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        lifecycle.EnterCallback();
        try
        {
            var claim = lifecycle.FindClaim(identity.RequestId);
            if (claim is null)
            { return null; }
            if (claim.Identity != identity || context.GrainInstance is not CommandPartitionGrain)
            { throw Invalid(); }
            var marker = new RequestCqrsProbeMarkerRecord(RequestCqrsProbeProtocol.Version, RequestCqrsProbeProtocol.MarkerKind,
                files.SessionId, claim.Arm.Record.ArmId, identity.RequestId, identity.CommandId,
                RequestCqrsProbePhase.BeforeSubmit, RequestCqrsProbeOutcome.Observed, replica.LocalId, siloAddress);
            var witness = RequestCqrsProbeActivationCapture.Create(marker, context);
            var request = files.Migration.Find(witness);
            if (request is null)
            { return null; }
            files.RequireActiveArm(claim.Arm);
            if (claim.Arm.Record.Phase != RequestCqrsProbePhase.BeforeSubmit
                || claim.Arm.Record.Action != RequestCqrsProbeAction.Hold || claim.Arm.Record.ReadKind is not null
                || request.Value.TargetVoter == replica.LocalId || !replica.VoterIds.Contains(request.Value.TargetVoter, StringComparer.Ordinal))
            { throw Invalid(); }
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, stopping);
            var target = await discovery.ResolveAsync(request.Value.TargetVoter, refresh: true, lifetime.Token).ConfigureAwait(true);
            lifetime.Token.ThrowIfCancellationRequested();
            if (target.ToParsableString() != request.Value.TargetSiloAddress || target.ToParsableString() == siloAddress)
            { throw Invalid(); }
            files.Migration.Requested(request.Value);
            return target;
        }
        finally
        { lifecycle.ExitCallback(); }
    }
    private static KeyLoadException Invalid() => Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
}
