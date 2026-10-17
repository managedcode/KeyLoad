using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace KeyLoad.AppHost.Features.ClusterReplication;

/// <summary>Builds only the original closed namespace rule grammar against actual admitted peer IPv4 addresses.</summary>
internal static class ReplicaIsolationCommandRule
{
    private const string Input = "INPUT";
    private const string Output = "OUTPUT";
    private const string SiloPort = "11111";
    private const int FirstOctet = 0;
    private const int Multicast = 224;
    private const string AppendRule = "-A";
    private const string ProtocolArgument = "-p";
    private const string TcpProtocol = "tcp";
    private const string SourceAddress = "-s";
    private const string DestinationAddress = "-d";
    private const string SourcePort = "--sport";
    private const string DestinationPort = "--dport";
    private const string JumpArgument = "-j";
    private const string DropTarget = "DROP";
    private const string CreateChain = "-N";
    private const string InsertRule = "-I";
    private const string FirstRulePosition = "1";
    private const string DeleteRule = "-D";
    private const string FlushChain = "-F";
    private const string DeleteChain = "-X";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
    };

    internal static ReplicaIsolationCommandProtocol.Rule? Read(string? json)
    {
        if (json is null || json.Length > ReplicaIsolationCommandProtocol.MaximumRuleCharacters
            || Encoding.UTF8.GetByteCount(json) > ReplicaIsolationCommandProtocol.MaximumRuleBytes)
        {
            return null;
        }
        try
        {
            return JsonSerializer.Deserialize<ReplicaIsolationCommandProtocol.Rule>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal static string[]? Build(ReplicaIsolationCommandProtocol.Rule rule, IReadOnlySet<string> peers)
    {
        if (!Enum.IsDefined(rule.Action) || rule.ChainSuffix is null
            || rule.ChainSuffix.Length != ReplicaIsolationCommandProtocol.SuffixLength
            || !rule.ChainSuffix.All(character => character is >= ContainerFaultCommandProtocol.DecimalDigitFirst and <= ContainerFaultCommandProtocol.DecimalDigitLast
                or >= ContainerFaultCommandProtocol.LowerHexDigitFirst and <= ContainerFaultCommandProtocol.LowerHexDigitLast))
        {
            return null;
        }
        var chain = (rule.Input ? ReplicaIsolationCommandProtocol.InputPrefix : ReplicaIsolationCommandProtocol.OutputPrefix)
            + rule.ChainSuffix;
        if (rule.Action == ReplicaIsolationCommandProtocol.Action.AppendDrop)
        {
            if (rule.PeerAddress is null || !peers.Contains(rule.PeerAddress) || !IsCanonicalPeer(rule.PeerAddress))
            {
                return null;
            }
            return [AppendRule, chain, ProtocolArgument, TcpProtocol, rule.Input ? SourceAddress : DestinationAddress, rule.PeerAddress,
                rule.SourcePort ? SourcePort : DestinationPort, SiloPort, JumpArgument, DropTarget];
        }
        if (rule.PeerAddress is not null || rule.SourcePort)
        {
            return null;
        }
        return rule.Action switch
        {
            ReplicaIsolationCommandProtocol.Action.CreateChain => [CreateChain, chain],
            ReplicaIsolationCommandProtocol.Action.InsertJump => [InsertRule, rule.Input ? Input : Output, FirstRulePosition, JumpArgument, chain],
            ReplicaIsolationCommandProtocol.Action.DeleteJump => [DeleteRule, rule.Input ? Input : Output, JumpArgument, chain],
            ReplicaIsolationCommandProtocol.Action.FlushChain => [FlushChain, chain],
            ReplicaIsolationCommandProtocol.Action.DeleteChain => [DeleteChain, chain],
            _ => null
        };
    }

    internal static bool IsCanonicalPeer(string value)
        => IPAddress.TryParse(value, out var address) && address.AddressFamily == AddressFamily.InterNetwork
            && !IPAddress.IsLoopback(address) && !address.Equals(IPAddress.Any) && !address.Equals(IPAddress.Broadcast)
            && address.GetAddressBytes()[FirstOctet] < Multicast && value == address.ToString();
}
