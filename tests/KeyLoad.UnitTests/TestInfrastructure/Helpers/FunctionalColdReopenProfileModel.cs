using System.Globalization;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.AppHost.Features.ClusterReplication;
using KeyLoad.AppHost.Hosting;
using KeyLoad.Server;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.UnitTests.Features.TestInfrastructure;

internal static class FunctionalColdReopenProfileModel
{
    private const string NodePrefix = "node";
    private const string MountTarget = "/data";
    private const string ContainerPrefix = "keyload-";
    private const string Separator = "-";
    private const string GuidFormat = "N";

    internal static async Task VerifyAsync(int nodes, bool cold, bool faults)
    {
        var root = FunctionalColdReopenProfileProtocol.NewRoot();
        var builder = FunctionalColdReopenProfileProtocol.Create(root, nodes, cold, faults);
        DistributedApplication? app = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            KeyLoadResources.AddKeyLoad(builder);
            var settings = AppHostOptionsRegistration.Get(builder).Control.Value;
            await Assert.That(settings.FunctionalColdReopen).IsEqualTo(cold);
            await Assert.That(settings.ContainerFaults).IsEqualTo(faults);
            await Assert.That(settings.FunctionalNodeCount).IsEqualTo(nodes == 0 ? (int?)null : nodes);
            var profileOptions = AppHostOptionsRegistration.Get(builder).Profile;
            var profile = ClusterProfileStore.DeserializeCurrent(ClusterProfileStore.ReadBoundedBytes(
                Path.Combine(root, ClusterProfileStore.ProfileName), profileOptions), profileOptions);
            app = builder.Build();
            await VerifyResourcesAsync(app, root, nodes == 0 ? 3 : nodes, faults, profile.Incarnation);
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

    private static async Task VerifyResourcesAsync(DistributedApplication app, string root, int nodes, bool faults, Guid incarnation)
    {
        var model = app.Services.GetRequiredService<DistributedApplicationModel>();
        var resources = model.Resources.OfType<ContainerResource>()
            .Where(resource => resource.Name.StartsWith(NodePrefix, StringComparison.Ordinal)).ToArray();
        await Assert.That(resources.Length).IsEqualTo(nodes);
        await Assert.That(resources.Select(resource => resource.Name).ToArray()).IsEquivalentTo(
            Enumerable.Range(1, nodes).Select(index => NodePrefix + index.ToString(CultureInfo.InvariantCulture)).ToArray());
        await Assert.That(model.Resources.OfType<ExecutableResource>()).IsEmpty();
        await Assert.That(File.Exists(Path.Combine(root, ClusterProfileStore.ProfileName))).IsTrue();
        foreach (var resource in resources)
        {
            var mount = resource.Annotations.OfType<ContainerMountAnnotation>().Single();
            await Assert.That(mount.Source).IsEqualTo(Path.Combine(root, resource.Name));
            await Assert.That(mount.Target).IsEqualTo(MountTarget);
            await Assert.That(mount.Type).IsEqualTo(ContainerMountType.BindMount);
            await Assert.That(mount.IsReadOnly).IsFalse();
            await Assert.That(Directory.Exists(mount.Source)).IsTrue();
            await Assert.That(resource.Annotations.OfType<ContainerNameAnnotation>().Single().Name)
                .IsEqualTo(ContainerPrefix + incarnation.ToString(GuidFormat) + Separator + resource.Name);
            var commands = resource.Annotations.OfType<ResourceCommandAnnotation>()
                .Where(command => command.Name == ContainerFaultCommandProtocol.CommandName).ToArray();
            await Assert.That(commands.Length).IsEqualTo(faults ? 1 : 0);
            if (faults)
            {
                var argument = commands.Single().Arguments.Single();
                await Assert.That(argument.Name).IsEqualTo(ContainerFaultCommandProtocol.ExpectedIdArgument);
                await Assert.That(argument.Required).IsTrue();
                await Assert.That(argument.MaxLength).IsEqualTo(ContainerFaultCommandProtocol.FullIdCharacters);
                await Assert.That(commands.Single().Visibility).IsEqualTo(ResourceCommandVisibility.Api);
            }
        }
    }
}
