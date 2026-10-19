using System.Net;
using System.Net.Sockets;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class OrleansNodeAddressResolver
{
    internal static Task<IPAddress> ResolveAsync(NodeOptions options, CancellationToken cancellationToken)
        => ResolveAsync(options.SiloAddress, cancellationToken);

    internal static async Task<IPAddress> ResolveAsync(string siloAddress, CancellationToken cancellationToken)
    {
        var addresses = IPAddress.TryParse(siloAddress, out var literal) ? [literal]
            : await Dns.GetHostAddressesAsync(siloAddress, cancellationToken).ConfigureAwait(false);
        return addresses.FirstOrDefault(address => address.AddressFamily == AddressFamily.InterNetwork
            && !address.Equals(IPAddress.Any))
            ?? throw new InvalidOperationException(OrleansNodeProtocol.InvalidSiloAddress);
    }

}
