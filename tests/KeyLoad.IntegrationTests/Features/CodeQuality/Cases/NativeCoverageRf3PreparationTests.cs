using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.AppHost.Features.CodeQuality;
using KeyLoad.AppHost.Hosting;
using KeyLoad.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KeyLoad.IntegrationTests.Features.CodeQuality;

/// <summary>Exercises owned directory preparation and the actual native RF3 resource composition.</summary>
internal sealed class NativeCoverageRf3PreparationTests
{
    private const string RootPrefix = "keyload-coverage-preparation-";
    private static readonly byte[] Sentinel = "owned-output-sentinel"u8.ToArray();
    private const int RequiredNodes = 3;

    [Test]
    public async Task OccupiedOutputIsPreservedThenActualPreparedRf3CompositionSucceeds()
    {
        var root = Directory.CreateTempSubdirectory(RootPrefix).FullName;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var context = NativeCoverageRf3PreparationControl.Create(root);
            await RejectOccupiedAsync(context);
            var arguments = NativeCoverageRf3FixtureArguments.Create(context, root,
                NativeCoverageRf3PreparationControl.StartupPoll);
            var builder = DistributedApplication.CreateBuilder(new DistributedApplicationOptions
            { DisableDashboard = true, Args = arguments });
            KeyLoadAppHostApplication.AddKeyLoad(builder);
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
                await Assert.That(environment[NativeCoverageRf3Protocol.CoverageSessionEnvironment])
                    .IsEqualTo(context.RunId);
                await Assert.That(environment[NativeCoverageRf3Protocol.CoverageDllHashEnvironment])
                    .IsEqualTo(context.Server.DllSha256);
            }
        }, failures);
        ServerFailureObserver.Observe(() => Directory.Delete(root, recursive: true), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task RejectOccupiedAsync(NativeCoverageRf3FixtureContext context)
    {
        var path = Path.Combine(context.FixtureRoot, NativeCoverageRf3FixtureProtocol.CoverageDirectoryName);
        await File.WriteAllBytesAsync(path, Sentinel, TestContext.Current!.Execution.CancellationToken);
        Assert.ThrowsExactly<IOException>(() => NativeCoverageRf3FixtureArguments.Create(context,
            context.FixtureRoot, NativeCoverageRf3PreparationControl.StartupPoll));
        await Assert.That(await File.ReadAllBytesAsync(path, TestContext.Current!.Execution.CancellationToken))
            .IsEquivalentTo(Sentinel);
        File.Delete(path);
    }
}
