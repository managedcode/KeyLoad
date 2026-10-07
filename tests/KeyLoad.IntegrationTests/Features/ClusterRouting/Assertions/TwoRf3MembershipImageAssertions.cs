using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TwoRf3MembershipImageAssertions
{
    private const string DigestMarker = "@sha256:";
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
        var digestMarker = reference.LastIndexOf(DigestMarker, StringComparison.Ordinal);
        if (digestMarker <= 0)
        { throw new InvalidOperationException(TwoRf3MembershipProtocol.ImageMismatch); }
        var imageTag = reference[..digestMarker];
        var tagSeparator = imageTag.LastIndexOf(':');
        if (tagSeparator <= 0 || tagSeparator == imageTag.Length - 1)
        { throw new InvalidOperationException(TwoRf3MembershipProtocol.ImageMismatch); }
        var repository = imageTag[..tagSeparator];
        var acceptedTag = imageTag[(tagSeparator + 1)..];
        var digest = reference[(digestMarker + DigestMarker.Length)..];
        var expectedResolvedImage = repository + DigestMarker + digest;
        var image = resource.Annotations.OfType<ContainerImageAnnotation>().Single();
        if (image.Image != repository || image.Tag != acceptedTag || image.SHA256 != digest
            || !resource.TryGetContainerImageName(out var actual) || actual is null
            || !string.Equals(actual, expectedResolvedImage, StringComparison.Ordinal))
        { throw new InvalidOperationException(TwoRf3MembershipProtocol.ImageMismatch); }
        await Assert.That(TwoRf3MembershipProtocol.Nodes.Contains(resource.Name, StringComparer.Ordinal)).IsTrue();
    }
}
