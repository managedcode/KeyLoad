using System.Net;
using System.Net.Sockets;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class OrleansNodeAddressResolver
{
    internal static async Task<IPAddress> ResolveAsync(NodeOptions options, CancellationToken cancellationToken)
    {
        var addresses = IPAddress.TryParse(options.SiloAddress, out var literal) ? [literal]
            : await Dns.GetHostAddressesAsync(options.SiloAddress, cancellationToken).ConfigureAwait(false);
        var allowLoopback = options.Peers.All(peer => new Uri(peer).IsLoopback);
        return addresses.FirstOrDefault(address => address.AddressFamily == AddressFamily.InterNetwork
            && !address.Equals(IPAddress.Any) && (allowLoopback || !IPAddress.IsLoopback(address)))
            ?? throw new InvalidOperationException(OrleansNodeProtocol.InvalidSiloAddress);
    }

}
