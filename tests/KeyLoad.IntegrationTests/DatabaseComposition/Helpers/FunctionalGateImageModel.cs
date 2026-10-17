using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using KeyLoad.IntegrationTests.Features.ClusterReplication;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

/// <summary>Validates the exact selected Aspire voter model and the original immutable native server image.</summary>
internal static class FunctionalGateImageModel
{
    internal static Dictionary<string, string> ReadContainerNames(IDistributedApplicationTestingBuilder ownedBuilder,
        string[] nodeNames)
    {
        var resources = ownedBuilder.Resources.OfType<ContainerResource>()
            .Where(resource => resource.Name.StartsWith(ClusterFixtureProtocol.NodePrefix, StringComparison.Ordinal)).ToArray();
        if (!resources.Select(resource => resource.Name).Order(StringComparer.Ordinal)
            .SequenceEqual(nodeNames.Order(StringComparer.Ordinal), StringComparer.Ordinal))
        {
            throw new InvalidOperationException("The functional Aspire model must expose exactly its requested native voters.");
        }
        return resources.ToDictionary(resource => resource.Name,
            resource => resource.Annotations.OfType<ContainerNameAnnotation>().Single().Name, StringComparer.Ordinal);
    }

    internal static async Task VerifyPublishedImageAsync(DistributedApplication app, string[] nodeNames,
        CancellationToken token)
    {
        var image = await ClusterFixtureImageIdentity.ReadVerifiedImageAsync(token).ConfigureAwait(false);
        var model = app.Services.GetRequiredService<DistributedApplicationModel>();
        foreach (var resource in model.Resources.OfType<ContainerResource>()
            .Where(resource => nodeNames.Contains(resource.Name, StringComparer.Ordinal)))
        {
            var annotation = resource.Annotations.OfType<ContainerImageAnnotation>().Single();
            if (annotation.SHA256 != image.ManifestDigest || annotation.Tag is not null
                || !resource.TryGetContainerImageName(out var reference)
                || !string.Equals(reference, image.Reference, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("A functional voter does not use the verified immutable native server image.");
            }
        }
    }
}
