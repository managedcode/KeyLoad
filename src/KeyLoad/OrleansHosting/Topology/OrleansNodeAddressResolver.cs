using System.Net;
using System.Net.Sockets;

namespace KeyLoad.Server.OrleansHosting;

internal static class OrleansNodeAddressResolver
{
    internal static Task<IPAddress> ResolveAsync(NodeOptions options, CancellationToken cancellationToken)
        => ResolveAsync(options.SiloAddress, cancellationToken);

    internal static async Task<IPAddress> ResolveAsync(string siloAddress, CancellationToken cancellationToken)
    {
        var addresses = IPAddress.TryParse(siloAddress, out var literal) ? [literal]
            : await Dns.GetHostAddressesAsync(siloAddress, cancellationToken);
        return addresses.FirstOrDefault(address => address.AddressFamily == AddressFamily.InterNetwork
            && !(address.Equals(IPAddress.Any) /* KLD0041: IPAddress exposes value equality through Equals(object); compare existing address references to reject the wildcard address without scalar boxing. */))
            ?? throw new InvalidOperationException(OrleansNodeProtocol.InvalidSiloAddress);
    }

}
