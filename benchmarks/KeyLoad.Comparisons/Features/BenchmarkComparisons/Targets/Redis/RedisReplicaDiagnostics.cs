using System.Net;
using System.Text.Json;
using StackExchange.Redis;

namespace KeyLoad.Comparisons.Targets;

internal static class RedisReplicaDiagnostics
{
    internal const int MaximumCharacters = 4096;
    private const int MaximumHostLength = 253, MinimumPort = 1, MaximumPort = 65535, RoleStateIndex = 3;
    private const string Prefix = "RedisReplicaDiagnostic ";
    private const string Overflow = "{\"Predicate\":\"ProjectionOverflow\"}";
    private const string LinkDown = "down";
    private static readonly string[] KnownHosts = ["primary", "primary.dev.internal", "replica1", "replica1.dev.internal", "replica2", "replica2.dev.internal", "localhost"];
    private static readonly string[] KnownStates = ["handshake", "none", "connect", "connecting", "sync", "connected", "unknown"];

    internal static void WriteFailure(EndPoint replica, EndPoint primary, Dictionary<string, string> info, RedisResult[] role)
    {
        try
        {
            Console.Error.WriteLine(Prefix + Project(replica, primary, info, role));
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

    internal static string Project(EndPoint replica, EndPoint primary, Dictionary<string, string> info, RedisResult[] role)
    {
        var infoHost = info.GetValueOrDefault(RedisNativeProtocol.MasterHostField);
        var roleHost = RoleText(role, RedisNativeProtocol.RoleHostIndex);
        var projection = new RedisReplicaDiagnostic(
            FailedPredicate(primary, info, role).ToString(), Host(primary), Port(primary), Host(replica), Port(replica),
            KnownRole(info.GetValueOrDefault(RedisNativeProtocol.RoleField)), KnownLink(info.GetValueOrDefault(RedisNativeProtocol.LinkField)),
            KnownHost(infoHost, primary, replica), ValidPort(RedisNativeProtocol.ParseInteger(info, RedisNativeProtocol.MasterPortField)), role.Length,
            KnownRole(RoleText(role, RedisNativeProtocol.RoleNameIndex)), KnownHost(roleHost, primary, replica),
            ValidPort(int.TryParse(RoleText(role, RedisNativeProtocol.RolePortIndex), out var port) ? port : -1),
            KnownStates.Contains(RoleText(role, RoleStateIndex), StringComparer.Ordinal) ? RoleText(role, RoleStateIndex) : null);
        var json = JsonSerializer.Serialize(projection);
        return json.Length <= MaximumCharacters ? json : Overflow;
    }

    private static RedisReplicaFailedPredicate FailedPredicate(EndPoint primary, Dictionary<string, string> info, RedisResult[] role)
    {
        var host = info.GetValueOrDefault(RedisNativeProtocol.MasterHostField);
        var port = RedisNativeProtocol.ParseInteger(info, RedisNativeProtocol.MasterPortField);
        if (info.GetValueOrDefault(RedisNativeProtocol.RoleField) != RedisNativeProtocol.ReplicaRole)
        {
            return RedisReplicaFailedPredicate.InfoRole;
        }
        if (info.GetValueOrDefault(RedisNativeProtocol.LinkField) != RedisNativeProtocol.LinkUp)
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
        if (role.Length < RedisNativeProtocol.RoleReplicaResponseLength)
        {
            return RedisReplicaFailedPredicate.RoleCardinality;
        }
        if (role[RedisNativeProtocol.RoleNameIndex].ToString() != RedisNativeProtocol.ReplicaRole)
        {
            return RedisReplicaFailedPredicate.RoleName;
        }
        if (!StringComparer.OrdinalIgnoreCase.Equals(host, role[RedisNativeProtocol.RoleHostIndex].ToString()))
        {
            return RedisReplicaFailedPredicate.HostAgreement;
        }
        if (!int.TryParse(role[RedisNativeProtocol.RolePortIndex].ToString(), out var rolePort))
        {
            return RedisReplicaFailedPredicate.RolePort;
        }
        if (rolePort != port)
        {
            return RedisReplicaFailedPredicate.PortAgreement;
        }
        return RedisNativeProtocol.EndpointMatches(primary, host, port) ? RedisReplicaFailedPredicate.None : RedisReplicaFailedPredicate.ConfiguredPrimary;
    }

    private static string? RoleText(RedisResult[] role, int index)
        => index < role.Length && !role[index].IsNull && role[index].Length < 0 ? role[index].ToString() : null;

    private static string? KnownRole(string? value)
        => value is RedisNativeProtocol.MasterRole or RedisNativeProtocol.ReplicaRole ? value : null;

    private static string? KnownLink(string? value)
        => value is RedisNativeProtocol.LinkUp or LinkDown ? value : null;

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
        if (string.IsNullOrEmpty(value) || value.Length > MaximumHostLength)
        {
            return false;
        }
        if (IPAddress.TryParse(value, out _))
        {
            return true;
        }
        return value.All(static character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-');
    }

    private static string? Host(EndPoint endpoint)
    {
        var host = endpoint switch { DnsEndPoint dns => dns.Host, IPEndPoint ip => ip.Address.ToString(), _ => null };
        return ValidHost(host) ? host : null;
    }

    private static int? Port(EndPoint endpoint)
        => endpoint switch { DnsEndPoint dns => ValidPort(dns.Port), IPEndPoint ip => ValidPort(ip.Port), _ => null };

    private static int? ValidPort(int value) => value is >= MinimumPort and <= MaximumPort ? value : null;
}

internal sealed record RedisReplicaDiagnostic(string Predicate, string? PrimaryHost, int? PrimaryPort, string? ReplicaHost, int? ReplicaPort,
    string? InfoRole, string? InfoLink, string? InfoHost, int? InfoPort, int RoleCount, string? RoleName, string? RoleHost, int? RolePort, string? RoleState);

internal enum RedisReplicaFailedPredicate
{
    None,
    InfoRole,
    InfoLink,
    InfoHost,
    InfoPort,
    RoleCardinality,
    RoleName,
    HostAgreement,
    RolePort,
    PortAgreement,
    ConfiguredPrimary,
}
