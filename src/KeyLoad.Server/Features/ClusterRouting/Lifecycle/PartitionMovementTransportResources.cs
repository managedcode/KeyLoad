using System.Globalization;
using KeyLoad.Orleans;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Owns the HTTP connection and both configured address-pin lifetimes through joined disposal.</summary>
internal sealed class PartitionMovementTransportResources : IDisposable
{
    private const string SiloSeparator = ":";
    private readonly HttpClient http;
    private readonly SocketsHttpHandler handler;
    private readonly ReplicaMembershipAuthorityAddressPins controlPins;
    private readonly ReplicaMembershipAuthorityAddressPins destinationPins;

    internal PartitionMovementTransportResources(IOptions<NodeOptions> options, PartitionHost partition,
        IOptions<OrleansMembershipOptions> membership)
    {
        handler = new()
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            AutomaticDecompression = System.Net.DecompressionMethods.None
        };
        HttpClient? created = null;
        ReplicaMembershipAuthorityAddressPins? control = null;
        ReplicaMembershipAuthorityAddressPins? destination = null;
        try
        {
            created = new(handler, disposeHandler: false) { Timeout = Timeout.InfiniteTimeSpan };
            control = Pins(PhysicalOwnerConfiguredTuples.Control(options.Value, partition), membership);
            destination = Pins(PhysicalOwnerConfiguredTuples.Destination(options.Value, partition), membership);
            http = created;
            controlPins = control;
            destinationPins = destination;
        }
        catch (Exception primary)
        {
            var failures = new List<Exception> { primary };
            if (destination is not null)
            { ServerFailureObserver.Observe(destination.Dispose, failures); }
            if (control is not null)
            { ServerFailureObserver.Observe(control.Dispose, failures); }
            if (created is not null)
            { ServerFailureObserver.Observe(created.Dispose, failures); }
            ServerFailureObserver.Observe(handler.Dispose, failures);
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }

    internal HttpClient Http => http;
    internal ReplicaMembershipAuthorityAddressPins ControlPins => controlPins;
    internal ReplicaMembershipAuthorityAddressPins DestinationPins => destinationPins;

    private static ReplicaMembershipAuthorityAddressPins Pins(RegisteredPhysicalOwnerV1 entry,
        IOptions<OrleansMembershipOptions> membership)
        => new(entry.Endpoints.Select(value => new Uri(value).DnsSafeHost + SiloSeparator
            + MembershipAuthoritySettingsProtocol.NativeSiloPort.ToString(CultureInfo.InvariantCulture)).ToArray(), membership);

    public void Dispose()
    {
        var failures = new List<Exception>();
        try
        { http.Dispose(); }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        try
        { handler.Dispose(); }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        try
        { controlPins.Dispose(); }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        try
        { destinationPins.Dispose(); }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
