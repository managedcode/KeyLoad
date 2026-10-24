using System.Net;
using KeyLoad.Server;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.UnitTests.OrleansExecution;

[RequestCqrsHostedDataSource]
[NotInParallel(RequestCqrsFixtureScheduling.SharedSession)]
internal sealed class ServerApplicationOwnershipTests(RequestCqrsHostedRuntimeFixture fixture)
{
    [Test]
    public async Task ServerOwnsApplicationAssetsAndRespondsAfterDatabaseStartup()
    {
        var environment = fixture.Services.GetRequiredService<IWebHostEnvironment>();
        await Assert.That(environment.ApplicationName).IsEqualTo(typeof(ServerConfiguration).Assembly.GetName().Name);
        using var http = new HttpClient { BaseAddress = fixture.PublicEndpoint, Timeout = fixture.Timing.StartupTimeout };
        using var response = await http.GetAsync(new Uri("/health/ready", UriKind.Relative), TestContext.Current!.Execution.CancellationToken);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }
}
