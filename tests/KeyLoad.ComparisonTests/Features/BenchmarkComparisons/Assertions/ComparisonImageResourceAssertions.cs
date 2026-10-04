using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal static class ComparisonImageResourceAssertions
{
    /// <summary>AC-IMAGE-003/004 checks real RF3 and runner container definitions before native start.</summary>
    internal static async Task<ComparisonImageReceipt> VerifyAsync(DistributedApplication app,
        CancellationToken cancellationToken)
    {
        var receipt = await ComparisonImageReceipt.ReadAsync(cancellationToken);
        var resources = app.Services.GetRequiredService<DistributedApplicationModel>().Resources;
        var nodes = resources.OfType<ContainerResource>().Where(resource => resource.Name is
            ComparisonImageProtocol.Node1 or ComparisonImageProtocol.Node2 or ComparisonImageProtocol.Node3).ToArray();
        await Assert.That(nodes.Length).IsEqualTo(ComparisonImageProtocol.NodeCount);
        foreach (var node in nodes)
        {
            await VerifyResourceAsync(node, receipt.ServerImage);
        }
        var runner = resources.Single(resource => resource.Name == ComparisonImageProtocol.Runner);
        await Assert.That(runner).IsTypeOf<ContainerResource>();
        await VerifyResourceAsync((ContainerResource)runner, receipt.RunnerImage);
        return receipt;
    }

    private static async Task VerifyResourceAsync(ContainerResource resource, string reference)
    {
        var digestStart = reference.LastIndexOf(ComparisonImageProtocol.ImageDigestPrefix, StringComparison.Ordinal);
        var namedImage = reference[..digestStart];
        var tagStart = namedImage.LastIndexOf(ComparisonImageProtocol.TagSeparator);
        var annotation = resource.Annotations.OfType<ContainerImageAnnotation>().Single();
        await Assert.That(annotation.Image).IsEqualTo(namedImage[..tagStart]);
        await Assert.That(annotation.Tag).IsNull();
        await Assert.That(annotation.SHA256).IsEqualTo(reference[(digestStart + ComparisonImageProtocol.ImageDigestPrefix.Length)..]);
        await Assert.That(resource.TryGetContainerImageName(out var image)).IsTrue();
        await Assert.That(image!.EndsWith(reference[digestStart..], StringComparison.Ordinal)).IsTrue();
    }
}
