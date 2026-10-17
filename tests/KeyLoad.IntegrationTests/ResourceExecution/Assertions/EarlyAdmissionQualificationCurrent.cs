using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server;
using static KeyLoad.IntegrationTests.Features.ResourceExecution.EarlyAdmissionQualificationProtocol;

namespace KeyLoad.IntegrationTests.Features.ResourceExecution;

internal static class EarlyAdmissionQualificationCurrent
{
    internal static async Task ObserveAsync(RequestCqrsRf3Callers root, EarlyAdmissionQualificationState state,
        Func<Task> operation, bool held, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(operation, failures);
        await ServerFailureObserver.ObserveAsync(() => held ? LightAsync(root, state, token) : FullAsync(root, state, token), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    internal static async Task LightAsync(RequestCqrsRf3Callers root, EarlyAdmissionQualificationState state,
        CancellationToken token)
    {
        foreach (var row in Rows())
        {
            var reference = new EntityRef(state.Partition, Collection, row.EntityId);
            var expected = new DocumentReadResult(new(reference, Revision, row.Json, false, []));
            await SqlRf3Protocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(
                await root.Sdk.GetAsync(reference, state.Receipt.Token, token)));
            await SqlRf3Protocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<DocumentReadResult>(
                await root.Mcp.CallAsync(McpCallerTools.DocumentsGet, new GetDocumentRequest(reference, state.Receipt.Token), token))).Value);
        }
        await SqlRf3Protocol.EqualAsync(state.Outbox,
            await QueryCursorRequestIdentityR47Current.OutboxAsync(root, state.Partition, token));
    }
    internal static async Task FullAsync(RequestCqrsRf3Callers root, EarlyAdmissionQualificationState state,
        CancellationToken token)
    {
        await LightAsync(root, state, token);
        var request = new QueryRequest(state.Partition, EarlyAdmissionQualificationProtocol.Query, AllowFullScan: true);
        await PageAsync(await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.QueryAsync(request, token)));
        await PageAsync((await McpCallerAssertions.SuccessAsync<QueryPage>(
            await root.Mcp.CallAsync(McpCallerTools.QueryExecute, request, token))).Value);
    }
    internal static async Task PageAsync(QueryPage page)
    {
        await SqlRf3Protocol.EqualAsync(Rows(), page.Rows.ToArray());
        await Assert.That(page.Cursor).IsNull();
        await Assert.That(page.AccessPath).IsEqualTo(Scan);
        await Assert.That(page.CutPosition).IsGreaterThan(InvalidCut);
    }
    private static QueryRow[] Rows() => [new(First, Revision, FirstJson, RedactedFields: []),
        new(Second, Revision, SecondJson, RedactedFields: [])];
}
