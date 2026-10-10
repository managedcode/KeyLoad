using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal sealed record SampleChunkRf3Scenario(TimeSeriesRf3Scenario Native, Guid WindowId,
    DateTimeOffset From, DateTimeOffset Until)
{
    internal static async Task<SampleChunkRf3Scenario> CreateAsync(ClusterFixture fixture,
        KeyLoadClient sdk, CancellationToken token)
    {
        var native = await TimeSeriesRf3Scenario.CreateAsync(fixture, token).ConfigureAwait(false);
        var from = TimeProvider.System.GetUtcNow();
        var scenario = new SampleChunkRf3Scenario(native, Guid.NewGuid(), from, from.AddHours(SampleChunkRf3Protocol.WindowHours));
        await scenario.CommitAsync(sdk, new OpenSampleChunkWindow(TimeSeriesRf3Scenario.Set,
            TimeSeriesRf3Scenario.Series, scenario.WindowId, scenario.From, scenario.Until), token)
            .ConfigureAwait(false);
        return scenario;
    }

    internal ReadSampleChunkWindowRequest Request => new(Native.Partition, TimeSeriesRf3Scenario.Set,
        TimeSeriesRf3Scenario.Series, WindowId, null, null, Limit: SampleChunkRf3Protocol.OutputLimit);
    internal SampleData First => new(SampleChunkRf3Protocol.FirstId, From, SampleChunkRf3Protocol.FirstValue);
    internal SampleData Equal => new(SampleChunkRf3Protocol.EqualId, From.ToOffset(TimeSpan.FromHours(SampleChunkRf3Protocol.AlternateOffsetHours)), SampleChunkRf3Protocol.SecondValue);
    internal SampleData Late => new(SampleChunkRf3Protocol.LateId, From.AddTicks(SampleChunkRf3Protocol.FirstSequence), SampleChunkRf3Protocol.NegativeZero);
    internal SampleRecord[] Expected =>
    [new(TimeSeriesRf3Scenario.Series, First, SampleChunkRf3Protocol.FirstSequence, TimeSeriesRf3Scenario.PrivateTags),
     new(TimeSeriesRf3Scenario.Series, Equal, SampleChunkRf3Protocol.InitialSequence, TimeSeriesRf3Scenario.PrivateTags),
     new(TimeSeriesRf3Scenario.Series, Late, SampleChunkRf3Protocol.CorrectedSequence, TimeSeriesRf3Scenario.PrivateTags)];

    internal async Task<CommitReceipt> CommitAsync(KeyLoadClient sdk, Mutation mutation,
        CancellationToken token)
        => await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(
            new CommandRequest(Guid.NewGuid(), Native.Partition, [mutation]), token).ConfigureAwait(false));

    internal async Task<SampleChunkWindowResult> WaitForActualMergeAsync(KeyLoadClient sdk,
        CancellationToken token)
    {
        while (true)
        {
            token.ThrowIfCancellationRequested();
            var result = await McpCallerAssertions.SdkSuccessAsync(
                await sdk.ReadSampleChunkWindowAsync(Request, token).ConfigureAwait(false));
            if (result.Generation == SampleChunkRf3Protocol.MergedGeneration)
            { return result; }
            await Assert.That(result.Generation).IsEqualTo(SampleChunkRf3Protocol.SealedGeneration);
            await Assert.That(result.Revision).IsEqualTo(SampleChunkRf3Protocol.CorrectedRevision);
            await Task.Delay(SampleChunkRf3Protocol.PollInterval, token).ConfigureAwait(false);
        }
    }
}
