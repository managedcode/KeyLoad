using System.Net;
using System.Net.Sockets;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class OrleansNodeAddressResolver
{
    internal static Task<IPAddress> ResolveAsync(NodeOptions options, CancellationToken cancellationToken)
        => ResolveAsync(options.SiloAddress, options.Peers.All(peer => new Uri(peer).IsLoopback), cancellationToken);

    internal static async Task<IPAddress> ResolveAsync(string siloAddress, bool allowLoopback, CancellationToken cancellationToken)
    {
        var addresses = IPAddress.TryParse(siloAddress, out var literal) ? [literal]
            : await Dns.GetHostAddressesAsync(siloAddress, cancellationToken).ConfigureAwait(false);
        return addresses.FirstOrDefault(address => address.AddressFamily == AddressFamily.InterNetwork
            && !address.Equals(IPAddress.Any) && (allowLoopback || !IPAddress.IsLoopback(address)))
            ?? throw new InvalidOperationException(OrleansNodeProtocol.InvalidSiloAddress);
    }

}
