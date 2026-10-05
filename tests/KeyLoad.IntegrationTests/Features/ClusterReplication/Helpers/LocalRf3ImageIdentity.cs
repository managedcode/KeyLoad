using System.Diagnostics;
using System.Text.Json;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.IntegrationTests.Features.ClusterReplication.Processes;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

/// <summary>Proves the local-development image receipt and the actual three started Docker containers.</summary>
internal static class LocalRf3ImageIdentity
{
    private const string ProvenanceEnvironment = "KEYLOAD_IMAGE_PROVENANCE";
    private const string ReceiptEnvironment = "KEYLOAD_LOCAL_IMAGE_RECEIPT";
    private const string ReferenceEnvironment = "KeyLoad__ContainerImages__Server";
    private const string GitHubReceiptEnvironment = "KEYLOAD_IMAGE_RECEIPT";
    private const string GitHubRevisionEnvironment = "GITHUB_SHA";
    private const string GitHubActionsEnvironment = "GITHUB_ACTIONS";
    private const string Provenance = "local-development";
    private const string Repository = "keyload/local-server";
    private const string ProvenanceField = "provenance";
    private const string ImageReferenceField = "imageReference";
    private const string InputDigestField = "inputDigest";
    private const string ImageConfigIdField = "imageConfigId";
    private const string InvocationIdField = "invocationId";
    private const int MaximumOutputBytes = 16_384;
    private const int MaximumReceiptPathCharacters = 256;
    private static readonly TimeSpan VerifyTimeout = TimeSpan.FromSeconds(30);

    internal sealed record Identity(string Reference, string Tag, string InvocationId, string ImageConfigId);

    internal static async Task<Identity?> VerifyBeforeStartAsync(DistributedApplication app, string root,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        var provenance = Environment.GetEnvironmentVariable(ProvenanceEnvironment);
        var reference = Environment.GetEnvironmentVariable(ReferenceEnvironment);
        var receipt = Environment.GetEnvironmentVariable(ReceiptEnvironment);
        if (provenance is null && reference is null && receipt is null)
        {
            return null;
        }
        ValidateEnvironment(provenance, reference, receipt);
        var tag = reference![(reference!.IndexOf(':', StringComparison.Ordinal) + 1)..];
        VerifyModel(app, tag);
        var script = Path.Combine(root, "scripts", "Features", "TestInfrastructure", "local-server-image.mjs");
        var output = await RunVerifierAsync(root, script, tag, receipt!, cancellationToken).ConfigureAwait(false);
        return ParseIdentity(output, reference!, tag);
    }

    internal static async Task VerifyStartedContainersAsync(Identity identity,
        IReadOnlyDictionary<string, string> containerNames, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(containerNames);
        if (containerNames.Count != ClusterFixtureProtocol.NodeCount)
        {
            throw new InvalidOperationException("The local RF3 container model is invalid.");
        }
        foreach (var nodeNumber in Enumerable.Range(ClusterFixtureProtocol.FirstNodeNumber, ClusterFixtureProtocol.NodeCount))
        {
            var node = ClusterFixtureProtocol.NodeName(nodeNumber);
            if (!containerNames.TryGetValue(node, out var name))
            {
                throw new InvalidOperationException("The local RF3 container model is invalid.");
            }
            var actual = await ContainerRuntimeDocker.InspectAsync(name, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(actual.ImageId, identity.ImageConfigId, StringComparison.Ordinal)
                || !string.Equals(actual.ConfigImage, identity.Reference, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("A started RF3 container does not use the verified local image.");
            }
        }
    }

    private static void ValidateEnvironment(string? provenance, string? reference, string? receipt)
    {
        if (provenance != Provenance || reference is null || receipt is null
            || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(GitHubReceiptEnvironment))
            || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(GitHubRevisionEnvironment))
            || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(GitHubActionsEnvironment))
            || receipt.Length is 0 or > MaximumReceiptPathCharacters || Path.IsPathRooted(receipt)
            || receipt.Contains('\\', StringComparison.Ordinal) || !reference.StartsWith(Repository + ":local-", StringComparison.Ordinal)
            || !IsCanonicalReferenceAndReceipt(reference, receipt))
        {
            throw new InvalidOperationException("The local RF3 image identity is invalid.");
        }
    }

    private static bool IsCanonicalReferenceAndReceipt(string reference, string receipt)
    {
        var separator = reference!.IndexOf(':', StringComparison.Ordinal);
        if (separator < 0 || separator == reference.Length - 1)
        {
            return false;
        }

        var tag = reference[(separator + 1)..];
        if (tag.Length != 38 || !tag.StartsWith("local-", StringComparison.Ordinal)
            || tag[6..].Length != 32 || tag[6..].Any(character => !char.IsAsciiHexDigit(character) || char.IsUpper(character)))
        {
            return false;
        }
        return receipt == $"TestResults/rf3/local-images/image-{tag[6..]}.json";
    }

    private static void VerifyModel(DistributedApplication app, string tag)
    {
        var model = app.Services.GetRequiredService<DistributedApplicationModel>();
        var nodes = model.Resources.OfType<ContainerResource>()
            .Where(resource => ClusterFixtureProtocol.IsNodeName(resource.Name)).ToArray();
        if (nodes.Length != ClusterFixtureProtocol.NodeCount)
        {
            throw new InvalidOperationException("The local RF3 container model is invalid.");
        }
        foreach (var node in nodes)
        {
            var image = node.Annotations.OfType<ContainerImageAnnotation>().Single();
            if (image.Image != Repository || image.Tag != tag || image.SHA256 is not null)
            {
                throw new InvalidOperationException("The local RF3 container model is invalid.");
            }
        }
    }

    internal static async Task<string> RunVerifierAsync(string root, string script, string tag, string receipt,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(VerifyTimeout);
        var start = new ProcessStartInfo("node")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add(script);
        start.ArgumentList.Add("verify");
        start.ArgumentList.Add(tag);
        start.ArgumentList.Add(receipt);
        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("The local RF3 image verifier could not start.");
        var output = LocalImageOwnedProcessLifetime.ReadBoundedAsync(process.StandardOutput, MaximumOutputBytes);
        var error = LocalImageOwnedProcessLifetime.ReadBoundedAsync(process.StandardError, MaximumOutputBytes);
        var exit = process.WaitForExitAsync(CancellationToken.None);
        try
        {
            await LocalImageOwnedProcessLifetime.ObserveAsync(exit, output, error, timeout.Token).ConfigureAwait(false);
            var streams = await Task.WhenAll(output, error).ConfigureAwait(false);
            if (process.ExitCode != 0 || streams[1].Length != 0)
            {
                throw new InvalidOperationException("The local RF3 image could not be verified.");
            }
            return streams[0];
        }
        catch (Exception primary)
        {
            var failures = new List<Exception> { primary };
            await LocalImageOwnedProcessLifetime.TerminateAndJoinAsync(process, exit, output, error, failures)
                .ConfigureAwait(false);
            if (failures.Count == 1)
            {
                throw;
            }

            throw new AggregateException("Local RF3 image verification and process settlement failed.", failures);
        }
    }

    private static Identity ParseIdentity(string output, string reference, string tag)
    {
        using var document = JsonDocument.Parse(output);
        var root = document.RootElement;
        var names = root.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray();
        if (!names.SequenceEqual([ImageConfigIdField, ImageReferenceField, InputDigestField, InvocationIdField, ProvenanceField],
                StringComparer.Ordinal)
            || root.GetProperty(ProvenanceField).GetString() != Provenance
            || root.GetProperty(ImageReferenceField).GetString() != reference
            || !IsSha256(root.GetProperty(InputDigestField).GetString())
            || !IsSha256(root.GetProperty(ImageConfigIdField).GetString()))
        {
            throw new InvalidOperationException("The local RF3 image receipt is invalid.");
        }
        var invocation = root.GetProperty(InvocationIdField).GetString();
        if (invocation is not { Length: 32 } || tag != "local-" + invocation)
        {
            throw new InvalidOperationException("The local RF3 image receipt is invalid.");
        }
        return new(reference, tag, invocation, root.GetProperty(ImageConfigIdField).GetString()!);
    }

    private static bool IsSha256(string? value) => value is { Length: 71 }
        && value.StartsWith("sha256:", StringComparison.Ordinal) && value[7..].All(Uri.IsHexDigit);
}
