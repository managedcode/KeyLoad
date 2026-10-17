using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class SampleChunkCorrectionPredicateRf3Reads
{
    internal static async Task WaveAsync(RequestCqrsRf3Callers administrator, RequestCqrsRf3Callers creator,
        SampleChunkCorrectionPredicateRf3State state, long generation, long revision, CancellationToken token)
    {
        var request = state.Window.Request with
        {
            MinimumToken = state.Receipt.Token,
            Limit = SampleChunkCorrectionPredicateRf3State.OneResult,
            TagPointer = SampleChunkCorrectionPredicateRf3State.Pointer,
            TagValue = SampleChunkCorrectionPredicateRf3State.Hit
        };
        foreach (var official in new[] { false, true })
        {
            await RouteAsync(administrator, creator, state, generation, revision,
                () => WindowAsync(creator, state, request, generation, revision, [state.Rows[^1]], official, token), token);
            await RouteAsync(administrator, creator, state, generation, revision,
                () => WindowAsync(creator, state, request with
                {
                    TagValue = SampleChunkCorrectionPredicateRf3State.Miss
                }, generation, revision, [], official, token), token);
            await RouteAsync(administrator, creator, state, generation, revision,
                () => RefusedAsync(creator, state, request with
                {
                    TagPointer = null,
                    TagValue = null
                }, official, token), token);
            await RouteAsync(administrator, creator, state, generation, revision,
                () => ReplayAsync(creator, state.Scenario.OriginalSeal!, state.Scenario.OriginalReceipt!, official, token), token);
            await RouteAsync(administrator, creator, state, generation, revision,
                () => ReplayAsync(creator, state.Correction, state.Receipt, official, token), token);
        }
    }

    private static async Task WindowAsync(RequestCqrsRf3Callers creator, SampleChunkCorrectionPredicateRf3State state,
        ReadSampleChunkWindowRequest request, long generation, long revision, SampleRecord[] rows, bool official,
        CancellationToken token)
    {
        var actual = official ? (await McpCallerAssertions.SuccessAsync<SampleChunkWindowResult>(
            await creator.Mcp.CallAsync(SampleChunkProtocol.ReadTool, request, token))).Value
            : await McpCallerAssertions.SdkSuccessAsync(await creator.Sdk.ReadSampleChunkWindowAsync(request, token));
        await Assert.That(actual.CutPosition).IsGreaterThan(0L);
        await SampleChunkRetentionRf3ReadOracle.EqualAsync(actual,
            state.Expected(generation, revision, actual.CutPosition, rows));
        for (var index = 0; index < rows.Length; index++)
        {
            await Assert.That(BitConverter.DoubleToInt64Bits(actual.Records[index].Sample.Value))
                .IsEqualTo(BitConverter.DoubleToInt64Bits(rows[index].Sample.Value));
        }
    }

    private static async Task RefusedAsync(RequestCqrsRf3Callers creator, SampleChunkCorrectionPredicateRf3State state,
        ReadSampleChunkWindowRequest request, bool official, CancellationToken token)
    {
        if (!official)
        {
            var actual = await creator.Sdk.ReadSampleChunkWindowAsync(request, token);
            await Assert.That(actual.IsFailed).IsTrue();
            await Assert.That(actual.Value).IsNull();
            await SqlRf3Protocol.EqualAsync(Errors.Problem(ErrorCode.BudgetExceeded,
                TimeSeriesChunkQualificationRf3Data.ReadBoundExceeded), actual.Problem);
            return;
        }
        var reply = await creator.Mcp.CallAsync(SampleChunkProtocol.ReadTool, request, token);
        await McpCallerAssertions.ErrorAsync(reply, ErrorCode.BudgetExceeded, dispatched: true);
        var expected = JsonSerializer.SerializeToElement(Errors.Problem(ErrorCode.BudgetExceeded,
            TimeSeriesChunkQualificationRf3Data.SafeBoundExceeded), JsonDefaults.Options);
        await Assert.That(JsonElement.DeepEquals(expected,
            reply.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error))).IsTrue();
        await McpCallerAssertions.DoesNotDiscloseAsync(reply, state.Scenario.Secret,
            SampleChunkCorrectionPredicateRf3State.CorrectionTags);
    }

    private static async Task ReplayAsync(RequestCqrsRf3Callers creator, CommandRequest command,
        CommitReceipt expected, bool official, CancellationToken token)
    {
        var actual = official ? (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await creator.Mcp.CallAsync(McpCallerTools.DocumentsCommit, command, token))).Value
            : await McpCallerAssertions.SdkSuccessAsync(await creator.Sdk.CommitAsync(command, token));
        await SampleChunkRetentionRf3ReadOracle.EqualAsync(actual, expected);
    }

    private static async Task RouteAsync(RequestCqrsRf3Callers administrator, RequestCqrsRf3Callers creator,
        SampleChunkCorrectionPredicateRf3State state, long generation, long revision, Func<Task> operation,
        CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(operation, failures);
        await ServerFailureObserver.ObserveAsync(() => SampleChunkCorrectionPredicateRf3Conservation.RequireAsync(
            administrator, creator, state, generation, revision, token), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
