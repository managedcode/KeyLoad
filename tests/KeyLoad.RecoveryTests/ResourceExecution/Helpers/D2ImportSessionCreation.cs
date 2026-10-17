using System.Security.Cryptography;
using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.CrashHost.Features.ResourceExecution;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.RecoveryTests.Features.ResourceExecution;

internal static class D2ImportSessionCreation
{
    internal static (ControlledPartitionMovementNativeNode Node, PartitionMovementPeerAdmission Admission)
        Target(D2ImportSession session)
    {
        var node = Node(session.Root, D2ImportCrashProtocol.TargetDirectory, session.Owners.Destination.Owner);
        try
        {
            return (node, Admission(node, session.TargetRuntime));
        }
        catch (Exception original)
        {
            var failures = new List<Exception> { original };
            ServerFailureObserver.Observe(node.Dispose, failures);
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }

    internal static (ServerRuntimeOptions Source, ServerRuntimeOptions Target, string Caller, DateTimeOffset ExpiresAt)
        Runtime(D2ImportSession session, ControlledPartitionMovementLoopbackListeners listeners)
    {
        var sourceKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(D2ImportCrashProtocol.KeyBytes));
        var targetKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(D2ImportCrashProtocol.KeyBytes));
        var source = ControlledPartitionMovementProcessOptions.Bind(session.Source, session.Owners, sourceKey, targetKey, control: true);
        var target = ControlledPartitionMovementProcessOptions.Bind(session.Target, session.Owners, sourceKey, targetKey, control: false);
        var caller = ControlledPartitionMovementProcessPrepareRequest.CallerAddress(listeners);
        var expiresAt = session.Source.Database.EvaluationClock.GetUtcNow() + source.GrainRouting.Value.RequestLifetime;
        return (source, target, caller, expiresAt);
    }

    internal static ControlledPartitionMovementNativeNode Node(string root, string name, PhysicalShardRecord owner)
        => new(Path.Combine(root, name), owner,
            maximumFixtureEntries: ControlledPartitionMovementNativeJournal.TerminalHistoryEntries);

    internal static PartitionMovementPeerAdmission Admission(ControlledPartitionMovementNativeNode node, ServerRuntimeOptions runtime)
        => new(runtime.Node, node.Database, runtime.ReplicaConfiguration, runtime.GrainRouting,
            runtime.Membership, runtime.PartitionMovement, node.Database.EvaluationClock);
}
