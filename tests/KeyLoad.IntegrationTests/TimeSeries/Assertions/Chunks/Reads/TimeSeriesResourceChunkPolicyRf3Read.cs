using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class TimeSeriesResourceChunkPolicyRf3Read
{
    private const int ReadLimit = 2;
    private const long Generation = 1;
    private const long Revision = 4;
    private const long Sequence = 2;
    private const long ChangedSchema = 2;
    private const long NoPosition = 0;
    private const string PrivateTagValue = "private-marker";

    internal static ReadSampleChunkWindowRequest Request(TimeSeriesResourceTagPolicyRf3State state)
        => new(state.Scenario.Partition, TimeSeriesRf3Scenario.Set, TimeSeriesRf3Scenario.Series,
            state.WindowId, null, null, ReadLimit);

    internal static async Task AdministratorAsync(RequestCqrsRf3Callers administrator,
        TimeSeriesResourceTagPolicyRf3State state, CancellationToken token)
    {
        var failures = new List<Exception>();
        foreach (var official in new[] { false, true })
        {
            await ServerFailureObserver.ObserveAsync(() => SuccessAsync(administrator, state,
                Request(state), TimeSeriesRf3Scenario.PrivateTags, official, token), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal static async Task RunAsync(RequestCqrsRf3Callers reader, RequestCqrsRf3Callers administrator,
        TimeSeriesResourceTagPolicyRf3State state, CancellationToken token)
    {
        var changed = state.Current.SchemaVersion == ChangedSchema;
        var tags = changed ? TimeSeriesRf3Scenario.ProjectedTags : TimeSeriesRf3Scenario.PrivateTags;
        var predicate = Request(state) with
        {
            TagPointer = TimeSeriesRf3Scenario.SecretField,
            TagValue = PrivateTagValue
        };
        foreach (var official in new[] { false, true })
        {
            await ObserveAsync(() => SuccessAsync(reader, state, Request(state), tags, official, token),
                administrator, state, token);
            await ObserveAsync(() => changed
                ? TimeSeriesResourceChunkPolicyRf3Refusal.DeniedAsync(reader, predicate, official, token)
                : SuccessAsync(reader, state, predicate, tags, official, token), administrator, state, token);
        }
    }

    internal static async Task ObserveAsync(Func<Task> primary, RequestCqrsRf3Callers administrator,
        TimeSeriesResourceTagPolicyRf3State state, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(primary, failures);
        await ServerFailureObserver.ObserveAsync(() => TimeSeriesResourceTagPolicyRf3Assertions.CurrentAsync(
            administrator, state, token), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task SuccessAsync(RequestCqrsRf3Callers callers, TimeSeriesResourceTagPolicyRf3State state,
        ReadSampleChunkWindowRequest request, string tags, bool official, CancellationToken token)
    {
        var actual = official
            ? (await McpCallerAssertions.SuccessAsync<SampleChunkWindowResult>(
                await callers.Mcp.CallAsync(SampleChunkProtocol.ReadTool, request, token))).Value
            : await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ReadSampleChunkWindowAsync(request, token));
        await Assert.That(actual.CutPosition).IsGreaterThan(NoPosition);
        var expected = new SampleChunkWindowResult(state.WindowId, TimeSeriesRf3Scenario.Start,
            DateTimeOffset.MaxValue, Generation, Revision, Sequence, null,
            [.. TimeSeriesResourceTagPolicyRf3Assertions.Expected(tags)], actual.CutPosition);
        await SqlRf3Protocol.EqualAsync(expected, actual);
    }
}
