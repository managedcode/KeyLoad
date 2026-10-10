using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.IntegrationTests.Features.ClusterRouting.Helpers;
using KeyLoad.IntegrationTests.Features.StorageRecovery;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class RequestCqrsRf3McpGuardEvidenceScenario
{
    private const string MissingWaveMessage = "The joined C1 wave did not retain its diagnostics owner.";
    private const string Node1 = RequestCqrsRf3Protocol.Node1;

    internal static async Task<string> ExecuteAsync(string root, NodeEpochRf3Profile profile,
        string currentImage, RequestCqrsLifecycleEvidence lifecycle, CancellationToken cancellationToken)
    {
        var images = All(currentImage);
        RequestCqrsRf3Wave? completedWave = null;
        await RequestCqrsRf3Epoch7WaveRunner.RunObservedAsync(root, images, false, true, async wave =>
        {
            completedWave = wave;
            await ExecuteInWaveAsync(wave, profile, lifecycle, cancellationToken).ConfigureAwait(false);
        }, new(lifecycle), cancellationToken).ConfigureAwait(false);
        var joined = completedWave ?? throw new InvalidOperationException(MissingWaveMessage);
        await Assert.That(joined.NativeAdmissionOwnersJoined).IsTrue();
        return joined.SaveDiagnosticsEvidence();
    }

    internal static async Task<string> ExecuteFailureCleanupAsync(string root, NodeEpochRf3Profile profile,
        string currentImage, RequestCqrsLifecycleEvidence lifecycle, CancellationToken cancellationToken)
    {
        RequestCqrsRf3Wave? wave = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            wave = await RequestCqrsRf3Wave.StartAsync(root, All(currentImage), false, true, cancellationToken)
                .ConfigureAwait(false);
            var workload = await RequestCqrsRf3Workload.SeedAsync(wave.App, profile, cancellationToken).ConfigureAwait(false);
            await RequestCqrsRf3McpGuardEvidenceCall.SendFailureAfterHealthyAsync(wave.App, Node1, profile.AdminKey,
                lifecycle, () => VerifyWarningAndHealthyAsync(wave, workload, profile, cancellationToken),
                cancellationToken).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        var original = failures.FirstOrDefault();
        await RequestCqrsPhaseFaultCleanup.RunAsync(root, true, null, wave, true, null, null, null,
            null, null, null, null, Guid.Empty, failures).ConfigureAwait(false);
        await Assert.That(original is HttpRequestException { StatusCode: System.Net.HttpStatusCode.BadRequest }).IsTrue();
        await Assert.That(failures.Count).IsEqualTo(1);
        await Assert.That(ReferenceEquals(failures[0], original)).IsTrue();
        var joined = wave ?? throw new InvalidOperationException(MissingWaveMessage);
        await Assert.That(joined.NativeAdmissionOwnersJoined).IsTrue();
        await Assert.That(Directory.Exists(root)).IsFalse();
        var path = joined.SaveDiagnosticsEvidence();
        await Assert.That(original!.Data.Values.OfType<string>().Contains(path, StringComparer.Ordinal)).IsTrue();
        return path;
    }

    private static async Task VerifyWarningAndHealthyAsync(RequestCqrsRf3Wave wave,
        RequestCqrsRf3Workload workload, NodeEpochRf3Profile profile, CancellationToken cancellationToken)
    {
        await wave.WaitForRejectionAsync(Node1, McpTransportStage.BodyMethodMismatch,
            McpTransportMethodCategory.ToolsCall, cancellationToken).ConfigureAwait(false);
        await workload.VerifyPreservedAsync(wave.App, profile, cancellationToken).ConfigureAwait(false);
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

    private static async Task ExecuteInWaveAsync(RequestCqrsRf3Wave wave, NodeEpochRf3Profile profile,
        RequestCqrsLifecycleEvidence lifecycle, CancellationToken cancellationToken)
    {
        await Assert.That(wave.NativeAdmittedNodeCount).IsEqualTo(RequestCqrsRf3Protocol.NodeCount);
        var app = wave.App;
        lifecycle.SetStage(RequestCqrsLifecycleStage.GuardSeed);
        var workload = await RequestCqrsRf3Workload.SeedAsync(app, profile, cancellationToken).ConfigureAwait(false);
        await RequestCqrsRf3McpGuardEvidenceCall.SendMalformedAsync(app, Node1, profile.AdminKey, lifecycle,
            cancellationToken).ConfigureAwait(false);
        lifecycle.SetStage(RequestCqrsLifecycleStage.GuardWarningWait);
        await wave.WaitForRejectionAsync(Node1, McpTransportStage.BodyMethodMismatch,
            McpTransportMethodCategory.ToolsCall, cancellationToken).ConfigureAwait(false);
        lifecycle.SetStage(RequestCqrsLifecycleStage.GuardHealthyCallers);
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
