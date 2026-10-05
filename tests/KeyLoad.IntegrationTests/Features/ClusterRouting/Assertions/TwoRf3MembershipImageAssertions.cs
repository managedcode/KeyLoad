using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TwoRf3MembershipImageAssertions
{
    internal static async Task VerifyAsync(DistributedApplication app, string reference,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(app);
        cancellationToken.ThrowIfCancellationRequested();
        var resources = app.Services.GetRequiredService<DistributedApplicationModel>().Resources
            .OfType<ContainerResource>().Where(resource => TwoRf3MembershipProtocol.Nodes.Contains(resource.Name,
                StringComparer.Ordinal)).ToArray();
        await Assert.That(resources.Length).IsEqualTo(TwoRf3MembershipProtocol.NodeCount);
        foreach (var resource in resources)
        { await VerifyResourceAsync(resource, reference).ConfigureAwait(false); }
    }

    private static async Task VerifyResourceAsync(ContainerResource resource, string reference)
    {
        var digestMarker = reference.LastIndexOf("@sha256:", StringComparison.Ordinal);
        if (digestMarker <= 0)
        { throw new InvalidOperationException(TwoRf3MembershipProtocol.ImageMismatch); }
        var imageTag = reference[..digestMarker];
        var repository = imageTag[..imageTag.LastIndexOf(':')];
        var digest = reference[(digestMarker + 8)..];
        var image = resource.Annotations.OfType<ContainerImageAnnotation>().Single();
        if (image.Image != repository || image.Tag is not null || image.SHA256 != "sha256:" + digest
            || !resource.TryGetContainerImageName(out var actual) || actual is null
            || !actual.EndsWith("@sha256:" + digest, StringComparison.Ordinal))
        { throw new InvalidOperationException(TwoRf3MembershipProtocol.ImageMismatch); }
        await Assert.That(TwoRf3MembershipProtocol.Nodes.Contains(resource.Name, StringComparer.Ordinal)).IsTrue();
    }
}
