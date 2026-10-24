namespace KeyLoad.IntegrationTests.DatabaseComposition;

internal static class FunctionalGateColdReferenceSeeds
{
    private const string WitnessId = "lifetime-witness";
    private const string WitnessJson = "{\"kind\":\"lifetime-witness\"}";
    private const long Absent = 0;
    private const long Initial = 1;
    private static readonly string[] Models = ["docs", "vectors", "text"];

    internal static async Task InitializeAsync(FunctionalGateContext context, CancellationToken token)
    {
        foreach (var index in Enumerable.Range(0, FunctionalGateColdProtocol.Resources))
        {
            foreach (var model in Models)
            {
                var collection = FunctionalGateColdProtocol.Resource(model, index);
                var command = new CommandRequest(Guid.NewGuid(), context.Partition,
                    [new PutDocument(collection, WitnessId, WitnessJson, Absent)]);
                var receipt = await context.SdkAsync(context.Administrator.Sdk.CommitAsync(command, token)).ConfigureAwait(false);
                await FunctionalGateRelationalAssertions.ReceiptAsync(context, receipt, command.CommandId, context.Partition,
                    [new("putDocument", collection, WitnessId, Initial)]);
                await FunctionalGateEntityReferences.CaptureAsync(context, context.Administrator,
                    new(new(context.Partition, collection, WitnessId), Initial, WitnessJson, false, []), token).ConfigureAwait(false);
            }
        }
    }
}
