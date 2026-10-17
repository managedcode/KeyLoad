using System.Diagnostics;
using System.Text.Json;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.IntegrationTests.Features.ClusterReplication.Processes;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

/// <summary>Proves the local-development image receipt and the expected started Docker containers.</summary>
internal static class LocalRf3ImageIdentity
{
    private const string ProvenanceField = "provenance";
    private const string ImageReferenceField = "imageReference";
    private const string InputDigestField = "inputDigest";
    private const string ImageConfigIdField = "imageConfigId";
    private const string InvocationIdField = "invocationId";
    private const string NodeResourcePrefix = "node";
    private const int MaximumOutputBytes = 16_384;
    private static readonly TimeSpan VerifyTimeout = TimeSpan.FromSeconds(30);

    internal sealed record Identity(string Reference, string Tag, string InvocationId, string ImageConfigId, string InputDigest);
    internal static Task<Identity?> ReadVerifiedAsync(string root, CancellationToken cancellationToken)
        => ReadVerifiedAsync(root, LocalRf3ImageSelection.Read(), cancellationToken);

    internal static Task<Identity?> ReadVerifiedAsync(string root, LocalRf3ImageSelection.Selection? selection,
        CancellationToken cancellationToken)
        => ReadVerifiedAsync(root, selection, null, cancellationToken);

    internal static async Task<Identity?> ReadVerifiedAsync(string root, LocalRf3ImageSelection.Selection? selection,
        Action<LocalImageVerifierPhase>? observe, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        if (selection is null)
        {
            return null;
        }
        var script = Path.Combine(root, "scripts", "Features", "TestInfrastructure", "local-server-image.mjs");
        var output = await RunVerifierAsync(root, script, selection.Tag, selection.Receipt, observe, cancellationToken)
            .ConfigureAwait(false);
        return ParseIdentity(output, selection.Reference, selection.Tag);
    }

    internal static async Task<Identity?> VerifyBeforeStartAsync(DistributedApplication app, string root,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(app);
        var identity = await ReadVerifiedAsync(root, cancellationToken).ConfigureAwait(false);
        if (identity is not null)
        {
            VerifyModel(app, identity, ThreeNodeNames());
        }
        return identity;
    }

    internal static async Task<Identity> VerifyBeforeStartAsync(DistributedApplication app, string root,
        LocalRf3ImageSelection.Selection selection, IReadOnlyCollection<string> expectedNames,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(selection);
        var identity = await ReadVerifiedAsync(root, selection, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The local RF3 image identity is invalid.");
        VerifyModel(app, identity, expectedNames);
        return identity;
    }

    internal static Task VerifyStartedContainersAsync(Identity identity,
        IReadOnlyDictionary<string, string> containerNames, CancellationToken cancellationToken)
        => VerifyStartedContainersAsync(identity, containerNames, ThreeNodeNames(), cancellationToken);

    internal static async Task VerifyStartedContainersAsync(Identity identity,
        IReadOnlyDictionary<string, string> containerNames, IReadOnlyCollection<string> expectedNames,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(containerNames);
        var expected = ValidateExpectedNames(expectedNames);
        if (containerNames.Count != expected.Length || expected.Any(name => !containerNames.ContainsKey(name))
            || containerNames.Any(pair => !expected.Contains(pair.Key, StringComparer.Ordinal)
                || string.IsNullOrWhiteSpace(pair.Value)))
        {
            throw new InvalidOperationException("The local RF3 container model is invalid.");
        }
        foreach (var node in expected)
        {
            var actual = await ContainerRuntimeDocker.InspectAsync(containerNames[node], cancellationToken)
                .ConfigureAwait(false);
            if (!string.Equals(actual.ImageId, identity.ImageConfigId, StringComparison.Ordinal)
                || !string.Equals(actual.ConfigImage, identity.Reference, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("A started RF3 container does not use the verified local image.");
            }
        }
    }

    private static void VerifyModel(DistributedApplication app, Identity identity,
        IReadOnlyCollection<string> expectedNames)
    {
        var expected = ValidateExpectedNames(expectedNames);
        var model = app.Services.GetRequiredService<DistributedApplicationModel>();
        var nodes = model.Resources.OfType<ContainerResource>()
            .Where(resource => resource.Name.StartsWith(NodeResourcePrefix, StringComparison.Ordinal)).ToArray();
        if (!nodes.Select(resource => resource.Name).Order(StringComparer.Ordinal)
                .SequenceEqual(expected, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("The local RF3 container model is invalid.");
        }
        foreach (var node in nodes)
        {
            var image = node.Annotations.OfType<ContainerImageAnnotation>().Single();
            if (image.Image != LocalRf3ImageSelection.Repository || image.Tag != identity.Tag || image.SHA256 is not null
                || !node.TryGetContainerImageName(out var actual)
                || !string.Equals(actual, identity.Reference, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The local RF3 container model is invalid.");
            }
        }
    }

    private static string[] ValidateExpectedNames(IReadOnlyCollection<string> expectedNames)
    {
        ArgumentNullException.ThrowIfNull(expectedNames);
        const int MaximumNodeCount = 6;
        var expected = expectedNames.Order(StringComparer.Ordinal).ToArray();
        if (expected.Length is 0 or > MaximumNodeCount
            || expected.Any(string.IsNullOrWhiteSpace)
            || expected.Distinct(StringComparer.Ordinal).Count() != expected.Length)
        {
            throw new InvalidOperationException("The local RF3 container model is invalid.");
        }
        return expected;
    }

    private static string[] ThreeNodeNames()
        => Enumerable.Range(ClusterFixtureProtocol.FirstNodeNumber, ClusterFixtureProtocol.NodeCount)
            .Select(ClusterFixtureProtocol.NodeName).ToArray();

    internal static Task<string> RunVerifierAsync(string root, string script, string tag, string receipt,
        CancellationToken cancellationToken)
        => RunVerifierAsync(root, script, tag, receipt, null, cancellationToken);

    internal static async Task<string> RunVerifierAsync(string root, string script, string tag, string receipt,
        Action<LocalImageVerifierPhase>? observe, CancellationToken cancellationToken)
    {
        using var timeoutTimeout = new CancellationTokenSource(VerifyTimeout, TimeProvider.System);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutTimeout.Token);
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
            await LocalImageVerifierFailurePhase.RecordAsync(exit, output, error, observe, failures).ConfigureAwait(false);
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
            || root.GetProperty(ProvenanceField).GetString() != LocalRf3ImageSelection.Provenance
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
        return new(reference, tag, invocation, root.GetProperty(ImageConfigIdField).GetString()!,
            root.GetProperty(InputDigestField).GetString()!);
    }

    private static bool IsSha256(string? value) => value is { Length: 71 }
        && value.StartsWith("sha256:", StringComparison.Ordinal) && value[7..].All(Uri.IsHexDigit);
}
