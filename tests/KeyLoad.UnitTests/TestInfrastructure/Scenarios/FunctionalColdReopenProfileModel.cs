using System.Globalization;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.AppHost.Features.TestInfrastructure;
using KeyLoad.AppHost.Hosting;
using KeyLoad.Server;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.UnitTests.Features.TestInfrastructure;

internal static class FunctionalColdReopenProfileModel
{
    private const string NodePrefix = "node";
    private const string DataEnvironment = "KeyLoad__DataDirectory";
    private const string ClusterEnvironment = "KeyLoad__ClusterId";

    internal static async Task VerifyAsync(int nodes, bool cold, bool faults)
    {
        var root = FunctionalColdReopenProfileProtocol.NewRoot();
        var builder = FunctionalColdReopenProfileProtocol.Create(root, nodes, cold, faults);
        DistributedApplication? app = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            AppHostProgram.AddKeyLoad(builder);
            var settings = AppHostOptionsRegistration.Get(builder).Control.Value;
            await Assert.That(settings.FunctionalColdReopen).IsEqualTo(cold);
            await Assert.That(settings.ProcessFaults).IsEqualTo(faults);
            await Assert.That(settings.FunctionalNodeCount).IsEqualTo(nodes == 0 ? (int?)null : nodes);
            app = builder.Build();
            var profile = app.Services.GetRequiredService<ClusterIdentity>();
            await VerifyResourcesAsync(app, builder.ExecutionContext, root, nodes == 0 ? 3 : nodes, faults, profile.Incarnation);
        }, failures);
        if (app is not null)
        {
            await ServerFailureObserver.ObserveAsync(() => app.DisposeAsync().AsTask(), failures);
        }
        if (failures.Count == 0 && Directory.Exists(root))
        {
            ServerFailureObserver.Observe(() => Directory.Delete(root, recursive: true), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
        await Assert.That(Directory.Exists(root)).IsFalse();
    }

    private static async Task VerifyResourcesAsync(DistributedApplication app, DistributedApplicationExecutionContext execution,
        string root, int nodes, bool faults, Guid incarnation)
    {
        var model = app.Services.GetRequiredService<DistributedApplicationModel>();
        var resources = model.Resources.OfType<ProjectResource>()
            .Where(resource => resource.Name.StartsWith(NodePrefix, StringComparison.Ordinal)).ToArray();
        await Assert.That(resources.Length).IsEqualTo(nodes);
        await Assert.That(resources.Select(resource => resource.Name).ToArray()).IsEquivalentTo(
            Enumerable.Range(1, nodes).Select(index => NodePrefix + index.ToString(CultureInfo.InvariantCulture)).ToArray());
        await Assert.That(model.Resources.OfType<ContainerResource>().Select(resource => resource.Name))
            .IsEmpty();
        await Assert.That(Directory.GetFiles(root)).IsEmpty();
        foreach (var resource in resources)
        {
            var environment = new Dictionary<string, object>(StringComparer.Ordinal);
            var context = new EnvironmentCallbackContext(execution, resource, environment);
            foreach (var callback in resource.Annotations.OfType<EnvironmentCallbackAnnotation>())
            {
                await callback.Callback(context);
            }
            var directory = ClusterDataDirectory.Resolve(Path.Combine(root, resource.Name));
            await Assert.That(environment[DataEnvironment]).IsEqualTo(directory);
            await Assert.That(environment[ClusterEnvironment]).IsEqualTo("keyload-" + incarnation.ToString("N"));
            await Assert.That(Directory.Exists(directory)).IsTrue();
            await Assert.That(resource.GetProjectMetadata().ProjectPath).EndsWith("KeyLoad.csproj");
            var commands = resource.Annotations.OfType<ResourceCommandAnnotation>()
                .Where(command => command.Name == ProcessFaultCommandProtocol.CommandName).ToArray();
            await Assert.That(commands.Length).IsEqualTo(faults ? 1 : 0);
            if (faults)
            {
                var arguments = commands.Single().Arguments;
                await Assert.That(arguments.Select(argument => argument.Name)).IsEquivalentTo(new[]
                {
                    ProcessFaultCommandProtocol.ExpectedProcessIdArgument, ProcessFaultCommandProtocol.ExpectedAspireStartArgument,
                    ProcessFaultCommandProtocol.ExpectedProcessStartArgument,
                    ProcessFaultCommandProtocol.ExpectedLaunchProcessIdArgument,
                    ProcessFaultCommandProtocol.ExpectedLaunchProcessStartArgument
                });
                await Assert.That(arguments.All(argument => argument.Required
                    && argument.MaxLength == ProcessFaultCommandProtocol.MaximumIdentityCharacters)).IsTrue();
                await Assert.That(commands.Single().Visibility).IsEqualTo(ResourceCommandVisibility.Api);
            }
        }
    }
}
