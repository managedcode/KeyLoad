using System.Text.Json;
using KeyLoad.IntegrationTests.Features.RelationalStorage;
using KeyLoad.IntegrationTests.Features.StorageReplication;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

internal static class FunctionalGateColdRetainedHealthy
{
    private const long AbsentRevision = 0;
    private const long FirstRevision = 1;
    private const string Prefix = "healthy-after-second-cold-";
    private const string Stage = "healthy-after-second-cold";
    private const string PutKind = "putDocument";

    internal static async Task RunAsync(FunctionalGateContext context, EntityRef original,
        Func<FunctionalGateContext, Task> unchanged, CancellationToken token)
    {
        var reference = original with
        {
            Id = Prefix + Guid.NewGuid().ToString(FunctionalGateProtocol.GuidFormat)
        };
        var json = JsonSerializer.Serialize(new
        {
            principal = context.WriterPrincipalId,
            stage = Stage
        }, JsonDefaults.Options);
        var command = new CommandRequest(Guid.NewGuid(), reference.Partition,
            [new PutDocument(reference.Collection, reference.Id, json, AbsentRevision)]);
        var originalReceipt = await context.SdkAsync(context.Writer.Sdk.CommitAsync(command, token));
        await FunctionalGateCompositionCalls.ReceiptAsync(context, originalReceipt, command.CommandId,
            [new(PutKind, reference.Collection, reference.Id, FirstRevision)]);
        var expected = new DocumentReadResult(new(reference, FirstRevision, json, false, []));
        await FunctionalGateColdRetainedRows.ReadAsync(context, expected, token);
        await unchanged(context);
        foreach (var route in Enum.GetValues<DropLifecycleClusterRoute>())
        {
            var replay = await FunctionalGateColdRetainedCalls.CommitAsync(context, command, route, token);
            await SubmissionColdAssertions.ReceiptAsync(replay, originalReceipt);
            await FunctionalGateColdRetainedRows.ReadAsync(context, expected, token);
            await unchanged(context);
        }
    }
}
