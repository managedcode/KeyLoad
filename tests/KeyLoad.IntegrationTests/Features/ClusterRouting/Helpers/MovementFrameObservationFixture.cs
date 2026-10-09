using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;
using Microsoft.Extensions.Options;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal sealed class MovementFrameObservationFixture
{
    private readonly Dictionary<string, MovementFrameObservationNodeFiles> nodes;
    private readonly PhysicalShardRecord target;
    private readonly IOptions<RequestProbeExecutionOptions> options;
    internal string Root { get; }
    internal string SessionId { get; }
    private bool closed;
    private bool evidencePreserved;
    internal bool RequiresEvidence { get; private set; }
    internal Guid SelectionId { get; } = Guid.NewGuid();
    internal MovementFrameObservationFixture(string root, string session,
        PhysicalShardRecord target, Dictionary<string, MovementFrameObservationNodeFiles> nodes,
        IOptions<RequestProbeExecutionOptions> options)
    { Root = root; SessionId = session; this.target = target; this.nodes = nodes; this.options = options; }

    internal void Select(PartitionMovementPublicParentRf3Seed seed, MovementFrameObservationSelectionMode mode)
    {
        if (mode == MovementFrameObservationSelectionMode.Absent)
        { return; }
        if (!Enum.IsDefined(mode))
        { throw new ArgumentOutOfRangeException(nameof(mode)); }
        RequiresEvidence = mode == MovementFrameObservationSelectionMode.Exact;
        var moveId = RequiresEvidence ? seed.FirstRequest.MoveId : Guid.NewGuid();
        if (!RequiresEvidence && moveId == seed.FirstRequest.MoveId)
        { throw new InvalidOperationException(MovementFrameObservationFixtureProtocol.Invalid); }
        var selection = new MovementFrameObservationSelection(MovementFrameObservationProtocol.Version,
            MovementFrameObservationProtocol.SelectionKind, SessionId, SelectionId, moveId,
            new(seed.Partition.TenantId, seed.Partition.DatabaseId, seed.Partition.TransactionDomainId,
                seed.Partition.PartitionKey), PartitionMovementPublicParentRf3Administrator.PrincipalId,
            target.PhysicalShardId, target.Incarnation);
        foreach (var node in nodes.Values)
        { node.Select(selection); }
    }

    internal async Task<MovementFrameObservationRecord> WaitFirstAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var node in nodes.Values)
            {
                if (node.Read() is { } observed)
                { return observed; }
            }
            await Task.Delay(options.Value.PollInterval, TimeProvider.System, cancellationToken).ConfigureAwait(false);
        }
    }
    internal async Task<MovementFrameObservationRecord[]> WaitAllAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var results = nodes.Values.Select(node => node.Read()).ToArray();
            if (results.All(result => result.HasValue))
            { return results.Select(result => result!.Value).ToArray(); }
            await Task.Delay(options.Value.PollInterval, TimeProvider.System, cancellationToken).ConfigureAwait(false);
        }
    }
    internal Dictionary<string, MovementFrameObservationRecord> ReadAll()
        => nodes.ToDictionary(pair => pair.Key, pair => pair.Value.Read()
            ?? throw new InvalidOperationException(MovementFrameObservationFixtureProtocol.Invalid), StringComparer.Ordinal);

    internal async Task RequireAbsentAsync()
    {
        foreach (var node in nodes.Values)
        { await Assert.That(node.Read()).IsNull(); }
    }

    internal void RetireAfterJoinedStop(TwoRf3MembershipWave wave)
    {
        RequireStopped(wave);
        PreserveEvidence();
        foreach (var node in nodes.Values)
        { node.RetireSelection(); }
    }
    internal void DeleteAfterOwnerJoin(TwoRf3MembershipWave wave)
    {
        if (closed)
        { return; }
        if (!wave.applicationDisposed || !wave.nodeLocksReleased)
        { throw new InvalidOperationException(MovementFrameObservationFixtureProtocol.Invalid); }
        RequireStopped(wave);
        var failures = new List<Exception>();
        ServerFailureObserver.Observe(PreserveEvidence, failures);
        foreach (var node in nodes.Values)
        {
            ServerFailureObserver.Observe(node.RetireSelection, failures);
            ServerFailureObserver.Observe(node.DeleteOwner, failures);
        }
        if (failures.Count == PartitionMoveProtocol.EmptyCount)
        { ServerFailureObserver.Observe(() => Directory.Delete(Root, recursive: false), failures); }
        ServerFailureObserver.ThrowIfAny(failures);
        closed = true;
    }
    private void PreserveEvidence()
    {
        if (evidencePreserved)
        { return; }
        if (OperatingSystem.IsWindows())
        { throw new PlatformNotSupportedException(MovementFrameObservationFixtureProtocol.Invalid); }
        var qualification = Path.Combine(ClusterFixtureDiagnostics.FindRepositoryRoot().FullName,
            global::KeyLoad.IntegrationTests.Features.ClusterReplication.ContainerRuntimeProtocol.ArtifactsDirectory,
            global::KeyLoad.IntegrationTests.Features.ClusterReplication.ContainerRuntimeProtocol.QualificationDirectory);
        Directory.CreateDirectory(qualification);
        var proof = Path.Combine(qualification, Path.GetFileName(Root) + MovementFrameObservationFixtureProtocol.ProofSuffix);
        if (Directory.Exists(proof) || File.Exists(proof))
        { throw new InvalidOperationException(MovementFrameObservationFixtureProtocol.Invalid); }
        Directory.CreateDirectory(proof, RequestCqrsProbeFileValidation.PrivateDirectoryMode);
        foreach (var pair in nodes)
        { pair.Value.PreserveTo(Path.Combine(proof, pair.Key)); }
        evidencePreserved = true;
    }
    private static void RequireStopped(TwoRf3MembershipWave wave)
    {
        foreach (var node in TwoRf3MembershipProtocol.Nodes)
        {
            var root = Path.Combine(wave.OwnedDataRoot, node);
            KeyLoad.IntegrationTests.Features.StorageRecovery.NodeEpochRf3OfflineFiles.AssertExclusive(Path.Combine(root, MovementFrameObservationFixtureProtocol.NodeOwnerLock));
            KeyLoad.IntegrationTests.Features.StorageRecovery.NodeEpochRf3OfflineFiles.AssertExclusive(Path.Combine(root, RequestCqrsRf3Protocol.Database, MovementFrameObservationFixtureProtocol.StoreLock));
            KeyLoad.IntegrationTests.Features.StorageRecovery.NodeEpochRf3OfflineFiles.AssertExclusive(Path.Combine(root, MovementFrameObservationFixtureProtocol.ReplicaDirectory, MovementFrameObservationFixtureProtocol.StoreLock));
        }
    }
}
