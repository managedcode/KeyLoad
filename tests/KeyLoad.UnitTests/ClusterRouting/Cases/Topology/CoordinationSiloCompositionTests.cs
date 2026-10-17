using KeyLoad.Clustering;
using KeyLoad.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.Configuration;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class CoordinationSiloCompositionTests
{
    [Test]
    public async Task StorageFreeSiloRetainsNativeInfrastructureWithoutAdvertisingDatabaseGrains()
    {
        string[] arguments = [
            "--KeyLoad:CoordinationOnly=true", "--KeyLoad:SiloCount=5", "--KeyLoad:ClusterId=coordination-composition",
            "--KeyLoad:PublicEndpoint=http://127.0.0.1:5100", "--KeyLoad:SiloAddress=127.0.0.1", "--KeyLoad:SiloPort=11112",
            "--ConnectionStrings:OrleansMembership=UseDevelopmentStorage=true"
        ];
        await using var app = await ServerConfiguration.BuildAsync(arguments,
            startupCancellation: TestContext.Current!.Execution.CancellationToken).ConfigureAwait(false);
        var types = app.Services.GetRequiredService<IOptions<GrainTypeOptions>>().Value;
        await Assert.That(types.Classes.Count).IsGreaterThan(0);
        await Assert.That(types.Classes.Any(type => type.Assembly == typeof(ConnectionGrain).Assembly)).IsFalse();
        await Assert.That(types.Classes.Any(type => type.Assembly == typeof(PartitionHostApplicationOwner).Assembly)).IsFalse();
        await Assert.That(app.Services.GetService<PartitionHostApplicationOwner>()).IsNull();
        await Assert.That(app.Services.GetService<OrleansNode>()).IsNull();
    }
}
