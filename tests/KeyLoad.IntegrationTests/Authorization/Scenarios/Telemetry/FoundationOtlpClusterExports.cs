using Microsoft.Extensions.Options;

namespace KeyLoad.IntegrationTests.Authorization;

internal static class FoundationOtlpClusterExports
{
    internal static async Task RunAsync(ClusterFixture fixture, FoundationOtlpClusterReceiver receiver,
        SensitiveAuthorityState state, string trace, IOptions<FoundationOtlpClusterExecutionOptions> options,
        TimeProvider clock, CancellationToken token)
    {
        var settings = options.Value;
        using var timeout = new CancellationTokenSource(settings.ObservationTimeout, clock);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token, timeout.Token);
        var observation = deadline.Token;
        foreach (var node in FoundationOtlpClusterProtocol.Nodes)
        {
            foreach (var logs in new[] { false, true })
            {
                await RequireCellAsync(fixture, receiver, state, trace, node, logs, options, clock, observation);
            }
        }
    }

    private static async Task RequireCellAsync(ClusterFixture fixture, FoundationOtlpClusterReceiver receiver,
        SensitiveAuthorityState state, string trace, string node, bool logs,
        IOptions<FoundationOtlpClusterExecutionOptions> options, TimeProvider clock, CancellationToken observation)
    {
        var settings = options.Value;
        var found = false;
        for (var attempt = 0; attempt < settings.MaximumAttempts; attempt++)
        {
            using var snapshot = await receiver.ReadSnapshotAsync(logs ? FoundationOtlpClusterProtocol.LogPath
                : FoundationOtlpClusterProtocol.SpanPath, node, trace, observation);
            if (await FoundationOtlpClusterSnapshotAssertions.HasExpectedAsync(snapshot, logs, node, trace,
                fixture, state, receiver, options))
            {
                found = true;
                break;
            }
            await Task.Delay(settings.PollInterval, clock, observation);
        }
        await Assert.That(found).IsTrue();
    }
}
