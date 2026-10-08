using System.Net;
using System.Net.Sockets;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

/// <summary>Creates only exact namespace-local rules for two admitted native voter IPv4 endpoints.</summary>
internal sealed class ReplicaIsolationRules
{
    private const int AddressFirstOctet = 0;
    private const int MulticastFirstOctet = 224;
    private const string OperationFormat = "N";
    private readonly string[] peers;
    internal string InputChain { get; }
    internal string OutputChain { get; }

    internal ReplicaIsolationRules(Guid operation, IReadOnlyList<string> peerAddresses)
    {
        if (operation == Guid.Empty || peerAddresses.Count != ReplicaIsolationProtocol.PeerCount)
        { throw new InvalidOperationException("Namespace isolation requires one operation and exactly two peers."); }
        peers = peerAddresses.Select(RequireIpv4).ToArray();
        if (peers.Distinct(StringComparer.Ordinal).Count() != ReplicaIsolationProtocol.PeerCount)
        { throw new InvalidOperationException("Namespace isolation requires distinct voter endpoints."); }
        var suffix = operation.ToString(OperationFormat)[..ReplicaIsolationProtocol.ChainSuffixLength];
        InputChain = ReplicaIsolationProtocol.InputPrefix + suffix;
        OutputChain = ReplicaIsolationProtocol.OutputPrefix + suffix;
    }

    internal IEnumerable<string[]> Installation()
    {
        yield return [ReplicaIsolationProtocol.Create, InputChain];
        yield return [ReplicaIsolationProtocol.Create, OutputChain];
        foreach (var peer in peers)
        {
            foreach (var port in new[] { ReplicaIsolationProtocol.SourcePort, ReplicaIsolationProtocol.DestinationPort })
            {
                yield return DropRule(InputChain, ReplicaIsolationProtocol.Source, peer, port);
                yield return DropRule(OutputChain, ReplicaIsolationProtocol.Destination, peer, port);
            }
        }
        yield return [ReplicaIsolationProtocol.Insert, ReplicaIsolationProtocol.Input, ReplicaIsolationProtocol.FirstRule,
            ReplicaIsolationProtocol.Jump, InputChain];
        yield return [ReplicaIsolationProtocol.Insert, ReplicaIsolationProtocol.Output, ReplicaIsolationProtocol.FirstRule,
            ReplicaIsolationProtocol.Jump, OutputChain];
    }

    internal static string[] NativeArguments(string fullContainerId, IReadOnlyList<string> rule)
    {
        if (fullContainerId.Length != ReplicaIsolationProtocol.FullContainerIdLength
            || !fullContainerId.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f'))
        { throw new InvalidOperationException("Namespace mutation requires an inspected full container identity."); }
        return [ReplicaIsolationProtocol.Exec, ReplicaIsolationProtocol.User, ReplicaIsolationProtocol.RootUser,
            fullContainerId, ReplicaIsolationProtocol.Timeout, ReplicaIsolationProtocol.TermSignal,
            ReplicaIsolationProtocol.KillBound, ReplicaIsolationProtocol.ExecutionBound, ReplicaIsolationProtocol.Iptables,
            ReplicaIsolationProtocol.Wait, ReplicaIsolationProtocol.WaitBound, .. rule];
    }

    private static string[] DropRule(string chain, string direction, string peer, string port)
        => [ReplicaIsolationProtocol.Append, chain, ReplicaIsolationProtocol.Protocol, ReplicaIsolationProtocol.Tcp,
            direction, peer, port, ReplicaIsolationProtocol.SiloPort, ReplicaIsolationProtocol.Jump, ReplicaIsolationProtocol.Drop];

    internal static string RequireIpv4(string value)
    {
        if (!IPAddress.TryParse(value, out var address) || address.AddressFamily != AddressFamily.InterNetwork
            || IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.Broadcast)
            || address.GetAddressBytes()[AddressFirstOctet] >= MulticastFirstOctet || !string.Equals(value, address.ToString(), StringComparison.Ordinal))
        { throw new InvalidOperationException("Namespace isolation requires a canonical native unicast IPv4 voter endpoint."); }
        return address.ToString();
    }
}
