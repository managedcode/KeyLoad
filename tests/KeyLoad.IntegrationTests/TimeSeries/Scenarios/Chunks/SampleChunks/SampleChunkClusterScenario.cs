using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal sealed record SampleChunkClusterScenario(TimeSeriesClusterScenario Execution, Guid WindowId,
    DateTimeOffset From, DateTimeOffset Until)
{
    internal static async Task<SampleChunkClusterScenario> CreateAsync(ClusterFixture fixture,
        KeyLoadClient sdk, CancellationToken token)
    {
        var execution = await TimeSeriesClusterScenario.CreateAsync(fixture, token).ConfigureAwait(false);
        var from = TimeProvider.System.GetUtcNow();
        var scenario = new SampleChunkClusterScenario(execution, Guid.NewGuid(), from, from.AddHours(SampleChunkClusterProtocol.WindowHours));
        await scenario.CommitAsync(sdk, new OpenSampleChunkWindow(TimeSeriesClusterScenario.Set,
            TimeSeriesClusterScenario.Series, scenario.WindowId, scenario.From, scenario.Until), token)
            .ConfigureAwait(false);
        return scenario;
    }

    internal ReadSampleChunkWindowRequest Request => new(Execution.Partition, TimeSeriesClusterScenario.Set,
        TimeSeriesClusterScenario.Series, WindowId, null, null, Limit: SampleChunkClusterProtocol.OutputLimit);
    internal SampleData First => new(SampleChunkClusterProtocol.FirstId, From, SampleChunkClusterProtocol.FirstValue);
    internal SampleData Equal => new(SampleChunkClusterProtocol.EqualId, From.ToOffset(TimeSpan.FromHours(SampleChunkClusterProtocol.AlternateOffsetHours)), SampleChunkClusterProtocol.SecondValue);
    internal SampleData Late => new(SampleChunkClusterProtocol.LateId, From.AddTicks(SampleChunkClusterProtocol.FirstSequence), SampleChunkClusterProtocol.NegativeZero);
    internal SampleRecord[] Expected =>
    [new(TimeSeriesClusterScenario.Series, First, SampleChunkClusterProtocol.FirstSequence, TimeSeriesClusterScenario.PrivateTags),
     new(TimeSeriesClusterScenario.Series, Equal, SampleChunkClusterProtocol.InitialSequence, TimeSeriesClusterScenario.PrivateTags),
     new(TimeSeriesClusterScenario.Series, Late, SampleChunkClusterProtocol.CorrectedSequence, TimeSeriesClusterScenario.PrivateTags)];

    internal async Task<CommitReceipt> CommitAsync(KeyLoadClient sdk, Mutation mutation,
        CancellationToken token)
        => await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(
            new CommandRequest(Guid.NewGuid(), Execution.Partition, [mutation]), token).ConfigureAwait(false));

    internal async Task<SampleChunkWindowResult> WaitForActualMergeAsync(KeyLoadClient sdk,
        CancellationToken token)
    {
        while (true)
        {
            token.ThrowIfCancellationRequested();
            var result = await McpCallerAssertions.SdkSuccessAsync(
                await sdk.ReadSampleChunkWindowAsync(Request, token).ConfigureAwait(false));
            if (result.Generation == SampleChunkClusterProtocol.MergedGeneration)
            {
                return result;
            }
            await Assert.That(result.Generation).IsEqualTo(SampleChunkClusterProtocol.SealedGeneration);
            await Assert.That(result.Revision).IsEqualTo(SampleChunkClusterProtocol.CorrectedRevision);
            await Task.Delay(SampleChunkClusterProtocol.PollInterval, token).ConfigureAwait(false);
        }
    }
}
