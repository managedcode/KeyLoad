using Aspire.Hosting;
using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.IntegrationTests.Features.StorageRecovery;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class RequestCqrsRf3McpGuardEvidenceScenario
{
    private const string MissingWaveMessage = "The joined C1 wave did not retain its diagnostics owner.";
    private const string Node1 = RequestCqrsRf3Protocol.Node1;

    internal static async Task<string> ExecuteAsync(string root, NodeEpochRf3Profile profile,
        string currentImage, CancellationToken cancellationToken)
    {
        var images = All(currentImage);
        RequestCqrsRf3Wave? completedWave = null;
        await RequestCqrsRf3Epoch7WaveRunner.RunAsync(root, images, false, true, async wave =>
        {
            completedWave = wave;
            await ExecuteInWaveAsync(wave.App, profile, cancellationToken).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
        return (completedWave ?? throw new InvalidOperationException(MissingWaveMessage)).SaveDiagnosticsEvidence();
    }

    internal static string CreatePrivateRoot()
    {
        var path = Path.Combine(ClusterFixtureDiagnostics.FindRepositoryRoot().FullName,
            "artifacts", "qualification", "c1-mcp-guard-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        if (!OperatingSystem.IsWindows())
        { File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); }
        return path;
    }

    private static async Task ExecuteInWaveAsync(DistributedApplication app, NodeEpochRf3Profile profile,
        CancellationToken cancellationToken)
    {
        var workload = await RequestCqrsRf3Workload.SeedAsync(app, profile, cancellationToken).ConfigureAwait(false);
        await RequestCqrsRf3McpGuardEvidenceCall.SendMalformedAsync(app, Node1, profile.AdminKey,
            cancellationToken).ConfigureAwait(false);
        await workload.VerifyPreservedAsync(app, profile, cancellationToken).ConfigureAwait(false);
    }

    private static Dictionary<string, string> All(string image)
        => new(StringComparer.Ordinal)
        {
            [RequestCqrsRf3Protocol.Node1] = image,
            [RequestCqrsRf3Protocol.Node2] = image,
            [RequestCqrsRf3Protocol.Node3] = image
        };
}
