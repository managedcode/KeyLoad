using Microsoft.Extensions.Options;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class FoundationOtlpRf3Exports
{
    internal static async Task RunAsync(ClusterFixture fixture, FoundationOtlpRf3Receiver receiver,
        SensitiveAuthorityState state, string trace, IOptions<FoundationOtlpRf3ExecutionOptions> options,
        TimeProvider clock, CancellationToken token)
    {
        var settings = options.Value;
        using var timeout = new CancellationTokenSource(settings.ObservationTimeout, clock);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token, timeout.Token);
        var observation = deadline.Token;
        foreach (var node in FoundationOtlpRf3Protocol.Nodes)
        {
            foreach (var logs in new[] { false, true })
            {
                await RequireCellAsync(fixture, receiver, state, trace, node, logs, options, clock, observation);
            }
        }
    }

    private static async Task RequireCellAsync(ClusterFixture fixture, FoundationOtlpRf3Receiver receiver,
        SensitiveAuthorityState state, string trace, string node, bool logs,
        IOptions<FoundationOtlpRf3ExecutionOptions> options, TimeProvider clock, CancellationToken observation)
    {
        var settings = options.Value;
        var found = false;
        for (var attempt = 0; attempt < settings.MaximumAttempts; attempt++)
        {
            using var snapshot = await receiver.ReadSnapshotAsync(logs ? FoundationOtlpRf3Protocol.LogPath
                : FoundationOtlpRf3Protocol.SpanPath, node, trace, observation);
            if (await FoundationOtlpRf3SnapshotAssertions.HasExpectedAsync(snapshot, logs, node, trace,
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
