using KeyLoad.Client;
using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.OrleansExecution;
using KeyLoad.ClusterTests.QueryExecution;
using KeyLoad.Server;

namespace KeyLoad.ClusterTests.TimeSeries;

internal static class TimeSeriesResourceChunkPolicyClusterRead
{
    private const int ReadLimit = 2;
    private const long Generation = 1;
    private const long Revision = 4;
    private const long Sequence = 2;
    private const long ChangedSchema = 2;
    private const long NoPosition = 0;
    private const string PrivateTagValue = "private-marker";

    internal static ReadSampleChunkWindowRequest Request(TimeSeriesResourceTagPolicyClusterState state)
        => new(state.Scenario.Partition, TimeSeriesClusterScenario.Set, TimeSeriesClusterScenario.Series,
            state.WindowId, null, null, ReadLimit);

    internal static async Task AdministratorAsync(RequestCqrsClusterCallers administrator,
        TimeSeriesResourceTagPolicyClusterState state, CancellationToken token)
    {
        var failures = new List<Exception>();
        foreach (var official in new[] { false, true })
        {
            await ServerFailureObserver.ObserveAsync(() => SuccessAsync(administrator, state,
                Request(state), TimeSeriesClusterScenario.PrivateTags, official, token), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal static async Task RunAsync(RequestCqrsClusterCallers reader, RequestCqrsClusterCallers administrator,
        TimeSeriesResourceTagPolicyClusterState state, CancellationToken token)
    {
        var changed = state.Current.SchemaVersion == ChangedSchema;
        var tags = changed ? TimeSeriesClusterScenario.ProjectedTags : TimeSeriesClusterScenario.PrivateTags;
        var predicate = Request(state) with
        {
            TagPointer = TimeSeriesClusterScenario.SecretField,
            TagValue = PrivateTagValue
        };
        foreach (var official in new[] { false, true })
        {
            await ObserveAsync(() => SuccessAsync(reader, state, Request(state), tags, official, token),
                administrator, state, token);
            await ObserveAsync(() => changed
                ? TimeSeriesResourceChunkPolicyClusterRefusal.DeniedAsync(reader, predicate, official, token)
                : SuccessAsync(reader, state, predicate, tags, official, token), administrator, state, token);
        }
    }

    internal static async Task ObserveAsync(Func<Task> primary, RequestCqrsClusterCallers administrator,
        TimeSeriesResourceTagPolicyClusterState state, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(primary, failures);
        await ServerFailureObserver.ObserveAsync(() => TimeSeriesResourceTagPolicyClusterAssertions.CurrentAsync(
            administrator, state, token), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task SuccessAsync(RequestCqrsClusterCallers callers, TimeSeriesResourceTagPolicyClusterState state,
        ReadSampleChunkWindowRequest request, string tags, bool official, CancellationToken token)
    {
        var actual = official
            ? (await McpCallerAssertions.SuccessAsync<SampleChunkWindowResult>(
                await callers.Mcp.CallAsync(SampleChunkProtocol.ReadTool, request, token))).Value
            : await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ReadSampleChunkWindowAsync(request, token));
        await Assert.That(actual.CutPosition).IsGreaterThan(NoPosition);
        var expected = new SampleChunkWindowResult(state.WindowId, TimeSeriesClusterScenario.Start,
            DateTimeOffset.MaxValue, Generation, Revision, Sequence, null,
            [.. TimeSeriesResourceTagPolicyClusterAssertions.Expected(tags)], actual.CutPosition);
        await SqlClusterProtocol.EqualAsync(expected, actual);
    }
}
