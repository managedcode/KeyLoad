using System.Text;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

internal static class FunctionalGateAnalyticsAssertions
{
    private const int Empty = 0;
    private const int MaximumDiagnosticCharacters = 4096;
    private const int MaximumSamples = 5;
    private const int EndMinute = 6;
    private const int Year = 2026;
    private const int Month = 10;
    private const int Day = 2;
    private static readonly DateTimeOffset Until = new(Year, Month, Day, Empty, EndMinute, Empty, TimeSpan.Zero);

    internal static async Task ReadAsync(FunctionalGateContext ctx, string set, string series,
        SampleRecord[] expected, DateTimeOffset from, SampleAggregate aggregate, CancellationToken token)
    {
        var raw = new ReadSamplesRequest(ctx.Partition, set, series, from, Until, MaximumSamples);
        await EqualAsync(await ctx.SdkAsync(ctx.Reader.Sdk.ReadSamplesAsync(raw, token)), expected);
        await EqualAsync(await ctx.McpAsync<SampleRecord[]>(ctx.Reader, McpCallerTools.SeriesRead, raw, token), expected);
        var request = new AggregateSamplesRequest(ctx.Partition, set, series, from, Until, MaximumSamples);
        await EqualAsync(await ctx.SdkAsync(ctx.Reader.Sdk.AggregateSamplesAsync(request, token)), aggregate);
        await EqualAsync(await ctx.McpAsync<SampleAggregate>(ctx.Reader, McpCallerTools.SeriesAggregate, request, token), aggregate);
        var latest = new ReadLatestSampleRequest(ctx.Partition, set, series);
        var expectedLatest = new LatestSampleResult(expected.Length == Empty ? null : expected[^1]);
        await EqualAsync(await ctx.SdkAsync(ctx.Reader.Sdk.ReadLatestSampleAsync(latest, token)), expectedLatest);
        await EqualAsync(await ctx.McpAsync<LatestSampleResult>(ctx.Reader, McpCallerTools.SeriesLatest, latest, token), expectedLatest);
    }

    internal static async Task EqualAsync<T>(T actual, T expected)
    {
        var observed = JsonDefaults.Serialize(actual);
        var required = JsonDefaults.Serialize(expected);
        await Assert.That(observed.AsSpan().SequenceEqual(required)).IsTrue()
            .Because($"actual[{observed.Length}]={Bounded(observed)} expected[{required.Length}]={Bounded(required)}");
    }

    private static string Bounded(byte[] value)
        => Encoding.UTF8.GetString(value.AsSpan(Empty, Math.Min(value.Length, MaximumDiagnosticCharacters)));
}
