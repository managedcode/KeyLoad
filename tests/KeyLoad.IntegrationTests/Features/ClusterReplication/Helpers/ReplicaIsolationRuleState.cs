namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

/// <summary>Reconciles native filter-table output only against the exact admitted per-operation chains and peers.</summary>
internal static class ReplicaIsolationRuleState
{
    private const string AppendPrefix = "-A ";
    private const string CreatePrefix = "-N ";
    private const string SourceOption = "-s";
    private const string DestinationOption = "-d";
    private const string MatchTcp = "-p tcp -m tcp";
    private const string HostMask = "/32";
    private const int OperationIndex = 0;
    private const int ChainIndex = 1;
    private const int DirectionIndex = 4;
    private const int PeerIndex = 5;
    private const int PortIndex = 6;
    private const int DropArgumentCount = 10;

    internal static void RequireInstalled(string native, IReplicaIsolationRulePlan plan)
    {
        var expected = plan.Installation().Select(Canonical).ToHashSet(StringComparer.Ordinal);
        var lines = Lines(native);
        if (!expected.SetEquals(lines.Where(line => Owned(line, plan)))
            || lines.First(line => line.StartsWith(AppendPrefix + ReplicaIsolationProtocol.Input + " ", StringComparison.Ordinal))
                != Canonical([ReplicaIsolationProtocol.Insert, ReplicaIsolationProtocol.Input, ReplicaIsolationProtocol.FirstRule,
                    ReplicaIsolationProtocol.Jump, plan.InputChain])
            || lines.First(line => line.StartsWith(AppendPrefix + ReplicaIsolationProtocol.Output + " ", StringComparison.Ordinal))
                != Canonical([ReplicaIsolationProtocol.Insert, ReplicaIsolationProtocol.Output, ReplicaIsolationProtocol.FirstRule,
                    ReplicaIsolationProtocol.Jump, plan.OutputChain]))
        { throw new InvalidOperationException("The actual native filter table does not contain the exact owned isolation rules first."); }
    }

    internal static IEnumerable<string[]> Removal(string native, IReplicaIsolationRulePlan plan)
    {
        var lines = Lines(native);
        var expected = plan.Installation().Select(Canonical).ToHashSet(StringComparer.Ordinal);
        if (lines.Where(line => Owned(line, plan)).Any(line => !expected.Contains(line)))
        { throw new InvalidOperationException("Unexpected native rules refer to an owned namespace chain; ownership is unresolved."); }
        if (lines.Contains(AppendPrefix + ReplicaIsolationProtocol.Input + " -j " + plan.InputChain, StringComparer.Ordinal))
        { yield return [ReplicaIsolationProtocol.Delete, ReplicaIsolationProtocol.Input, ReplicaIsolationProtocol.Jump, plan.InputChain]; }
        if (lines.Contains(AppendPrefix + ReplicaIsolationProtocol.Output + " -j " + plan.OutputChain, StringComparer.Ordinal))
        { yield return [ReplicaIsolationProtocol.Delete, ReplicaIsolationProtocol.Output, ReplicaIsolationProtocol.Jump, plan.OutputChain]; }
        foreach (var chain in new[] { plan.InputChain, plan.OutputChain })
        {
            if (lines.Contains(CreatePrefix + chain, StringComparer.Ordinal))
            {
                yield return [ReplicaIsolationProtocol.Flush, chain];
                yield return [ReplicaIsolationProtocol.DeleteChain, chain];
            }
        }
    }

    private static string Canonical(string[] rule)
    {
        if (rule[OperationIndex] == ReplicaIsolationProtocol.Create)
        { return CreatePrefix + rule[ChainIndex]; }
        if (rule[OperationIndex] == ReplicaIsolationProtocol.Insert)
        { return AppendPrefix + rule[ChainIndex] + " -j " + rule[^1]; }
        if (rule.Length != DropArgumentCount || rule[DirectionIndex] is not (SourceOption or DestinationOption))
        { throw new InvalidOperationException("Only the frozen native namespace rule shape is supported."); }
        return AppendPrefix + rule[ChainIndex] + " " + rule[DirectionIndex] + " " + rule[PeerIndex] + HostMask
            + " " + MatchTcp + " " + rule[PortIndex] + " " + ReplicaIsolationProtocol.SiloPort + " -j " + ReplicaIsolationProtocol.Drop;
    }

    private static bool Owned(string line, IReplicaIsolationRulePlan plan)
        => line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(word => word == plan.InputChain || word == plan.OutputChain);
    private static string[] Lines(string native) => native.Split('\n', StringSplitOptions.RemoveEmptyEntries)
        .Select(line => line.Trim()).ToArray();
}
