using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class ChunkMinimumRf3Refusals
{
    private const long InvalidEpoch = 0;
    private const long EpochStep = 1;
    private const long NegativePosition = -1;
    private const string ForeignPartition = "foreign-chunk-minimum";
    internal const string WrongIncarnation = "The document session token belongs to another incarnation.";
    internal const string WrongScope = "The document session token belongs to another atomic partition or placement.";
    internal const string FuturePosition = "The document session token is beyond the current quorum-applied cut.";
    internal const string InvalidPosition = "The document session token position must be positive.";
    internal const string ScopeDenied = "The principal cannot perform this operation in this scope.";
    private const string Exhausted = "The native series chunk window exceeds its admitted bound.";
    private const string PrivateValue = "private-marker";

    internal static async Task RunAsync(RequestCqrsRf3Callers reader, RequestCqrsRf3Callers administrator,
        ChunkMinimumRf3State state, ChunkMinimumRf3Window window, CancellationToken token)
    {
        var trials = new (CommitToken Token, string Detail)[]
        {
            (window.Minimum with { Incarnation = Guid.Empty }, WrongIncarnation),
            (window.Minimum with { AtomicPartitionId = ForeignPartition }, WrongScope),
            (window.Minimum with { OwnershipEpoch = checked(window.Minimum.OwnershipEpoch - EpochStep) }, WrongScope),
            (window.Minimum with { OwnershipEpoch = checked(window.Minimum.OwnershipEpoch + EpochStep) }, WrongScope),
            (window.Minimum with { Position = long.MaxValue }, FuturePosition),
            (window.Minimum with { Position = InvalidEpoch }, InvalidPosition),
            (window.Minimum with { Position = NegativePosition }, InvalidPosition)
        };
        foreach (var trial in trials)
        {
            foreach (var official in new[] { false, true })
            {
                var request = window.Request(state.Partition) with { MinimumToken = trial.Token };
                await ChunkMinimumRf3Pages.ObserveAsync(() => FailureAsync(reader, request,
                    ErrorCode.TokenInvalidated, trial.Detail, official, token), administrator, state, token);
            }
        }
        foreach (var official in new[] { false, true })
        {
            var invalid = window.Request(state.Partition) with
            {
                MinimumToken = window.Minimum with { Position = long.MaxValue },
                Limit = checked((int)InvalidEpoch)
            };
            await ChunkMinimumRf3Pages.ObserveAsync(() => FailureAsync(reader, invalid,
                ErrorCode.BudgetExceeded, Exhausted, official, token), administrator, state, token);
        }
    }

    internal static async Task FailureAsync(RequestCqrsRf3Callers reader, ReadSampleChunkWindowRequest request,
        ErrorCode code, string detail, bool official, CancellationToken token)
    {
        if (official)
        {
            var reply = await reader.Mcp.CallAsync(SampleChunkProtocol.ReadTool, request, token);
            await McpCallerAssertions.ErrorAsync(reply, code, dispatched: true);
            await McpCallerAssertions.DoesNotDiscloseAsync(reply, TimeSeriesRf3Scenario.PrivateTags, PrivateValue);
            return;
        }
        var failed = await reader.Sdk.ReadSampleChunkWindowAsync(request, token);
        await Assert.That(failed.IsFailed).IsTrue();
        await Assert.That(failed.Value).IsNull();
        await SqlRf3Protocol.EqualAsync(Errors.Problem(code, detail), failed.Problem);
    }
}
