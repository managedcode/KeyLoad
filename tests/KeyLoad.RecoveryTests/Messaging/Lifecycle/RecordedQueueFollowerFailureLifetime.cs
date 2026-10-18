using System.Reflection;

namespace KeyLoad.RecoveryTests.Features.Messaging;

// Reads only exact native failure fields after a real operation fails; never replaces workers or outcomes.
internal static class RecordedQueueFollowerFailureLifetime
{
    private const string ConsensusStateField = "state";
    private const string OriginalFailureField = "failure";
    private const string ReplicaStateType = "KeyLoad.Replication.ReplicaState";
    private const string ShapeChanged = "The original native replica failure field shape changed.";
    private const string RetainedRoots = "Failed original native node roots retained after joined cleanup: ";
    private const string RootSeparator = "; ";
    private const string NodeSeparator = "=";
    private const int NoFailures = 0;
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    internal static void Capture(PeerDiscoveryNativeCluster cluster, List<Exception> failures)
    {
        if (failures.Count != NoFailures)
        {
            foreach (var node in cluster.Nodes)
            {
                node.RetainOnFailure();
                ReplicaMaterializerLifecycleErrors.Attempt(() => Capture(node, failures), failures);
            }
            ReplicaMaterializerLifecycleErrors.Attempt(() =>
            {
                var roots = string.Join(RootSeparator,
                    cluster.Nodes.Select(node => node.Configuration.LocalId + NodeSeparator + node.EvidenceDirectory));
                failures.First().Data[RetainedRoots] = roots;
                Console.Error.WriteLine(RetainedRoots + roots);
            }, failures);
        }
    }

    private static void Capture(PeerDiscoveryNativeNode node, List<Exception> failures)
    {
        var state = typeof(ReplicaConsensus).GetField(ConsensusStateField, PrivateInstance)?.GetValue(node.Consensus)
            ?? throw new InvalidOperationException(ShapeChanged);
        if (state.GetType().FullName != ReplicaStateType)
        {
            throw new InvalidOperationException(ShapeChanged);
        }
        ReadOriginal(state, failures);
        ReadOriginal(node.Materializer, failures);
    }

    private static void ReadOriginal(object owner, List<Exception> failures)
    {
        var field = owner.GetType().GetField(OriginalFailureField, PrivateInstance)
            ?? throw new InvalidOperationException(ShapeChanged);
        if (field.FieldType != typeof(Exception))
        {
            throw new InvalidOperationException(ShapeChanged);
        }
        if (field.GetValue(owner) is Exception original)
        {
            ReplicaMaterializerLifecycleErrors.Add(original, failures);
        }
    }
}
