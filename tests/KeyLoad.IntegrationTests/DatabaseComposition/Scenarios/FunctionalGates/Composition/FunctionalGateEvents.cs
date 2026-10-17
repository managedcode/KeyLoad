using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

internal static class FunctionalGateEvents
{
    internal static async Task InitializeAsync(FunctionalGateContext context, CancellationToken token)
    {
        await Assert.That(context.Bind(context.Anchor)).IsEqualTo(context.Bind(new EntityRef(context.Partition, "gate_docs_0", "anchor")));
        await Assert.That(context.ResourceCount).IsEqualTo(FunctionalGateEventsProtocol.ResourceCount);
        for (var resource = 0; resource < FunctionalGateEventsProtocol.ResourceCount; resource++)
        {
            var definition = new ResourceDefinition(FunctionalGateEventsProtocol.Resource(resource),
                ResourceKind.StreamSet, context.Partition.TransactionDomainId);
            var configured = await context.SdkAsync(context.Administrator.Sdk.ConfigureResourceAsync(Guid.NewGuid(),
                new(context.Partition.TenantId, context.Partition.DatabaseId, definition), token));
            await FunctionalGateEventsAssertions.SameAsync(definition, configured);
            var stream = FunctionalGateEventsProtocol.Stream(context, -1, resource);
            var data = FunctionalGateEventsProtocol.Event(context, -1, linked: true);
            var command = FunctionalGateEventsProtocol.Append(context, stream, data, 0);
            var receipt = await context.SdkAsync(context.Administrator.Sdk.CommitAsync(command, token));
            await FunctionalGateEventsAssertions.ReceiptAsync(receipt, command, stream, 1);
            await FunctionalGateEventsAssertions.ReadAsync(context, context.Administrator, stream, [data], receipt, token);
        }
    }

    internal static async Task ExerciseAsync(FunctionalGateContext context, int iteration, int resourceIndex, CancellationToken token)
    {
        await Assert.That(iteration).IsGreaterThanOrEqualTo(0);
        ArgumentOutOfRangeException.ThrowIfNegative(resourceIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(resourceIndex, FunctionalGateEventsProtocol.ResourceCount);
        var resource = resourceIndex;
        var baseline = FunctionalGateEventsProtocol.Stream(context, -1, resource);
        await FunctionalGateEventsAssertions.BaselineAsync(context, baseline, token);
        var stream = FunctionalGateEventsProtocol.Stream(context, iteration, resource);
        var linked = FunctionalGateEventsProtocol.Event(context, iteration, linked: true);
        var unlinked = FunctionalGateEventsProtocol.Event(context, iteration, linked: false);
        await FunctionalGateEventsAssertions.DeniedAsync(context, stream, token);
        var firstCommand = FunctionalGateEventsProtocol.Append(context, stream, linked, 0);
        var first = await context.SdkAsync(context.Writer.Sdk.CommitAsync(firstCommand, token));
        await FunctionalGateEventsAssertions.ReceiptAsync(first, firstCommand, stream, 1);
        var firstReplay = await context.McpAsync<CommitReceipt>(context.Writer,
            McpCallerTools.DocumentsCommit, firstCommand, token);
        await FunctionalGateEventsAssertions.SameAsync(first, firstReplay);
        await FunctionalGateEventsQueries.VerifyAsync(context, stream, iteration, first, token);
        var secondCommand = FunctionalGateEventsProtocol.Append(context, stream, unlinked, 1);
        var second = await context.McpAsync<CommitReceipt>(context.Writer,
            McpCallerTools.DocumentsCommit, secondCommand, token);
        await FunctionalGateEventsAssertions.ReceiptAsync(second, secondCommand, stream, 2);
        await Assert.That(second.Token.Position).IsGreaterThan(first.Token.Position);
        var secondReplay = await context.SdkAsync(context.Writer.Sdk.CommitAsync(secondCommand, token));
        await FunctionalGateEventsAssertions.SameAsync(second, secondReplay);
        await FunctionalGateEventsAssertions.ReadAsync(context, context.Reader, stream, [linked, unlinked], second, token);
        await FunctionalGateEventsQueries.VerifyAsync(context, stream, iteration, second, token);
    }
}
