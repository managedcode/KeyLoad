using System.Net;
using System.Net.Sockets;

namespace KeyLoad.Server;

/// <summary>Explicit private native seed selection for an owned loopback test host.</summary>
internal sealed class NativeLocalhostClusteringOptions
{
    private readonly byte[] address;
    private readonly int port;

    internal NativeLocalhostClusteringOptions(IPEndPoint primarySiloEndpoint)
    {
        ArgumentNullException.ThrowIfNull(primarySiloEndpoint);
        if (primarySiloEndpoint.AddressFamily != AddressFamily.InterNetwork
            || !IPAddress.IsLoopback(primarySiloEndpoint.Address)
            || primarySiloEndpoint.Port <= IPEndPoint.MinPort
            || primarySiloEndpoint.Port > IPEndPoint.MaxPort)
        {
            throw new ArgumentOutOfRangeException(nameof(primarySiloEndpoint));
        }
        address = primarySiloEndpoint.Address.GetAddressBytes();
        port = primarySiloEndpoint.Port;
    }

    internal IPEndPoint PrimarySiloEndpoint => new(new IPAddress(address.ToArray()), port);
}
