using KeyLoad.ClusterTests.QueryExecution;

namespace KeyLoad.ClusterTests.DatabaseComposition;

internal static class FunctionalGateColdSeed
{
    internal static async Task<FunctionalGateColdState> RunAsync(FunctionalGateContext context,
        FunctionalGateColdState state, CancellationToken token)
    {
        await FunctionalGateColdReferenceSeeds.InitializeAsync(context, token);
        context = context with
        {
            Anchor = context.Bind(context.Anchor)
        };
        var models = new List<FunctionalGateColdModel>();
        for (var index = 0; index < FunctionalGateColdProtocol.Resources; index++)
        {
            var table = FunctionalGateColdProtocol.Resource("table", index);
            var create = new SqlOperationRequest(context.Partition, "CREATE TABLE [" + table
                + "] (id NVARCHAR(MAX) NOT NULL PRIMARY KEY,[value] BIGINT NOT NULL,[reference] NVARCHAR(MAX) NOT NULL)", CommandId: Guid.NewGuid());
            var expected = new ResourceDefinition(table, ResourceKind.Collection, context.Partition.TransactionDomainId)
            {
                Authority = DocumentAuthority.Document,
                RelationalSchema = new(FunctionalGateColdProtocol.IdColumn, [new(FunctionalGateColdProtocol.IdColumn, RelationalColumnType.Text), new("value", RelationalColumnType.WholeNumber),
                    new("reference", RelationalColumnType.Text)])
            };
            var definition = await FunctionalGateRelationalCalls.SqlSdkAsync<ResourceDefinition>(context, context.Administrator, create, token);
            await SqlClusterProtocol.EqualAsync(expected, definition);
            var blob = await FunctionalGateFiles.UploadAsync(context, FunctionalGateColdProtocol.Resource("blobs", index), FunctionalGateColdProtocol.Id, token);
            var beforeWrite = TimeProvider.System.GetUtcNow();
            var command = FunctionalGateColdMutations.Command(context, index, blob.Path);
            var receipt = await context.SdkAsync(context.Writer.Sdk.CommitAsync(command, token));
            await FunctionalGateColdMutations.ReceiptAsync(context, index, command, receipt);
            var reference = context.Bind(new(context.Partition, FunctionalGateColdProtocol.Resource("docs", index), FunctionalGateColdProtocol.Id));
            var insert = FunctionalGateColdProtocol.Insert(context.Partition, index, reference);
            var inserted = await FunctionalGateRelationalCalls.SqlMcpAsync<CommitReceipt>(context, context.Writer, insert, token);
            await FunctionalGateRelationalAssertions.ReceiptAsync(context, inserted, insert.CommandId!.Value, context.Partition,
                [new("putDocument", table, FunctionalGateColdProtocol.Id, 1)]);
            var stream = await context.SdkAsync(context.Reader.Sdk.ReadStreamAsync(new(new(context.Partition,
                FunctionalGateColdProtocol.Resource("events", index), FunctionalGateColdProtocol.Id), Limit: 2), token));
            var source = new EventSourceRef(context.Partition, FunctionalGateColdProtocol.Resource("topic", index), EventSourceKind.Topic);
            var topic = await context.SdkAsync(context.Reader.Sdk.ReadEventSourceAsync(new(source, Limit: 2), token));
            var message = await context.SdkAsync(context.Reader.Sdk.InspectAsync(new(new(context.Partition,
                FunctionalGateColdProtocol.Resource("queue", index)), FunctionalGateColdProtocol.Id), token));
            await FunctionalGateColdReadAssertions.InitialHistoryAsync(context, index, reference, stream, topic, message, receipt.Token.Position, beforeWrite);
            models.Add(new(index, command, receipt, create, definition, insert, inserted, blob,
                stream.Events.Single(), topic.Events.Single(), message));
        }
        var retained = state with
        {
            Anchor = context.Anchor,
            Models = [.. models]
        };
        foreach (var model in retained.Models)
        {
            await FunctionalGateColdReadAssertions.VerifyAsync(context, model, token);
        }
        return retained;
    }
}
