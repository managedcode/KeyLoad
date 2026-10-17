using System.Collections.Immutable;
using System.Net;
using KeyLoad.Server;

namespace KeyLoad.CrashHost.Features.ClusterRouting;

/// <summary>Owns native Orleans port reservations for the original six movement owner slots.</summary>
internal sealed class ControlledPartitionMovementLoopbackListeners : IDisposable
{
    private const int VotersPerOwner = 3;
    private const int Owners = 2;
    private const int FirstControlVoter = 0;
    private readonly global::Orleans.TestingHost.TestClusterPortAllocator ports = new();
    private readonly ImmutableArray<IPEndPoint> siloEndpoints;
    private bool closed;

    /// <summary>Retains native Orleans port reservations for the original six owner slots.</summary>
    public ControlledPartitionMovementLoopbackListeners()
    {
        try
        {
            var (siloPort, httpPort) = ports.AllocateConsecutivePortPairs(VotersPerOwner * Owners);
            var native = ImmutableArray.CreateBuilder<IPEndPoint>(VotersPerOwner * Owners);
            var origins = ImmutableArray.CreateBuilder<string>(VotersPerOwner * Owners);
            for (var index = FirstControlVoter; index < VotersPerOwner * Owners; index++)
            {
                native.Add(new(IPAddress.Loopback, checked(siloPort + index)));
                origins.Add(new UriBuilder(Uri.UriSchemeHttp, IPAddress.Loopback.ToString(), checked(httpPort + index))
                    .Uri.GetLeftPart(UriPartial.Authority));
            }
            siloEndpoints = native.MoveToImmutable();
            ControlOrigins = origins.Take(VotersPerOwner).ToImmutableArray();
            DestinationOrigins = origins.Skip(VotersPerOwner).ToImmutableArray();
        }
        catch (Exception primary)
        {
            var failures = new List<Exception> { primary };
            ServerFailureObserver.Observe(Dispose, failures);
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }

    /// <summary>Three distinct original control-owner origins in immutable voter order.</summary>
    public ImmutableArray<string> ControlOrigins
    {
        get;
    }
    /// <summary>Three distinct original destination-owner origins in immutable voter order.</summary>
    public ImmutableArray<string> DestinationOrigins
    {
        get;
    }
    /// <summary>The original native primary endpoint reservation; no generation is inferred.</summary>
    public IPEndPoint SiloEndpoint => NativeEndpoint(FirstControlVoter);

    /// <summary>Returns one immutable endpoint allocated by the native Orleans owner.</summary>
    /// <param name="index">The original control-then-destination slot.</param>
    public IPEndPoint NativeEndpoint(int index)
    {
        ObjectDisposedException.ThrowIf(closed, this);
        var actual = siloEndpoints[index];
        return new(actual.Address, actual.Port);
    }

    /// <summary>Checks the retained native reservations before the original child opens its hosts.</summary>
    internal void JoinForChild() => ObjectDisposedException.ThrowIf(closed, this);

    /// <summary>Checks the same native reservations after the original child has joined.</summary>
    internal void OpenAfterChild() => ObjectDisposedException.ThrowIf(closed, this);

    /// <summary>Releases the original native allocator after the owning callers and hosts join.</summary>
    public void Dispose()
    {
        if (closed)
        {
            return;
        }
        closed = true;
        ports.Dispose();
    }
}
