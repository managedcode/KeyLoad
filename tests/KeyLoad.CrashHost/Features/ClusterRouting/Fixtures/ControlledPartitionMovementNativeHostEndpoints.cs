using System.Net;
using System.Net.Sockets;
using KeyLoad.Server;

namespace KeyLoad.CrashHost.Features.ClusterRouting;

internal sealed class ControlledPartitionMovementNativeHostEndpoints : IDisposable
{
    internal const int GroupSize = 3;
    internal const int OwnerCount = GroupSize + GroupSize;
    private readonly List<Socket> reservations = [];
    private readonly IPEndPoint[] http = new IPEndPoint[OwnerCount];
    private readonly IPEndPoint[] silo = new IPEndPoint[OwnerCount];

    internal ControlledPartitionMovementNativeHostEndpoints()
    {
        try
        {
            for (var index = 0; index < OwnerCount; index++)
            {
                http[index] = Reserve();
                silo[index] = Reserve();
            }
        }
        catch (Exception primary)
        {
            var failures = new List<Exception> { primary };
            ServerFailureObserver.Observe(Dispose, failures);
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }

    internal IPEndPoint Primary => Copy(silo.First());
    internal IPEndPoint Silo(int index) => Copy(silo[index]);
    internal string Origin(int index)
        => new UriBuilder(Uri.UriSchemeHttp, IPAddress.Loopback.ToString(), http[index].Port)
            .Uri.GetLeftPart(UriPartial.Authority);

    private IPEndPoint Reserve()
    {
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        reservations.Add(socket);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, IPEndPoint.MinPort));
        return Copy((IPEndPoint)socket.LocalEndPoint!);
    }

    private static IPEndPoint Copy(IPEndPoint original)
        => new(new IPAddress(original.Address.GetAddressBytes()), original.Port);

    public void Dispose()
    {
        var failures = new List<Exception>();
        foreach (var original in reservations)
        { ServerFailureObserver.Observe(original.Dispose, failures); }
        reservations.Clear();
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
