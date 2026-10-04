using System.Diagnostics;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.IntegrationTests.Features.ClusterReplication;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.IntegrationTests.Features.StorageRecovery;

internal static class NodeEpochRf3ImageProof
{
    private const string PriorEnvironment = "KEYLOAD_PRIOR_SERVER_IMAGE";
    private const string DigestMarker = "@sha256:";
    private const string InvalidProof = "The actual genuine prior-server proof failed verification.";

    internal static async Task<string> ReadPriorReferenceAsync(CancellationToken cancellationToken)
    {
        var root = ClusterFixtureDiagnostics.FindRepositoryRoot().FullName;
        var start = new ProcessStartInfo("node")
        { WorkingDirectory = root, UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true };
        start.ArgumentList.Add(Path.Combine(root, "scripts", "Features", "StorageRecovery", "verify-native5-server-image.mjs"));
        var verified = await NodeEpochRf3OfflineProcess.RunAsync(start, cancellationToken).ConfigureAwait(false);
        var expected = Environment.GetEnvironmentVariable(PriorEnvironment);
        if (verified.ExitCode != 0 || !string.IsNullOrEmpty(verified.Stderr) || string.IsNullOrWhiteSpace(expected)
            || verified.Stdout != expected + "\n" || expected.LastIndexOf(DigestMarker, StringComparison.Ordinal) < 0)
        { throw new InvalidOperationException(InvalidProof); }
        return expected;
    }

    internal static async Task VerifyPriorModelAsync(DistributedApplication app, string reference,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(app);
        var verified = await ReadPriorReferenceAsync(cancellationToken).ConfigureAwait(false);
        if (reference != verified)
        { throw new InvalidOperationException(InvalidProof); }
        var model = app.Services.GetRequiredService<DistributedApplicationModel>();
        var nodes = model.Resources.OfType<ContainerResource>()
            .Where(node => ClusterFixtureProtocol.IsNodeName(node.Name)).ToArray();
        await Assert.That(nodes.Length).IsEqualTo(ClusterFixtureProtocol.NodeCount);
        var separator = reference.LastIndexOf(DigestMarker, StringComparison.Ordinal);
        var tagged = reference[..separator];
        var repository = tagged[..tagged.LastIndexOf(':')];
        var digest = reference[(separator + DigestMarker.Length)..];
        foreach (var node in nodes)
        {
            var annotation = node.Annotations.OfType<ContainerImageAnnotation>().Single();
            await Assert.That(annotation.Image).IsEqualTo(repository);
            await Assert.That(annotation.Tag).IsNull();
            await Assert.That(annotation.SHA256).IsEqualTo(digest);
            await Assert.That(node.TryGetContainerImageName(out var actual)).IsTrue();
            await Assert.That(actual!.EndsWith(DigestMarker + digest, StringComparison.Ordinal)).IsTrue();
        }
    }
}
