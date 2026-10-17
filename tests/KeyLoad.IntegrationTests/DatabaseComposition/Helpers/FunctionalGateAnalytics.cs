using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

/// <summary>Exercises persisted native series through the same SDK and official MCP callers.</summary>
internal static class FunctionalGateAnalytics
{
    private const int First = 0;
    private const int Step = 1;
    private const int ResourceCount = 3;
    private const int InitialCount = 4;
    private const int FinalCount = 5;
    private const int RetainedFrom = 2;
    private const double InitialSum = 20;
    private const double InitialAverage = 5;
    private const double FullSum = 30;
    private const double RetainedSum = 24;
    private const int Year = 2026;
    private const int Month = 10;
    private const int Day = 2;
    private const double FirstValue = 2;
    private const double SecondValue = 4;
    private const double ThirdValue = 6;
    private const double FourthValue = 8;
    private const double FifthValue = 10;
    private static readonly DateTimeOffset Start = new(Year, Month, Day, First, First, First, TimeSpan.Zero);
    private static readonly double[] Values = [FirstValue, SecondValue, ThirdValue, FourthValue, FifthValue];

    internal static async Task InitializeAsync(FunctionalGateContext ctx, CancellationToken token)
    {
        for (var index = First; index < ResourceCount; index++)
        {
            var definition = new ResourceDefinition(Set(index), ResourceKind.TimeSeries, ctx.Partition.TransactionDomainId);
            var configured = await ctx.SdkAsync(ctx.Administrator.Sdk.ConfigureResourceAsync(Guid.NewGuid(),
                new(ctx.Partition.TenantId, ctx.Partition.DatabaseId, definition), token));
            await FunctionalGateAnalyticsAssertions.EqualAsync(configured, definition);
        }
    }

    internal static async Task ExerciseAsync(FunctionalGateContext ctx, int iteration, CancellationToken token)
    {
        var anchor = await ctx.SdkAsync(ctx.Reader.Sdk.GetAsync(ctx.Anchor, token));
        var official = await ctx.McpAsync<DocumentReadResult>(ctx.Reader, McpCallerTools.DocumentsGet, new GetDocumentRequest(ctx.Anchor), token);
        await Assert.That(anchor.Document).IsNotNull();
        await Assert.That(anchor.Document!.Reference).IsEqualTo(ctx.Anchor);
        await FunctionalGateAnalyticsAssertions.EqualAsync(official, anchor);
        for (var index = First; index < ResourceCount; index++)
        {
            var series = "gate-" + iteration.ToString(CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N");
            await ExerciseSeriesAsync(ctx, Set(index), series, token);
        }
    }

    private static async Task ExerciseSeriesAsync(FunctionalGateContext ctx, string set, string series, CancellationToken token)
    {
        var tags = JsonSerializer.Serialize(new
        {
            anchor = ctx.Bind(ctx.Anchor)
        }, JsonDefaults.Options);
        var samples = Samples(series);
        var expectedTags = ExpectedTags(ctx.Bind(ctx.Anchor));
        await CommitAsync(ctx, new AppendSamples(set, series, samples[..InitialCount], tags),
            "appendSamples", series, InitialCount, token);
        await FunctionalGateAnalyticsAssertions.ReadAsync(ctx, set, series,
            Expected(series, expectedTags, First, InitialCount), Start, new(InitialCount, InitialSum, FirstValue, FourthValue, InitialAverage), token);
        await CommitAsync(ctx, new AppendSamples(set, series, [samples[InitialCount]], tags),
            "appendSamples", series, FinalCount, token);
        await FunctionalGateAnalyticsAssertions.ReadAsync(ctx, set, series,
            Expected(series, expectedTags, First, FinalCount), Start, new(FinalCount, FullSum, FirstValue, FifthValue, ThirdValue), token);
        await CommitAsync(ctx, new ExpireSamples(set, series, Start.AddMinutes(RetainedFrom), FinalCount),
            "expireSamples", series, RetainedFrom, token);
        await FunctionalGateAnalyticsAssertions.ReadAsync(ctx, set, series,
            Expected(series, expectedTags, RetainedFrom, FinalCount), Start.AddMinutes(RetainedFrom),
            new(ResourceCount, RetainedSum, ThirdValue, FifthValue, FourthValue), token);
        await RetentionAsync(ctx, set, series, new(Start.AddMinutes(RetainedFrom), RetainedFrom, false), token);
        await CommitAsync(ctx, new ExpireSamples(set, series, Start.AddMinutes(FinalCount), FinalCount),
            "expireSamples", series, FinalCount, token);
        await FunctionalGateAnalyticsAssertions.ReadAsync(ctx, set, series, [], Start.AddMinutes(FinalCount),
            new(First, First, null, null, null), token);
        await RetentionAsync(ctx, set, series, new(Start.AddMinutes(FinalCount), FinalCount, false), token);
    }

    private static async Task CommitAsync(FunctionalGateContext ctx, Mutation mutation, string kind,
        string series, long revision, CancellationToken token)
    {
        var command = new CommandRequest(Guid.NewGuid(), ctx.Partition, [mutation]);
        var receipt = await ctx.SdkAsync(ctx.Writer.Sdk.CommitAsync(command, token));
        var replay = await ctx.McpAsync<CommitReceipt>(ctx.Writer, McpCallerTools.DocumentsCommit, command, token);
        await FunctionalGateAnalyticsAssertions.EqualAsync(replay, receipt);
        await Assert.That(receipt.CommandId).IsEqualTo(command.CommandId);
        await Assert.That(receipt.Token.Position).IsGreaterThan((long)First);
        await FunctionalGateAnalyticsAssertions.EqualAsync(receipt.Mutations,
            ImmutableArray.Create(new MutationReceipt(kind, mutation.Resource, series, revision)));
    }

    private static async Task RetentionAsync(FunctionalGateContext ctx, string set, string series,
        SampleRetentionStatus expected, CancellationToken token)
    {
        var request = new ReadSampleRetentionRequest(ctx.Partition, set, series);
        await FunctionalGateAnalyticsAssertions.EqualAsync(await ctx.SdkAsync(
            ctx.Reader.Sdk.ReadSampleRetentionAsync(request, token)), expected);
        await FunctionalGateAnalyticsAssertions.EqualAsync(await ctx.McpAsync<SampleRetentionStatus>(ctx.Reader,
            McpCallerTools.SeriesRetention, request, token), expected);
    }

    private static string ExpectedTags(EntityRef anchor)
        => FunctionalGateBoundJson.Serialize(new
        {
            anchor
        });

    private static ImmutableArray<SampleData> Samples(string series)
        => [.. Values.Select((value, index) => new SampleData(series + "-" + index.ToString(CultureInfo.InvariantCulture),
            Start.AddMinutes(index), value))];

    private static SampleRecord[] Expected(string series, string tags, int from, int until)
        => Samples(series).Skip(from).Take(until - from).Select((sample, index) =>
            new SampleRecord(series, sample, from + index + Step, tags)).ToArray();

    private static string Set(int index) => "gate_series_" + index.ToString(CultureInfo.InvariantCulture);
}
