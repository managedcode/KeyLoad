using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.AppHost.Features.CodeQuality;
using KeyLoad.AppHost.Hosting;
using KeyLoad.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KeyLoad.IntegrationTests.Features.CodeQuality;

/// <summary>Exercises owned directory preparation and the actual native CLUSTER resource composition.</summary>
internal sealed class NativeCoverageClusterPreparationTests
{
    private const string RootPrefix = "keyload-coverage-preparation-";
    private static readonly byte[] Sentinel = "owned-output-sentinel"u8.ToArray();
    private const int RequiredNodes = 3;

    [Test]
    public async Task OccupiedOutputIsPreservedThenActualPreparedClusterCompositionSucceeds()
    {
        var root = Directory.CreateTempSubdirectory(RootPrefix).FullName;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var context = NativeCoverageClusterPreparationControl.Create(root);
            await RejectOccupiedAsync(context);
            var arguments = NativeCoverageClusterFixtureArguments.Create(context, root,
                NativeCoverageClusterPreparationControl.StartupPoll);
            var builder = DistributedApplication.CreateBuilder(new DistributedApplicationOptions
            {
                DisableDashboard = true,
                Args = arguments
            });
            KeyLoadResources.AddKeyLoad(builder);
            await using var application = builder.Build();
            var resources = application.Services.GetRequiredService<DistributedApplicationModel>()
                .Resources.OfType<ContainerResource>().ToArray();
            await Assert.That(resources.Length).IsEqualTo(RequiredNodes);
            foreach (var resource in resources)
            {
                var emitted = await ExecutionConfigurationBuilder.Create(resource)
                    .WithEnvironmentVariablesConfig().BuildAsync(
                        new DistributedApplicationExecutionContext(DistributedApplicationOperation.Run),
                        NullLogger.Instance, TestContext.Current!.Execution.CancellationToken);
                await Assert.That(emitted.Exception).IsNull();
                var environment = emitted.EnvironmentVariables.ToDictionary(pair => pair.Key,
                    pair => pair.Value, StringComparer.Ordinal);
                await Assert.That(environment[NativeCoverageClusterProtocol.CoverageSessionEnvironment])
                    .IsEqualTo(context.RunId);
                await Assert.That(environment[NativeCoverageClusterProtocol.CoverageDllHashEnvironment])
                    .IsEqualTo(context.Server.DllSha256);
            }
        }, failures);
        ServerFailureObserver.Observe(() => Directory.Delete(root, recursive: true), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task RejectOccupiedAsync(NativeCoverageClusterFixtureContext context)
    {
        var path = Path.Combine(context.FixtureRoot, NativeCoverageClusterFixtureProtocol.CoverageDirectoryName);
        await File.WriteAllBytesAsync(path, Sentinel, TestContext.Current!.Execution.CancellationToken);
        Assert.ThrowsExactly<IOException>(() => NativeCoverageClusterFixtureArguments.Create(context,
            context.FixtureRoot, NativeCoverageClusterPreparationControl.StartupPoll));
        await Assert.That(await File.ReadAllBytesAsync(path, TestContext.Current!.Execution.CancellationToken))
            .IsEquivalentTo(Sentinel);
        File.Delete(path);
    }
}
