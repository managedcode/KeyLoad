using System.Diagnostics;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.IntegrationTests.Features.StorageRecovery;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Verifies the frozen interface3 image receipt and binds each Aspire voter to its expected digest.</summary>
internal static class RequestCqrsRf3Interface3ImageProof
{
    private const string VerifyScript = "verify-interface3-server-image.mjs";
    private const string ImageEnvironment = "KEYLOAD_INTERFACE3_SERVER_IMAGE";
    private const string DigestMarker = "@sha256:";
    private const string MissingReference = "A verified interface3 server image reference is required.";
    private const string ResourceMismatch = "An actual Aspire node image does not match its verified source receipt.";

    internal static async Task<string> ReadVerifiedReferenceAsync(CancellationToken cancellationToken)
    {
        var root = ClusterFixtureDiagnostics.FindRepositoryRoot().FullName;
        var start = new ProcessStartInfo("node")
        { WorkingDirectory = root, UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true };
        start.ArgumentList.Add(Path.Combine(root, "scripts", "Features", "ClusterRouting", VerifyScript));
        var result = await NodeEpochRf3OfflineProcess.RunAsync(start, cancellationToken).ConfigureAwait(false);
        var expected = Environment.GetEnvironmentVariable(ImageEnvironment);
        if (result.ExitCode != 0 || result.Stderr.Length != 0 || string.IsNullOrWhiteSpace(expected)
            || result.Stdout != expected + "\n" || expected.LastIndexOf(DigestMarker, StringComparison.Ordinal) < 0)
        { throw new InvalidOperationException(MissingReference); }
        return expected;
    }

    internal static async Task VerifyModelAsync(DistributedApplication app,
        IReadOnlyDictionary<string, string> expected, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(expected);
        await Assert.That(expected.Count).IsEqualTo(RequestCqrsRf3Protocol.NodeCount);
        var actual = app.Services.GetRequiredService<DistributedApplicationModel>().Resources
            .OfType<ContainerResource>().Where(resource => IsNode(resource.Name)).ToArray();
        await Assert.That(actual.Length).IsEqualTo(RequestCqrsRf3Protocol.NodeCount);
        foreach (var resource in actual)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!expected.TryGetValue(resource.Name, out var reference))
            { throw new InvalidOperationException(ResourceMismatch); }
            VerifyNode(resource, reference);
        }
    }

    private static void VerifyNode(ContainerResource resource, string expected)
    {
        var marker = expected.LastIndexOf(DigestMarker, StringComparison.Ordinal);
        if (marker <= 0)
        { throw new InvalidOperationException(MissingReference); }
        var tagged = expected[..marker];
        var separator = tagged.LastIndexOf(':');
        if (separator <= 0)
        { throw new InvalidOperationException(MissingReference); }
        var repository = tagged[..separator];
        var digest = expected[(marker + DigestMarker.Length)..];
        var annotation = resource.Annotations.OfType<ContainerImageAnnotation>().Single();
        if (annotation.Image != repository || annotation.Tag is not null || annotation.SHA256 != digest
            || !resource.TryGetContainerImageName(out var actual) || actual is null
            || !actual.EndsWith(DigestMarker + digest, StringComparison.Ordinal))
        { throw new InvalidOperationException(ResourceMismatch); }
    }

    private static bool IsNode(string name) => name is RequestCqrsRf3Protocol.Node1
        or RequestCqrsRf3Protocol.Node2 or RequestCqrsRf3Protocol.Node3;
}
