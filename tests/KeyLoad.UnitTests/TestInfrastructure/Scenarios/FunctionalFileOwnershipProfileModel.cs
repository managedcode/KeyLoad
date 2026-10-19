using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.AppHost.Features.TestInfrastructure;
using KeyLoad.AppHost.Features.StorageRecovery;
using KeyLoad.AppHost.Hosting;
using KeyLoad.Server;
using Microsoft.Extensions.DependencyInjection;
using P = KeyLoad.UnitTests.Features.TestInfrastructure.FunctionalColdReopenProfileProtocol;

namespace KeyLoad.UnitTests.Features.TestInfrastructure;

internal static class FunctionalFileOwnershipProfileModel
{
    private const int Silos = 3;
    private const int OwnersAndContenders = 6;
    private const string NodePrefix = "node";
    private const string SiloAddress = "KeyLoad__SiloAddress";
    private const string DataEnvironment = "KeyLoad__DataDirectory";

    internal static async Task VerifyAsync()
    {
        var root = P.NewRoot();
        var builder = P.Create(root, Silos, true, true,
            [new(ExclusiveOwnerContentionProfile.Setting, P.True)]);
        DistributedApplication? app = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            AppHostProgram.AddKeyLoad(builder);
            await Assert.That(AppHostOptionsRegistration.Get(builder).Control.Value.ExclusiveOwnerContention).IsTrue();
            app = builder.Build();
            var model = app.Services.GetRequiredService<DistributedApplicationModel>();
            var names = ExclusiveOwnerContentionResources.ResourceNames;
            var resources = model.Resources.OfType<ProjectResource>()
                .Where(value => value.Name.StartsWith(NodePrefix, StringComparison.Ordinal)
                    || names.Contains(value.Name, StringComparer.Ordinal)).ToArray();
            await Assert.That(resources.Length).IsEqualTo(OwnersAndContenders);
            await Assert.That(model.Resources.OfType<ContainerResource>().Select(resource => resource.Name))
                .IsEquivalentTo(new[] { "orleans-storage" });
            await VerifyResourcesAsync(builder, resources, root, names);
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

    private static async Task VerifyResourcesAsync(IDistributedApplicationBuilder builder,
        ProjectResource[] resources, string root, string[] contenders)
    {
        var owner = resources.Single(value => value.Name == ExclusiveOwnerContentionResources.OwnerName);
        var names = new HashSet<string>(StringComparer.Ordinal);
        var endpoints = new HashSet<EndpointAnnotation>(ReferenceEqualityComparer.Instance);
        var silos = resources.Where(value => !contenders.Contains(value.Name, StringComparer.Ordinal)).ToArray();
        await Assert.That(silos.Select(value => value.Name)).IsEquivalentTo(new[] { "node1", "node2", "node3" });
        foreach (var resource in resources)
        {
            var contender = contenders.Contains(resource.Name, StringComparer.Ordinal);
            var directory = Path.GetFullPath(Path.Combine(root, contender ? owner.Name : resource.Name));
            await Assert.That(Directory.Exists(directory)).IsTrue();
            await Assert.That(names.Add(resource.Name)).IsTrue();
            await Assert.That(resource.GetProjectMetadata().ProjectPath).IsEqualTo(owner.GetProjectMetadata().ProjectPath);
            await Assert.That(resource.Annotations.OfType<ExplicitStartupAnnotation>().Count()).IsEqualTo(contender ? 1 : 0);
            if (contender)
            {
                var wait = resource.Annotations.OfType<WaitAnnotation>().Single(value => ReferenceEquals(value.Resource, owner));
                await Assert.That(wait.WaitType).IsEqualTo(WaitType.WaitUntilHealthy);
            }
            foreach (var endpoint in resource.Annotations.OfType<EndpointAnnotation>())
            {
                await Assert.That(endpoints.Add(endpoint)).IsTrue();
                await Assert.That(endpoint.IsProxied).IsFalse();
            }
            await VerifyEnvironmentAsync(builder, resource, directory);
            await VerifyFaultAsync(resource);
        }
    }

    private static async Task VerifyEnvironmentAsync(IDistributedApplicationBuilder builder, ProjectResource resource, string directory)
    {
        var environment = new Dictionary<string, object>(StringComparer.Ordinal);
        var context = new EnvironmentCallbackContext(builder.ExecutionContext, resource, environment);
        foreach (var callback in resource.Annotations.OfType<EnvironmentCallbackAnnotation>())
        {
            await callback.Callback(context);
        }
        await Assert.That(environment[SiloAddress]).IsEqualTo("127.0.0.1");
        await Assert.That(environment[DataEnvironment]).IsEqualTo(directory);
    }

    private static async Task VerifyFaultAsync(ProjectResource resource)
    {
        var command = resource.Annotations.OfType<ResourceCommandAnnotation>()
            .Single(value => value.Name == ProcessFaultCommandProtocol.CommandName);
        await Assert.That(command.Visibility).IsEqualTo(ResourceCommandVisibility.Api);
        await Assert.That(command.Arguments.Select(argument => argument.Name)).IsEquivalentTo(new[]
        {
            ProcessFaultCommandProtocol.ExpectedProcessIdArgument, ProcessFaultCommandProtocol.ExpectedAspireStartArgument,
            ProcessFaultCommandProtocol.ExpectedProcessStartArgument
        });
        await Assert.That(command.Arguments.All(argument => argument.Required
            && argument.MaxLength == ProcessFaultCommandProtocol.MaximumIdentityCharacters)).IsTrue();
    }
}
