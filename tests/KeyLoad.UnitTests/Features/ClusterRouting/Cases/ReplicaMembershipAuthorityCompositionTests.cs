using KeyLoad.Orleans;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class ReplicaMembershipAuthorityCompositionTests
{
    [Test]
    public async Task ServerMapsTheNativeAuthorityRouteAndContainerOwnsItsLifetime()
    {
        var app = ServerConfiguration.Build(Arguments());
        ReplicaMembershipAuthorityEndpoint? endpoint = null;
        ReplicaMembershipAuthorityOwner? owner = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            endpoint = app.Services.GetRequiredService<ReplicaMembershipAuthorityEndpoint>();
            owner = app.Services.GetRequiredService<ReplicaMembershipAuthorityOwner>();
            await VerifyMappedRouteAsync(app).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(() => app.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        if (owner is { } ownedOwner && endpoint is { } ownedEndpoint)
        {
            await ServerFailureObserver.ObserveAsync(() => VerifyDisposedAsync(ownedOwner, ownedEndpoint), failures)
                .ConfigureAwait(false);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task VerifyMappedRouteAsync(WebApplication app)
    {
        var routes = app.DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText == ReplicaMembershipAuthorityProtocol.Path).ToArray();
        await Assert.That(routes.Length).IsEqualTo(1);
        await Assert.That(routes[0].Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.SingleOrDefault())
            .IsEqualTo(HttpMethods.Post);
        var context = new DefaultHttpContext { RequestServices = app.Services };
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = ReplicaMembershipAuthorityProtocol.Path;
        var handler = routes[0].RequestDelegate
            ?? throw new InvalidOperationException("The authority route handler is missing.");
        await handler(context).ConfigureAwait(false);
        await Assert.That(context.Response.StatusCode).IsEqualTo(StatusCodes.Status503ServiceUnavailable);
    }

    private static async Task VerifyDisposedAsync(ReplicaMembershipAuthorityOwner owner,
        ReplicaMembershipAuthorityEndpoint endpoint)
    {
        await Assert.That(owner.IsAdmissionClosed).IsTrue();
        await Assert.That(owner.IsDisposed).IsTrue();
        await Assert.That(endpoint.IsDisposed).IsTrue();
    }

    private static string[] Arguments()
    {
        var secret = Convert.ToBase64String(new byte[MembershipAuthoritySettingsProtocol.SecretBytes]);
        return
        [
            "--KeyLoad:DataDirectory=" + Path.Combine(Path.GetTempPath(),
                "membership-composition-" + Guid.NewGuid().ToString("N")),
            "--KeyLoad:PublicEndpoint=http://127.0.0.1:5100",
            "--KeyLoad:Peers:0=http://127.0.0.1:5100",
            "--KeyLoad:Peers:1=http://127.0.0.1:5101",
            "--KeyLoad:Peers:2=http://127.0.0.1:5102",
            "--KeyLoad:AllowLoopbackHttp=true",
            "--KeyLoad:PhysicalShardId=00000000-0000-0000-0000-000000000001",
            "--KeyLoad:Incarnation=00000000-0000-0000-0000-000000000002",
            "--KeyLoad:SigningKey=" + secret,
            "--KeyLoad:PeerSecret=" + secret,
            "--KeyLoad:RequestCqrsProbe:Enabled=false",
            "--KeyLoad:AdminKey=root.membership-composition-test-key-00000000000000000000"
        ];
    }
}
