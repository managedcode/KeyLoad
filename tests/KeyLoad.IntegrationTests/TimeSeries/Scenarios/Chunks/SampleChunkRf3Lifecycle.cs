using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class SampleChunkRf3Lifecycle
{
    internal static async Task<SampleChunkRf3Continuation> ExecuteAsync(ClusterFixture fixture, KeyLoadClient sdk,
        McpOfficialClient mcp, CancellationToken token)
    {
        var scenario = await SampleChunkRf3Scenario.CreateAsync(fixture, sdk, token).ConfigureAwait(false);
        var append = await scenario.CommitAsync(sdk, new AppendSamples(TimeSeriesRf3Scenario.Set,
            TimeSeriesRf3Scenario.Series, [scenario.First, scenario.Equal], TimeSeriesRf3Scenario.PrivateTags), token)
            .ConfigureAwait(false);
        var original = new CommandRequest(Guid.NewGuid(), scenario.Native.Partition,
            [new SealSampleChunkWindow(TimeSeriesRf3Scenario.Set, TimeSeriesRf3Scenario.Series,
                scenario.WindowId, SampleChunkRf3Protocol.AppendedRevision)]);
        var seal = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(original, token).ConfigureAwait(false));
        await OriginalSealAsync(scenario, sdk, original, seal, append, token);
        var correction = await scenario.CommitAsync(sdk, new AppendSamples(TimeSeriesRf3Scenario.Set,
            TimeSeriesRf3Scenario.Series, [scenario.Late], TimeSeriesRf3Scenario.PrivateTags), token).ConfigureAwait(false);
        await Assert.That(correction.Token.Position > seal.Token.Position).IsTrue();
        var merged = await scenario.WaitForActualMergeAsync(sdk, token).ConfigureAwait(false);
        await SampleChunkRf3Assertions.CompleteAsync(scenario, merged, correction);
        await SampleChunkRf3Assertions.AllReadRoutesAsync(scenario, sdk, mcp, correction, token);
        await SampleChunkRf3Assertions.OriginalReceiptAsync(sdk, mcp, original, seal, token);
        await SampleChunkRf3Assertions.AllReadRoutesAsync(scenario, sdk, mcp, correction, token);
        await SampleChunkRf3Privacy.ExecuteAsync(fixture, scenario, sdk, correction, token);
        return new(scenario, original, seal, correction);
    }

    private static async Task OriginalSealAsync(SampleChunkRf3Scenario scenario, KeyLoadClient sdk,
        CommandRequest original, CommitReceipt actual, CommitReceipt previous, CancellationToken token)
    {
        var owner = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReadAtomicPartitionPlacementAsync(
            new(SampleChunkRf3Protocol.PlacementVersion, scenario.Native.Partition), token).ConfigureAwait(false));
        await Assert.That(owner.Partition).IsEqualTo(scenario.Native.Partition);
        await Assert.That(actual.Token.Position > previous.Token.Position).IsTrue();
        var expected = new CommitReceipt(original.CommandId, new CommitToken(owner.Incarnation,
            scenario.Native.Partition.AtomicPartitionId, actual.Token.Position, owner.PlacementEpoch),
            [new MutationReceipt(SampleChunkProtocol.SealKind, TimeSeriesRf3Scenario.Set,
                TimeSeriesRf3Scenario.Series, SampleChunkRf3Protocol.SealedRevision)],
            DurabilityProfile.QuorumProcessDurable);
        await Assert.That(Convert.ToHexString(JsonDefaults.Serialize(actual)))
            .IsEqualTo(Convert.ToHexString(JsonDefaults.Serialize(expected)));
    }
}
