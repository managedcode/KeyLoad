using System.Diagnostics;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.IntegrationTests.Features.StorageRecovery;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal sealed record RequestCqrsRf3Images(string Current, string Rpc1);

internal static class RequestCqrsRf3ImageProof
{
    private const string VerifyScript = "verify-rpc1-server-image.mjs";
    private const string DigestMarker = "@sha256:";
    private const string MissingReference = "A verified RPC1 image reference is required.";
    private const string ResourceMismatch = "An actual Aspire node image does not match its verified source receipt.";

    internal static async Task<RequestCqrsRf3Images> ReadAsync(CancellationToken cancellationToken)
    {
        var current = await ClusterFixtureImageIdentity.ReadVerifiedReferenceAsync(cancellationToken).ConfigureAwait(false);
        var rpc1 = await ReadRpc1ReferenceAsync(cancellationToken).ConfigureAwait(false);
        return new(current, rpc1);
    }

    internal static async Task VerifyModelAsync(DistributedApplication app, IReadOnlyDictionary<string, string> expected,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(app);
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
    }

    private static async Task<string> ReadRpc1ReferenceAsync(CancellationToken cancellationToken)
    {
        var root = ClusterFixtureDiagnostics.FindRepositoryRoot().FullName;
        var start = new ProcessStartInfo("node")
        { WorkingDirectory = root, UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true };
        start.ArgumentList.Add(Path.Combine(root, "scripts", "Features", "ClusterRouting", VerifyScript));
        var result = await NodeEpochRf3OfflineProcess.RunAsync(start, cancellationToken).ConfigureAwait(false);
        var expected = Environment.GetEnvironmentVariable(RequestCqrsRf3Protocol.Rpc1ImageEnvironment);
        if (result.ExitCode != 0 || result.Stderr.Length != 0 || string.IsNullOrWhiteSpace(expected)
            || result.Stdout != expected + "\n" || expected.LastIndexOf(DigestMarker, StringComparison.Ordinal) < 0)
        { throw new InvalidOperationException(MissingReference); }
        return expected;
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
