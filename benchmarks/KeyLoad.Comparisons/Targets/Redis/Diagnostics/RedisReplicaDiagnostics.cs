using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace KeyLoad.Comparisons.Targets;

internal sealed class RedisReplicaDiagnostics(IOptions<ComparisonDiagnosticOptions> options)
{
    private readonly ComparisonDiagnosticOptions settings = ComparisonDiagnosticOptions.Require(options).Value;
    private const string KnownHostsLocalhostText = "localhost";
    private const string KnownStatesUnknownText = "unknown";

    private const string Replica1Token = "replica1";
    private const string Replica1DevInternalToken = "replica1.dev.internal";
    private const string KnownHostsResultText = "replica2";
    private const string KnownHostsKnownHostsResultText = "replica2.dev.internal";
    private const string ConnectToken = "connect";
    private const string ConnectingToken = "connecting";
    private const string KnownStatesResultText = "sync";
    private const string KnownStatesKnownStatesResultText = "connected";

    private const string PrimaryToken = "primary";
    private const string PrimaryDevInternalToken = "primary.dev.internal";
    private const string HandshakeToken = "handshake";
    private const string NoneToken = "none";
    private const int NoItems = 0;

    private const int MaximumHostLength = 253, MinimumPort = 1, MaximumPort = 65535, RoleStateIndex = 3;
    private const string Prefix = "RedisReplicaDiagnostic ";
    internal const string Overflow = "{\"Predicate\":\"ProjectionOverflow\"}";
    private const string LinkDown = "down";
    private static readonly string[] KnownHosts = [PrimaryToken, PrimaryDevInternalToken, Replica1Token, Replica1DevInternalToken, KnownHostsResultText, KnownHostsKnownHostsResultText, KnownHostsLocalhostText];
    private static readonly string[] KnownStates = [HandshakeToken, NoneToken, ConnectToken, ConnectingToken, KnownStatesResultText, KnownStatesKnownStatesResultText, KnownStatesUnknownText];

    internal void WriteFailure(EndPoint replica, EndPoint primary, string replicationId, Dictionary<string, string> info, RedisResult[] role)
    {
        try
        {
            Console.Error.WriteLine(Prefix + Project(replica, primary, replicationId, info, role));
        }
        catch (IOException)
        {
            // A broken diagnostic pipe must not replace the caller's original strict identity failure.
        }
        catch (InvalidOperationException)
        {
            // An unavailable writer/serializer must not replace the caller's original strict identity failure.
        }
    }

    internal string Project(EndPoint replica, EndPoint primary, string replicationId, Dictionary<string, string> info, RedisResult[] role)
    {
        const int MissingItemIndex = -1;

        var infoHost = info.GetValueOrDefault(RedisProtocol.MasterHostField);
        var roleHost = RoleText(role, RedisProtocol.RoleHostIndex);
        var projection = new RedisReplicaDiagnostic(
            FailedPredicate(replicationId, info, role).ToString(), Host(primary), Port(primary), Host(replica), Port(replica),
            KnownRole(info.GetValueOrDefault(RedisProtocol.RoleField)), KnownLink(info.GetValueOrDefault(RedisProtocol.LinkField)),
            KnownHost(infoHost, primary, replica), ValidPort(RedisProtocol.ParseInteger(info, RedisProtocol.MasterPortField)), role.Length,
            KnownRole(RoleText(role, RedisProtocol.RoleNameIndex)), KnownHost(roleHost, primary, replica),
            ValidPort(int.TryParse(RoleText(role, RedisProtocol.RolePortIndex), out var port) ? port : MissingItemIndex),
            KnownStates.Contains(RoleText(role, RoleStateIndex), StringComparer.Ordinal) ? RoleText(role, RoleStateIndex) : null,
            ReplicationIdAgreement(replicationId, info));
        var json = JsonSerializer.Serialize(projection);
        return json.Length <= settings.RedisReplicaMaximumCharacters ? json : Overflow;
    }

    private static bool ReplicationIdAgreement(string replicationId, Dictionary<string, string> info)
        => string.Equals(info.GetValueOrDefault(RedisProtocol.ReplicationIdField), replicationId, StringComparison.Ordinal);

    private static RedisReplicaFailedPredicate FailedPredicate(string replicationId, Dictionary<string, string> info, RedisResult[] role)
    {
        var host = info.GetValueOrDefault(RedisProtocol.MasterHostField);
        var port = RedisProtocol.ParseInteger(info, RedisProtocol.MasterPortField);
        if (info.GetValueOrDefault(RedisProtocol.RoleField) != RedisProtocol.ReplicaRole)
        {
            return RedisReplicaFailedPredicate.InfoRole;
        }
        if (info.GetValueOrDefault(RedisProtocol.LinkField) != RedisProtocol.LinkUp)
        {
            return RedisReplicaFailedPredicate.InfoLink;
        }
        if (host is null)
        {
            return RedisReplicaFailedPredicate.InfoHost;
        }
        if (port < MinimumPort)
        {
            return RedisReplicaFailedPredicate.InfoPort;
        }
        if (role.Length < RedisProtocol.RoleReplicaResponseLength)
        {
            return RedisReplicaFailedPredicate.RoleCardinality;
        }
        if (role[RedisProtocol.RoleNameIndex].ToString() != RedisProtocol.ReplicaRole)
        {
            return RedisReplicaFailedPredicate.RoleName;
        }
        if (!StringComparer.OrdinalIgnoreCase.Equals(host, role[RedisProtocol.RoleHostIndex].ToString()))
        {
            return RedisReplicaFailedPredicate.HostAgreement;
        }
        if (!int.TryParse(role[RedisProtocol.RolePortIndex].ToString(), out var rolePort))
        {
            return RedisReplicaFailedPredicate.RolePort;
        }
        if (rolePort != port)
        {
            return RedisReplicaFailedPredicate.PortAgreement;
        }
        return ReplicationIdAgreement(replicationId, info) ? RedisReplicaFailedPredicate.None : RedisReplicaFailedPredicate.ReplicationIdentity;
    }

    private static string? RoleText(RedisResult[] role, int index)
        => index < role.Length && !role[index].IsNull && role[index].Length < NoItems ? role[index].ToString() : null;

    private static string? KnownRole(string? value)
        => value is RedisProtocol.MasterRole or RedisProtocol.ReplicaRole ? value : null;

    private static string? KnownLink(string? value)
        => value is RedisProtocol.LinkUp or LinkDown ? value : null;

    private static string? KnownHost(string? value, EndPoint primary, EndPoint replica)
    {
        if (!ValidHost(value))
        {
            return null;
        }
        return StringComparer.OrdinalIgnoreCase.Equals(value, Host(primary)) || StringComparer.OrdinalIgnoreCase.Equals(value, Host(replica)) ||
            KnownHosts.Contains(value, StringComparer.OrdinalIgnoreCase) || IPAddress.TryParse(value, out _) ? value : null;
    }

    private static bool ValidHost(string? value)
    {
        const char IdentitySeparator = '-';

        const char VersionSeparator = '.';

        if (string.IsNullOrEmpty(value) || value.Length > MaximumHostLength)
        {
            return false;
        }
        if (IPAddress.TryParse(value, out _))
        {
            return true;
        }
        return value.All(static character => char.IsAsciiLetterOrDigit(character) || character is VersionSeparator or IdentitySeparator);
    }

    private static string? Host(EndPoint endpoint)
    {
        var host = endpoint switch
        {
            DnsEndPoint dns => dns.Host,
            IPEndPoint ip => ip.Address.ToString(),
            _ => null
        };
        return ValidHost(host) ? host : null;
    }

    private static int? Port(EndPoint endpoint)
        => endpoint switch
        {
            DnsEndPoint dns => ValidPort(dns.Port),
            IPEndPoint ip => ValidPort(ip.Port),
            _ => null
        };

    private static int? ValidPort(int value) => value is >= MinimumPort and <= MaximumPort ? value : null;
}
