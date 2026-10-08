using System.Collections.Immutable;
using System.Net;
using System.Net.Sockets;
using KeyLoad.Server;
using ManagedCode.Communication.CQRS;

namespace KeyLoad.CrashHost.Features.ClusterRouting;

/// <summary>Owns actual listener-discovered origins for direct native movement admission fixtures.</summary>
internal sealed class ControlledPartitionMovementLoopbackListeners : IDisposable
{
    private const int VotersPerOwner = 3;
    private const int Owners = 2;
    private const int FirstControlVoter = 0;
    private const int NativeSiloPort = 11_111;
    private const int EphemeralPort = 0;
    private const int ListenerBacklog = 1;
    private const byte LoopbackNetworkOctet = 127;
    private const byte EmptyOctet = 0;
    private const int AddressStep = 1;
    private readonly List<Socket> listeners = [];
    private readonly ImmutableArray<IPEndPoint> siloEndpoints;
    private bool closed;

    /// <summary>Binds original native listeners and discovers every admitted loopback origin.</summary>
    public ControlledPartitionMovementLoopbackListeners()
    {
        try
        {
            var native = ImmutableArray.CreateBuilder<IPEndPoint>(VotersPerOwner * Owners);
            var origins = ImmutableArray.CreateBuilder<string>(VotersPerOwner * Owners);
            for (var index = FirstControlVoter; index < VotersPerOwner * Owners; index++)
            {
                var address = new IPAddress(new byte[] {LoopbackNetworkOctet, EmptyOctet, EmptyOctet,
                    checked((byte)(index + AddressStep))});
                native.Add((IPEndPoint)CreateListener(address, NativeSiloPort).LocalEndPoint!);
                var endpoint = (IPEndPoint)CreateListener(address, EphemeralPort).LocalEndPoint!;
                origins.Add(new UriBuilder(Uri.UriSchemeHttp, endpoint.Address.ToString(), endpoint.Port)
                    .Uri.GetLeftPart(UriPartial.Authority));
            }
            siloEndpoints = native.MoveToImmutable();
            ControlOrigins = origins.Take(VotersPerOwner).ToImmutableArray();
            DestinationOrigins = origins.Skip(VotersPerOwner).ToImmutableArray();
        }
        catch (Exception primary)
        {
            var failures = new List<Exception> { primary };
            try
            { Dispose(); }
            catch (Exception cleanup) when (CqrsRuntimeFailures.FindFatal(cleanup) is null) { failures.Add(cleanup); }
            catch (Exception cleanup) when (CqrsRuntimeFailures.FindFatal(cleanup) is not null) { failures.Add(cleanup); }
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }

    /// <summary>Three actual distinct control-owner HTTP origins, in immutable voter order.</summary>
    public ImmutableArray<string> ControlOrigins { get; }
    /// <summary>Three actual distinct destination-owner HTTP origins, in immutable voter order.</summary>
    public ImmutableArray<string> DestinationOrigins { get; }
    /// <summary>The actual bound native source-caller silo-port endpoint used by peer pins.</summary>
    public IPEndPoint SiloEndpoint => NativeEndpoint(FirstControlVoter);

    /// <summary>Returns an actual discovered native endpoint for one immutable voter slot.</summary>
    /// <param name="index">The original control-then-destination listener slot.</param>
    public IPEndPoint NativeEndpoint(int index)
    {
        var actual = siloEndpoints[index];
        return new(actual.Address, actual.Port);
    }

    private Socket CreateListener(IPAddress address, int port)
    {
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        listeners.Add(socket);
        socket.Bind(new IPEndPoint(address, port));
        socket.Listen(ListenerBacklog);
        return socket;
    }

    /// <summary>Closes every original native listener, retaining all disposal failures.</summary>
    public void Dispose()
    {
        if (closed)
        { return; }
        closed = true;
        var failures = new List<Exception>();
        foreach (var socket in listeners)
        {
            try
            { socket.Dispose(); }
            catch (Exception error) when (CqrsRuntimeFailures.FindFatal(error) is null) { failures.Add(error); }
            catch (Exception error) when (CqrsRuntimeFailures.FindFatal(error) is not null) { failures.Add(error); }
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
