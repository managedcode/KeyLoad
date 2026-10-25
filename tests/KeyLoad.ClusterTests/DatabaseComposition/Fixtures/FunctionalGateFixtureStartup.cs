using System.Globalization;
using KeyLoad.AppHost.TestInfrastructure;
using KeyLoad.ClusterTests.StorageReplication;
using KeyLoad.Server;

namespace KeyLoad.ClusterTests.DatabaseComposition;

/// <summary>Owns original functional startup arguments and failure cleanup before publication.</summary>
internal static class FunctionalGateFixtureStartup
{
    internal const string FailureRootDataKey = "KeyLoad.FunctionalGate.Root";

    internal static async Task<FunctionalGateFixture> InitializeAsync(FunctionalGateFixture owner, Func<Task> initialize)
    {
        try
        {
            await initialize().ConfigureAwait(false);
            return owner;
        }
        catch (Exception primary)
        {
            owner.PreserveEvidence = true;
            var failures = new List<Exception> { primary };
            ServerFailureObserver.Observe(() => primary.Data[FailureRootDataKey] = owner.Root, failures);
            await ServerFailureObserver.ObserveAsync(() => owner.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }

    internal static List<string> Arguments(string root, int nodes, int replicas, bool coldReopen, bool exclusiveOwnerContention)
    {
        var arguments = new List<string>
        {
            ClusterFixtureProtocol.DataRootArgument + root,
            ClusterFixtureProtocol.EphemeralArgument,
            "--KeyLoadTests:FunctionalNodeCount=" + nodes.ToString(CultureInfo.InvariantCulture),
            "--KeyLoad:SiloCount=" + nodes.ToString(CultureInfo.InvariantCulture),
            "--KeyLoad:ReplicaCount=" + replicas.ToString(CultureInfo.InvariantCulture)
        };
        if (coldReopen)
        {
            arguments.Add(FunctionalColdReopenProfile.EnabledArgument);
            arguments.Add(ProcessFaultCommandProtocol.EnabledArgument);
        }
        if (exclusiveOwnerContention)
        {
            arguments.Add(KeyLoad.AppHost.StorageRecovery.ExclusiveOwnerContentionProfile.EnabledArgument);
        }
        return arguments;
    }
}
