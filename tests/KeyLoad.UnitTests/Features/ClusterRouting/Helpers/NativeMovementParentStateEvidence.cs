using System.Security.Cryptography;
using System.Text;
using KeyLoad.Orleans;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class NativeMovementParentStateEvidence(int configuredObserverOwnerIndex) : IGrainRequestPhaseObserver
{
    private readonly Lock gate = new();
    private readonly NativeMovementParentCommandEvidence commands = new();
    private Stage? executing;
    private Stage? returned;
    private Stage? lastExecuting;
    private Stage? lastReturned;

    public ValueTask ObserveAsync(GrainRequestProbeIdentity identity, GrainRequestPhase phase,
        IGrainContext context, CancellationToken cancellationToken)
    {
        commands.Observe(identity, phase, cancellationToken);
        if (identity.ReadKind != GrainReadKind.PartitionMovementParentState
            || phase is not (GrainRequestPhase.MovementParentStateExecuting or GrainRequestPhase.MovementParentStateReturned))
        { return ValueTask.CompletedTask; }
        lock (gate)
        {
            if (phase == GrainRequestPhase.MovementParentStateExecuting)
            {
                var snapshot = Read(identity, context);
                executing ??= snapshot;
                lastExecuting = snapshot;
                lastReturned = null;
            }
            else
            {
                var snapshot = Read(identity, context);
                if (returned is null && executing?.RequestId == identity.RequestId)
                { returned = snapshot; }
                if (lastExecuting?.RequestId == identity.RequestId)
                { lastReturned = snapshot; }
            }
        }
        return ValueTask.CompletedTask;
    }

    public void ProducerDisposed(GrainRequestProbeIdentity identity, IGrainContext context)
        => commands.ProducerDisposed(identity);

    internal void Write(TextWriter output)
    {
        Stage? before;
        Stage? after;
        Stage? latestBefore;
        Stage? latestAfter;
        lock (gate)
        { before = executing; after = returned; latestBefore = lastExecuting; latestAfter = lastReturned; }
        commands.Write(output, configuredObserverOwnerIndex);
        output.WriteLine($"Native movement configuredObserverOwnerIndex={configuredObserverOwnerIndex}; Executing={before}; Returned={after}; LastExecuting={latestBefore}; LastReturned={latestAfter}");
    }

    private static Stage Read(GrainRequestProbeIdentity identity, IGrainContext context)
    {
        var services = context.ActivationServices;
        var database = global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
            .GetService<global::KeyLoad.Core.DatabaseEngine>(services);
        var local = global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
            .GetService<global::KeyLoad.PhysicalShardRecord>(services);
        var configuration = global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
            .GetService<global::Microsoft.Extensions.Options.IOptions<global::KeyLoad.Replication.ReplicaConfiguration>>(services);
        ConfiguredOwner? configuredOwner = null;
        if (database is not null && local is not null && configuration is not null)
        {
            configuredOwner = new(local.PhysicalShardId, local.Incarnation, local.PlacementEpoch,
                string.Join("|", local.VoterIds), configuration.Value.LocalId,
                database.Store.Identity.Incarnation);
        }
        return new(identity.RequestId, identity.ReadKind,
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(context.GrainId.ToString()))),
            context.ActivationId.ToParsableString(), configuredOwner);
    }

    private sealed record Stage(Guid RequestId, GrainReadKind? ReadKind, string GrainIdDigest,
        string ActivationId, ConfiguredOwner? ConfiguredActivationOwner);

    private sealed record ConfiguredOwner(Guid PhysicalShardId, Guid Incarnation, long PlacementEpoch,
        string OrderedVoterIds, string LocalReplicaId, Guid StoreIncarnation);
}
