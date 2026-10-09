using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.IntegrationTests.Features.ClusterReplication;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal sealed record RequestCqrsRf3Images(string Current);

internal static class RequestCqrsRf3ImageProof
{
    private const string DigestMarker = "@sha256:";
    private const string MissingReference = "A verified current server image reference is required.";
    private const string ResourceMismatch = "An actual Aspire node image does not match its verified source receipt.";

    internal static async Task<RequestCqrsRf3Images> ReadAsync(CancellationToken cancellationToken,
        LocalRf3ImageSelection.Selection? selection = null)
    {
        var root = ClusterFixtureDiagnostics.FindRepositoryRoot().FullName;
        var localIdentity = await LocalRf3ImageIdentity.ReadVerifiedAsync(root, selection ?? LocalRf3ImageSelection.Read(),
            cancellationToken).ConfigureAwait(false);
        var current = localIdentity?.Reference
            ?? await ClusterFixtureImageIdentity.ReadVerifiedReferenceAsync(cancellationToken).ConfigureAwait(false);
        return new(current);
    }

    internal static async Task<LocalRf3ImageIdentity.Identity?> VerifyModelAsync(DistributedApplication app,
        IReadOnlyDictionary<string, string> expected, CancellationToken cancellationToken,
        LocalRf3ImageSelection.Selection? selection = null)
    {
        ArgumentNullException.ThrowIfNull(app);
        var root = ClusterFixtureDiagnostics.FindRepositoryRoot().FullName;
        var localIdentity = selection is null
            ? await LocalRf3ImageIdentity.VerifyBeforeStartAsync(app, root, cancellationToken).ConfigureAwait(false)
            : await LocalRf3ImageIdentity.VerifyBeforeStartAsync(app, root, selection,
                [RequestCqrsRf3Protocol.Node1, RequestCqrsRf3Protocol.Node2, RequestCqrsRf3Protocol.Node3],
                cancellationToken).ConfigureAwait(false);
        if (localIdentity is not null)
        {
            var expectedReference = ValidateExpectedReferences(expected);
            if (!string.Equals(localIdentity.Reference, expectedReference, StringComparison.Ordinal))
            { throw new InvalidOperationException(ResourceMismatch); }
            return localIdentity;
        }
        await Assert.That(expected.Count).IsEqualTo(RequestCqrsRf3Protocol.NodeCount);
        var actual = app.Services.GetRequiredService<DistributedApplicationModel>().Resources
            .OfType<ContainerResource>().Where(resource => IsNode(resource.Name)).ToArray();
        await Assert.That(actual.Length).IsEqualTo(RequestCqrsRf3Protocol.NodeCount);
        foreach (var node in actual)
        {
            if (!expected.TryGetValue(node.Name, out var reference))
            { throw new InvalidOperationException(ResourceMismatch); }
            await VerifyNodeAsync(node, reference, cancellationToken).ConfigureAwait(false);
        }
        return null;
    }

    internal static Task VerifyStartedContainersAsync(LocalRf3ImageIdentity.Identity? localIdentity,
        IReadOnlyDictionary<string, string> containerNames, CancellationToken cancellationToken)
        => localIdentity is null ? Task.CompletedTask
            : LocalRf3ImageIdentity.VerifyStartedContainersAsync(localIdentity, containerNames, cancellationToken);

    private static string ValidateExpectedReferences(IReadOnlyDictionary<string, string> expected)
    {
        ArgumentNullException.ThrowIfNull(expected);
        if (expected.Count != RequestCqrsRf3Protocol.NodeCount
            || !expected.ContainsKey(RequestCqrsRf3Protocol.Node1)
            || !expected.ContainsKey(RequestCqrsRf3Protocol.Node2)
            || !expected.ContainsKey(RequestCqrsRf3Protocol.Node3))
        { throw new InvalidOperationException(ResourceMismatch); }
        var references = expected.Values.Distinct(StringComparer.Ordinal).ToArray();
        return references.Length == 1 ? references[0] : throw new InvalidOperationException(ResourceMismatch);
    }

    private static async Task VerifyNodeAsync(ContainerResource resource, string expected,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var marker = expected.LastIndexOf(DigestMarker, StringComparison.Ordinal);
        if (marker <= 0)
        { throw new InvalidOperationException(MissingReference); }
        var tagged = expected[..marker];
        var repository = tagged[..tagged.LastIndexOf(':')];
        var digest = expected[(marker + DigestMarker.Length)..];
        var annotation = resource.Annotations.OfType<ContainerImageAnnotation>().Single();
        if (annotation.Image != repository || annotation.Tag is not null || annotation.SHA256 != digest
            || !resource.TryGetContainerImageName(out var actual) || actual is null
            || !actual.EndsWith(DigestMarker + digest, StringComparison.Ordinal))
        { throw new InvalidOperationException(ResourceMismatch); }
        await Assert.That(IsNode(resource.Name)).IsTrue();
    }

    private static bool IsNode(string name) => name is RequestCqrsRf3Protocol.Node1
        or RequestCqrsRf3Protocol.Node2 or RequestCqrsRf3Protocol.Node3;

}
