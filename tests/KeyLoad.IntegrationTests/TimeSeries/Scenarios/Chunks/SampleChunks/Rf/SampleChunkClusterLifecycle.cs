using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class SampleChunkClusterLifecycle
{
    internal static async Task<SampleChunkClusterContinuation> ExecuteAsync(ClusterFixture fixture, KeyLoadClient sdk,
        McpOfficialClient mcp, CancellationToken token)
    {
        var scenario = await SampleChunkClusterScenario.CreateAsync(fixture, sdk, token).ConfigureAwait(false);
        var append = await scenario.CommitAsync(sdk, new AppendSamples(TimeSeriesClusterScenario.Set,
            TimeSeriesClusterScenario.Series, [scenario.First, scenario.Equal], TimeSeriesClusterScenario.PrivateTags), token)
            .ConfigureAwait(false);
        var original = new CommandRequest(Guid.NewGuid(), scenario.Native.Partition,
            [new SealSampleChunkWindow(TimeSeriesClusterScenario.Set, TimeSeriesClusterScenario.Series,
                scenario.WindowId, SampleChunkClusterProtocol.AppendedRevision)]);
        var seal = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(original, token).ConfigureAwait(false));
        await OriginalSealAsync(scenario, original, seal, append);
        var correction = await scenario.CommitAsync(sdk, new AppendSamples(TimeSeriesClusterScenario.Set,
            TimeSeriesClusterScenario.Series, [scenario.Late], TimeSeriesClusterScenario.PrivateTags), token).ConfigureAwait(false);
        await Assert.That(correction.Token.Position > seal.Token.Position).IsTrue();
        var merged = await scenario.WaitForActualMergeAsync(sdk, token).ConfigureAwait(false);
        await SampleChunkClusterAssertions.CompleteAsync(scenario, merged, correction);
        await SampleChunkClusterAssertions.AllReadRoutesAsync(scenario, sdk, mcp, correction, token);
        await SampleChunkClusterAssertions.OriginalReceiptAsync(sdk, mcp, original, seal, token);
        await SampleChunkClusterAssertions.AllReadRoutesAsync(scenario, sdk, mcp, correction, token);
        await SampleChunkClusterPrivacy.ExecuteAsync(fixture, scenario, sdk, correction, token);
        return new(scenario, original, seal, correction);
    }

    private static async Task OriginalSealAsync(SampleChunkClusterScenario scenario, CommandRequest original, CommitReceipt actual, CommitReceipt previous)
    {
        await Assert.That(actual.Token.Position > previous.Token.Position).IsTrue();
        var expected = new CommitReceipt(original.CommandId, new CommitToken(actual.Token.Incarnation,
            scenario.Native.Partition.AtomicPartitionId, actual.Token.Position, actual.Token.OwnershipEpoch),
            [new MutationReceipt(SampleChunkProtocol.SealKind, TimeSeriesClusterScenario.Set,
                TimeSeriesClusterScenario.Series, SampleChunkClusterProtocol.SealedRevision)],
            DurabilityProfile.QuorumProcessDurable);
        await Assert.That(Convert.ToHexString(JsonDefaults.Serialize(actual)))
            .IsEqualTo(Convert.ToHexString(JsonDefaults.Serialize(expected)));
    }
}
