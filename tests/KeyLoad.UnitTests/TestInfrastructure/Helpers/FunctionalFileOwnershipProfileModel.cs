using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.AppHost.Features.ClusterReplication;
using KeyLoad.AppHost.Features.StorageRecovery;
using KeyLoad.AppHost.Hosting;
using KeyLoad.Server;
using Microsoft.Extensions.DependencyInjection;
using P = KeyLoad.UnitTests.Features.TestInfrastructure.FunctionalColdReopenProfileProtocol;

namespace KeyLoad.UnitTests.Features.TestInfrastructure;

internal static class FunctionalFileOwnershipProfileModel
{
    private const int Voters = 3;
    private const int OwnersAndContenders = 6;
    private const int OneAnnotation = 1;
    private const string NodePrefix = "node";
    private const string DataDirectory = "/data";
    private const string SiloAddress = "KeyLoad__SiloAddress";
    private const string DataEnvironment = "KeyLoad__DataDirectory";
    private const string FirstVoter = "node1";
    private const string SecondVoter = "node2";
    private const string ThirdVoter = "node3";
    private const string ExpectedImageReference = "ghcr.io/managedcode/keyload@sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    internal static async Task VerifyAsync()
    {
        var root = P.NewRoot();
        var builder = P.Create(root, Voters, true, true,
            [new(ExclusiveOwnerContentionProfile.Setting, P.True)]);
        DistributedApplication? app = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            KeyLoadAppHostApplication.AddKeyLoad(builder);
            await Assert.That(AppHostOptionsRegistration.Get(builder).Control.Value.ExclusiveOwnerContention).IsTrue();
            app = builder.Build();
            var model = app.Services.GetRequiredService<DistributedApplicationModel>();
            var names = ExclusiveOwnerContentionResources.ResourceNames;
            var resources = model.Resources.OfType<ContainerResource>()
                .Where(value => value.Name.StartsWith(NodePrefix, StringComparison.Ordinal)
                    || names.Contains(value.Name, StringComparer.Ordinal)).ToArray();
            await Assert.That(resources.Length).IsEqualTo(OwnersAndContenders);
            await Assert.That(model.Resources.OfType<ExecutableResource>()).IsEmpty();
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
        ContainerResource[] resources, string root, string[] contenders)
    {
        var owner = resources.Single(value => value.Name == ExclusiveOwnerContentionResources.OwnerName);
        var aliases = new HashSet<string>(StringComparer.Ordinal);
        var containers = new HashSet<string>(StringComparer.Ordinal);
        var voters = resources.Where(value => !contenders.Contains(value.Name, StringComparer.Ordinal)).ToArray();
        await Assert.That(voters.Select(value => value.Name).ToArray()).IsEquivalentTo(
            new[] { FirstVoter, SecondVoter, ThirdVoter });
        var image = owner.Annotations.OfType<ContainerImageAnnotation>().Single();
        foreach (var resource in resources)
        {
            var contender = contenders.Contains(resource.Name, StringComparer.Ordinal);
            var mount = resource.Annotations.OfType<ContainerMountAnnotation>().Single();
            await Assert.That(mount.Source).IsEqualTo(Path.Combine(root, contender ? owner.Name : resource.Name));
            await Assert.That(mount.Target).IsEqualTo(DataDirectory);
            await Assert.That(mount.Type).IsEqualTo(ContainerMountType.BindMount);
            await Assert.That(mount.IsReadOnly).IsFalse();
            await Assert.That(Directory.Exists(mount.Source)).IsTrue();
            await Assert.That(resource.Annotations.OfType<ExplicitStartupAnnotation>().Count()).IsEqualTo(contender ? OneAnnotation : 0);
            if (contender)
            {
                var wait = resource.Annotations.OfType<WaitAnnotation>().Single(value => ReferenceEquals(value.Resource, owner));
                await Assert.That(wait.WaitType).IsEqualTo(WaitType.WaitUntilHealthy);
            }
            var alias = resource.Annotations.OfType<ContainerNetworkAliasAnnotation>().Single().Alias;
            await Assert.That(alias).IsEqualTo(resource.Name);
            await Assert.That(aliases.Add(alias)).IsTrue();
            await Assert.That(containers.Add(resource.Annotations.OfType<ContainerNameAnnotation>().Single().Name)).IsTrue();
            await Assert.That(resource.TryGetContainerImageName(out var reference)).IsTrue();
            // Native digest-pinned image names omit the configured tag.
            await Assert.That(reference).IsEqualTo(ExpectedImageReference);
            var actual = resource.Annotations.OfType<ContainerImageAnnotation>().Single();
            await Assert.That(actual.Registry).IsEqualTo(image.Registry);
            await Assert.That(actual.Image).IsEqualTo(image.Image);
            await Assert.That(actual.Tag).IsEqualTo(image.Tag);
            await Assert.That(actual.SHA256).IsEqualTo(image.SHA256);
            await VerifyEnvironmentAsync(builder, resource);
            await VerifyFaultAsync(resource);
        }
    }

    private static async Task VerifyEnvironmentAsync(IDistributedApplicationBuilder builder, ContainerResource resource)
    {
        var environment = new Dictionary<string, object>(StringComparer.Ordinal);
        var context = new EnvironmentCallbackContext(builder.ExecutionContext, resource, environment);
        foreach (var callback in resource.Annotations.OfType<EnvironmentCallbackAnnotation>())
        {
            await callback.Callback(context);
        }
        await Assert.That(environment[SiloAddress]).IsEqualTo(resource.Name);
        await Assert.That(environment[DataEnvironment]).IsEqualTo(DataDirectory);
    }

    private static async Task VerifyFaultAsync(ContainerResource resource)
    {
        var command = resource.Annotations.OfType<ResourceCommandAnnotation>()
            .Single(value => value.Name == ContainerFaultCommandProtocol.CommandName);
        var argument = command.Arguments.Single();
        await Assert.That(command.Visibility).IsEqualTo(ResourceCommandVisibility.Api);
        await Assert.That(argument.Name).IsEqualTo(ContainerFaultCommandProtocol.ExpectedIdArgument);
        await Assert.That(argument.Required).IsTrue();
        await Assert.That(argument.MaxLength).IsEqualTo(ContainerFaultCommandProtocol.FullIdCharacters);
    }
}
