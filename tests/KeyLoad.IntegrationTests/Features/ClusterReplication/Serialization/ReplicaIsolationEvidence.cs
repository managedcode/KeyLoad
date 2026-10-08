using System.Text.Json;
using KeyLoad.AppHost.Features.ClusterReplication;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

/// <summary>Persists bounded actual native fault receipts outside the ephemeral store root before retirement.</summary>
internal static class ReplicaIsolationEvidence
{
    private const string Prefix = "rf3-owned-namespace-";
    private const string JsonExtension = ".json";
    private const string MutationsSuffix = "-mutations";
    private const string RetirementSuffix = "-retirement";
    private const string AuthoritySuffix = "-authority";
    private const string CountersSuffix = "-counters";
    private const int MaximumBytes = 1_048_576;
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    internal static Task WriteStartedAsync(string root, ClusterFixtureSourceImage source, ReplicaIsolationBuildPlan plan,
        ReplicaIsolationNode node, ReplicaIsolationImage original, ReplicaIsolationImage derived,
        ContainerRuntimeProcessResult tools, ContainerRuntimeProcessResult modules, CancellationToken cancellationToken)
        => WriteAsync(root, node.Resource, new { source, plan, node, original, derived, tools, modules }, cancellationToken);

    internal static Task WriteMutationsAsync(string root, IReadOnlyList<ReplicaIsolationMutation> mutations,
        IReadOnlyList<Exception> failures, CancellationToken cancellationToken)
        => WriteAsync(root, MutationsSuffix, new { mutations, failureCount = failures.Count }, cancellationToken);

    internal static Task WriteRetirementAsync(string root, IReadOnlyList<ReplicaIsolationRetirementObservation> observations,
        int failureCount, CancellationToken cancellationToken)
        => WriteAsync(root, RetirementSuffix, new { observations, failureCount }, cancellationToken);

    internal static Task WriteAuthorityAsync(string root, IReadOnlyList<ReplicaIsolationAuthorityObservation> observations,
        CancellationToken cancellationToken) => WriteAsync(root, AuthoritySuffix, observations, cancellationToken);

    internal static Task WriteCountersAsync(string root, ContainerRuntimeProcessResult input,
        ContainerRuntimeProcessResult output, CancellationToken cancellationToken)
        => WriteAsync(root, CountersSuffix, new { input, output }, cancellationToken);

    private static async Task WriteAsync<T>(string root, string suffix, T value, CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, Options);
        if (bytes.Length > MaximumBytes)
        { throw new InvalidOperationException("Actual namespace evidence exceeds its bounded diagnostic size."); }
        var repository = ClusterFixtureDiagnostics.FindRepositoryRoot().FullName;
        var directory = Path.Combine(repository, ContainerRuntimeProtocol.ArtifactsDirectory, ContainerRuntimeProtocol.QualificationDirectory);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, Prefix + Path.GetFileName(root) + suffix + JsonExtension);
        await using var output = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        await output.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}
