using System.Text.Json;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.AppHost.Features.StorageRecovery;
using KeyLoad.IntegrationTests.Features.ClusterReplication;
using Microsoft.Extensions.DependencyInjection;
using P = KeyLoad.IntegrationTests.Features.DatabaseComposition.FunctionalGateFileOwnershipProtocol;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

/// <summary>Compares actual model and container mounts; no distinct directory is relabelled as shared files.</summary>
internal static class FunctionalGateFileOwnershipAssertions
{
    internal static async Task ModelAsync(FunctionalGateFixture fixture)
    {
        await Assert.That(fixture.NodeNames.Length).IsEqualTo(P.SiloCount);
        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in fixture.NodeNames)
        {
            var path = Mount(fixture, name);
            await Assert.That(path).IsEqualTo(Path.GetFullPath(Path.Combine(fixture.Root, name)));
            await Assert.That(paths.Add(path)).IsTrue();
        }
        var owner = Mount(fixture, ExclusiveOwnerContentionResources.OwnerName);
        foreach (var name in ExclusiveOwnerContentionResources.ResourceNames)
        {
            await Assert.That(Mount(fixture, name)).IsEqualTo(owner);
        }
    }

    internal static string ContainerName(FunctionalGateFixture fixture, string name)
        => Resource(fixture, name).Annotations.OfType<ContainerNameAnnotation>().Single().Name;

    private static ContainerResource Resource(FunctionalGateFixture fixture, string name)
        => fixture.App.Services.GetRequiredService<DistributedApplicationModel>().Resources.OfType<ContainerResource>().Single(resource => resource.Name == name);

    private static string Mount(FunctionalGateFixture fixture, string name)
    {
        var mount = Resource(fixture, name).Annotations.OfType<ContainerMountAnnotation>()
            .Single(value => value.Target == P.DataDirectory);
        if (mount.Type != ContainerMountType.BindMount || mount.IsReadOnly || mount.Source is null)
        {
            throw new InvalidOperationException(P.Incomplete);
        }
        return Path.GetFullPath(mount.Source);
    }

    internal static async Task<(string Source, string Image, string Configured, string Id, string Inspection)> RuntimeAsync(
        FunctionalGateFixture fixture, string name, bool refused, CancellationToken token)
    {
        var result = await ContainerRuntimeDocker.RunAsync([P.Inspect, P.Format, P.Inspection,
            ContainerName(fixture, name)], token).ConfigureAwait(false);
        ContainerRuntimeDocker.EnsureSuccessful(result, P.Inspect, name);
        using var document = JsonDocument.Parse(result.StandardOutput);
        var root = document.RootElement;
        await Assert.That(root.GetProperty(P.StateProperty).GetString()).IsEqualTo(refused ? P.Exited : P.Running);
        if (refused)
        {
            await Assert.That(root.GetProperty(P.ExitProperty).GetInt32()).IsNotEqualTo(P.SuccessExit);
        }
        var mount = root.GetProperty(P.MountsProperty).EnumerateArray()
            .Single(value => value.GetProperty(P.DestinationProperty).GetString() == P.DataDirectory);
        await Assert.That(mount.GetProperty(P.TypeProperty).GetString()).IsEqualTo(P.Bind);
        await Assert.That(mount.GetProperty(P.WritableProperty).GetBoolean()).IsTrue();
        var source = mount.GetProperty(P.SourceProperty).GetString();
        var image = root.GetProperty(P.ImageProperty).GetString();
        var configured = root.GetProperty(P.ConfiguredProperty).GetString();
        var id = root.GetProperty(P.IdProperty).GetString();
        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(image)
            || string.IsNullOrWhiteSpace(configured) || id is null
            || id.Length != P.ContainerIdCharacters || !id.All(Uri.IsHexDigit))
        {
            throw new InvalidOperationException(P.Incomplete);
        }
        return (source, image, configured, id, result.StandardOutput);
    }

    internal static bool HasOriginalRefusal(string text)
        => text.Contains(P.IoFailure, StringComparison.Ordinal) && text.Contains(P.LockPath, StringComparison.Ordinal)
            && text.Contains(P.OriginalOwner, StringComparison.Ordinal);
}
