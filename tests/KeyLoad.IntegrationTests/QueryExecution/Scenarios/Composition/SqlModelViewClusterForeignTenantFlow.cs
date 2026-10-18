using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class SqlModelViewClusterForeignTenantFlow
{
    internal static async Task RunAsync(KeyLoadClient administrator, KeyLoadClient reader,
        McpOfficialClient mcp, string secret, CancellationToken cancellationToken)
    {
        var foreign = await SqlModelViewClusterScenario.CreateAsync(administrator, cancellationToken);
        var eventsBefore = await McpCallerAssertions.SdkSuccessAsync(await administrator.ReadStreamAsync(
            new(foreign.Stream), cancellationToken));
        var queueBefore = await SqlModelViewClusterParityAssertions.InspectBothAsync(
            administrator, foreign, cancellationToken);
        await LiteralQueueAsync(queueBefore);
        foreach (var request in new[] { foreign.EventSql(), foreign.QueueSql() })
        {
            var before = await McpCallerAssertions.SdkSuccessAsync(await administrator.QueryAsync(
                request, cancellationToken));
            await Assert.That(before.CutPosition).IsGreaterThan(0L);
            if (request == foreign.EventSql())
            {
                await SqlModelViewClusterParityAssertions.AssertEventRowsAsync(eventsBefore.Events, before);
            }
            else
            {
                await SqlModelViewClusterParityAssertions.AssertQueueRowsAsync(queueBefore, before);
            }
            await SqlModelViewClusterDenialAssertions.SdkAsync(await reader.QueryAsync(request, cancellationToken),
                secret, foreignScope: true);
            await SqlModelViewClusterDenialAssertions.McpAsync(await mcp.CallAsync(
                McpCallerTools.QueryExecute, request, cancellationToken), secret, foreignScope: true);
            var after = await McpCallerAssertions.SdkSuccessAsync(await administrator.QueryAsync(
                request, cancellationToken));
            await Assert.That(after.CutPosition).IsGreaterThan(0L);
            await SqlClusterProtocol.EqualAsync(before.Rows, after.Rows);
        }
        var eventsAfter = await McpCallerAssertions.SdkSuccessAsync(await administrator.ReadStreamAsync(
            new(foreign.Stream), cancellationToken));
        await SqlClusterProtocol.EqualAsync(eventsBefore.Events, eventsAfter.Events);
        await SqlClusterProtocol.EqualAsync(eventsBefore.Head, eventsAfter.Head);
        await Assert.That(eventsAfter.Stream).IsEqualTo(foreign.Stream);
        await Assert.That(eventsBefore.HasMore || eventsAfter.HasMore).IsFalse();
        await Assert.That(eventsBefore.CutPosition > 0L && eventsAfter.CutPosition > 0L).IsTrue();
        var queueAfter = await SqlModelViewClusterParityAssertions.InspectBothAsync(
            administrator, foreign, cancellationToken);
        await SqlModelViewClusterParityAssertions.AssertQueueUnchangedAsync(queueBefore.First, queueAfter.First);
        await SqlModelViewClusterParityAssertions.AssertQueueUnchangedAsync(queueBefore.Second, queueAfter.Second);
    }

    private static async Task LiteralQueueAsync((MessageInspection First, MessageInspection Second) queue)
    {
        await Assert.That(queue.First.PayloadJson)
            .IsEqualTo("{\"secret\":\"private-queue-canary\",\"value\":\"created\"}");
        await Assert.That(queue.Second.PayloadJson)
            .IsEqualTo("{\"secret\":\"private-queue-canary\",\"value\":\"paid\"}");
        await Assert.That(queue.First.HeadersJson)
            .IsEqualTo("{\"privateHeader\":\"private-queue-header-canary\",\"source\":\"orders\"}");
        await Assert.That(queue.Second.HeadersJson)
            .IsEqualTo("{\"privateHeader\":\"private-queue-header-canary\",\"source\":\"billing\"}");
    }
}
