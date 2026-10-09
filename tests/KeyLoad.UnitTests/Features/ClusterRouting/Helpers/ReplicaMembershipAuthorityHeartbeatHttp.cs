using System.Net;
using KeyLoad.Orleans;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Owns a real listener and production signed endpoint, using the fixture's actual native provider.</summary>
internal static class ReplicaMembershipAuthorityHeartbeatHttp
{
    private const int EphemeralPort = 0;

    internal static async Task WithAsync(TestDatabase fixture, ReplicaMembershipTable table,
        Func<ReplicaMembershipAuthorityClientTable, ReplicaMembershipAuthorityExchangeOptions, Task> verify,
        CancellationToken cancellationToken)
    {
        var settings = ReplicaMembershipAuthorityHeartbeatOptions.Node(fixture);
        var failures = new List<Exception>();
        try
        {
            await using var owner = new ReplicaMembershipAuthorityOwner();
            await WithOwnerAsync(settings, owner, table, verify, failures, cancellationToken);
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task WithOwnerAsync(IOptions<NodeOptions> settings,
        ReplicaMembershipAuthorityOwner owner, ReplicaMembershipTable table,
        Func<ReplicaMembershipAuthorityClientTable, ReplicaMembershipAuthorityExchangeOptions, Task> verify,
        List<Exception> failures, CancellationToken cancellationToken)
    {
        try
        {
            owner.Publish(table);
            using var endpoint = new ReplicaMembershipAuthorityEndpoint(settings, owner, TimeProvider.System,
                UnitRoutingOptions.Membership(), ReplicaExecutionTestOptions.Execution());
            await WithEndpointAsync(settings, owner, endpoint, verify, failures, cancellationToken);
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
    }

    private static async Task WithEndpointAsync(IOptions<NodeOptions> settings,
        ReplicaMembershipAuthorityOwner owner, ReplicaMembershipAuthorityEndpoint endpoint,
        Func<ReplicaMembershipAuthorityClientTable, ReplicaMembershipAuthorityExchangeOptions, Task> verify,
        List<Exception> failures, CancellationToken cancellationToken)
    {
        WebApplication? application = null;
        ReplicaMembershipAuthorityClientTable? client = null;
        try
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, EphemeralPort));
            application = builder.Build();
            application.MapPost(ReplicaMembershipAuthorityProtocol.Path, endpoint.HandleAsync);
            await application.StartAsync(cancellationToken);
            var addresses = application.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!;
            var exchange = ReplicaMembershipAuthorityHeartbeatOptions.Exchange(settings.Value, new Uri(addresses.Addresses.Single()));
            client = Client(exchange, ReplicaMembershipNativeTests.Entry().SiloAddress);
            await verify(client, exchange);
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        finally
        {
            if (client is not null)
            {
                try
                { await client.DisposeAsync(); }
                catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
                catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
            }
            await ServerFailureObserver.ObserveAsync(owner.StopAdmissionAndJoinAsync, failures);
            if (application is not null)
            {
                await ServerFailureObserver.ObserveAsync(() => application.StopAsync(cancellationToken), failures);
                try
                { await application.DisposeAsync(); }
                catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
                catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
            }
        }
    }

    internal static ReplicaMembershipAuthorityClientTable Client(ReplicaMembershipAuthorityExchangeOptions options,
        SiloAddress caller)
        => new(options with { CallerSiloAddress = caller.ToParsableString() }, UnitRoutingOptions.Membership(),
            ReplicaExecutionTestOptions.Execution());
}
