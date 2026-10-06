using System.Net;
using System.Text;
using KeyLoad.Orleans;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class ReplicaMembershipReadiness
{
    private const char HostColonCharacter = ':';
    private const string EndpointKeyComparisonText = ":";

    private const int ExpectedActiveSilos = 6;
    private const int MaximumRows = 48;
    private const int MaximumAddressBytes = 256;

    internal static async Task<MembershipReadinessSnapshot?> ReadNodeAsync(bool siloJoined, IHost? host,
        NodeOptions options, CancellationToken token)
    {
        if (!siloJoined || host is null || options.MembershipAuthority.Mode == MembershipAuthoritySettingsProtocol.Local)
        { return null; }
        try
        {
            return await ReadAsync(host.Services.GetRequiredService<IMembershipTable>(), options,
                host.Services.GetRequiredService<ILocalSiloDetails>().SiloAddress, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        { throw; }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        { return null; }
    }

    internal static async Task<MembershipReadinessSnapshot?> ReadAsync(IMembershipTable table, NodeOptions options,
        SiloAddress localAddress, CancellationToken token)
    {
        const int IndexInitialValue = 0;
        const int EmptyCanonicalLength = 0;
        const int VersionSingleItemCount = 1;

        var expected = await ExpectedEndpointsAsync(options, token).ConfigureAwait(false);
        if (expected.Count != ExpectedActiveSilos)
        { return null; }
        var view = await table.ReadAllAsync(token).ConfigureAwait(false);
        var active = view.Members.Where(member => member.Item1.Status == SiloStatus.Active).ToArray();
        if (view.Members.Count is < ExpectedActiveSilos or > MaximumRows
            || active.Length != ExpectedActiveSilos || active.All(member => member.Item1.SiloAddress != localAddress))
        { return null; }
        var addresses = new string[ExpectedActiveSilos];
        var observedEndpoints = new HashSet<string>(StringComparer.Ordinal);
        for (var index = IndexInitialValue; index < active.Length; index++)
        {
            var address = active[index].Item1.SiloAddress;
            var canonical = address.ToParsableString();
            if (SiloAddress.FromParsableString(canonical).ToParsableString() != canonical)
            { return null; }
            if (canonical.Length == EmptyCanonicalLength || Encoding.UTF8.GetByteCount(canonical) > MaximumAddressBytes
                || !expected.Contains(EndpointKey(address.Endpoint.Address, address.Endpoint.Port))
                || !observedEndpoints.Add(EndpointKey(address.Endpoint.Address, address.Endpoint.Port)))
            { return null; }
            addresses[index] = canonical;
        }
        if (!observedEndpoints.SetEquals(expected))
        { return null; }
        var fingerprint = ReplicaMembershipFingerprint.Compute(addresses);
        return new(VersionSingleItemCount, ExpectedActiveSilos, view.Members.Count, fingerprint);
    }

    private static async Task<HashSet<string>> ExpectedEndpointsAsync(NodeOptions options, CancellationToken token)
    {
        var hosts = options.Peers.Select(origin => new Uri(origin).DnsSafeHost).ToList();
        if (options.MembershipAuthority.Mode == MembershipAuthoritySettingsProtocol.Authority)
        { hosts.AddRange(options.MembershipAuthority.TrustedGroupSiloEndpoints.Select(Host)); }
        else if (options.MembershipAuthority.Mode == MembershipAuthoritySettingsProtocol.Proxy)
        { hosts.AddRange(options.MembershipAuthority.AuthorityEndpoints.Select(origin => new Uri(origin).DnsSafeHost)); }
        var endpoints = new HashSet<string>(StringComparer.Ordinal);
        foreach (var host in hosts)
        {
            var addresses = await Dns.GetHostAddressesAsync(host, token).ConfigureAwait(false);
            foreach (var address in addresses.Where(address => address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork))
            { endpoints.Add(EndpointKey(address, MembershipAuthoritySettingsProtocol.NativeSiloPort)); }
        }
        return endpoints;
    }

    private static string Host(string endpoint) => endpoint[..endpoint.LastIndexOf(HostColonCharacter)];

    private static string EndpointKey(IPAddress address, int port) => address + EndpointKeyComparisonText +
        port.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
