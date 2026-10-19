using System.Text.Json;
using ManagedCode.Communication;
using ModelContextProtocol.Protocol;

namespace KeyLoad.IntegrationTests.Features.ClientApi;

internal static class PublicReadAssertions
{
    internal static Task<DocumentResult?> DocumentAsync(Result<DocumentReadResult> result)
        => SdkAsync(result, response => response.Document);

    internal static Task<McpCallReceipt<DocumentResult?>> DocumentAsync(CallToolResult reply)
        => OfficialAsync<DocumentReadResult, DocumentResult>(reply, "document", response => response.Document);

    internal static Task<MessageInspection?> MessageAsync(Result<MessageInspectionResult> result)
        => SdkAsync(result, response => response.Message);

    internal static Task<McpCallReceipt<MessageInspection?>> MessageAsync(CallToolResult reply)
        => OfficialAsync<MessageInspectionResult, MessageInspection>(reply, "message", response => response.Message);

    internal static Task<QueueTransferInspection?> TransferAsync(Result<QueueTransferReadResult> result)
        => SdkAsync(result, response => response.Transfer);

    internal static Task<McpCallReceipt<QueueTransferInspection?>> TransferAsync(CallToolResult reply)
        => OfficialAsync<QueueTransferReadResult, QueueTransferInspection>(reply, "transfer", response => response.Transfer);

    internal static Task<QueueTransferReceiptInspection?> ReceiptAsync(Result<QueueTransferReceiptReadResult> result)
        => SdkAsync(result, response => response.Receipt);

    internal static Task<McpCallReceipt<QueueTransferReceiptInspection?>> ReceiptAsync(CallToolResult reply)
        => OfficialAsync<QueueTransferReceiptReadResult, QueueTransferReceiptInspection>(reply, "receipt", response => response.Receipt);

    internal static Task<RecurringScheduleInspection?> ScheduleAsync(Result<RecurringScheduleReadResult> result)
        => SdkAsync(result, response => response.Schedule);

    internal static Task<McpCallReceipt<RecurringScheduleInspection?>> ScheduleAsync(CallToolResult reply)
        => OfficialAsync<RecurringScheduleReadResult, RecurringScheduleInspection>(reply, "schedule", response => response.Schedule);

    internal static Task<SagaInspection?> SagaAsync(Result<SagaReadResult> result)
        => SdkAsync(result, response => response.Saga);

    internal static Task<McpCallReceipt<SagaInspection?>> SagaAsync(CallToolResult reply)
        => OfficialAsync<SagaReadResult, SagaInspection>(reply, "saga", response => response.Saga);

    internal static Task<McpCallReceipt<DocumentResult>> RequiredDocumentAsync(CallToolResult reply)
        => RequireAsync(DocumentAsync(reply));

    internal static Task<McpCallReceipt<MessageInspection>> RequiredMessageAsync(CallToolResult reply)
        => RequireAsync(MessageAsync(reply));

    internal static Task<McpCallReceipt<QueueTransferInspection>> RequiredTransferAsync(CallToolResult reply)
        => RequireAsync(TransferAsync(reply));

    internal static Task<McpCallReceipt<QueueTransferReceiptInspection>> RequiredReceiptAsync(CallToolResult reply)
        => RequireAsync(ReceiptAsync(reply));

    internal static Task<McpCallReceipt<RecurringScheduleInspection>> RequiredScheduleAsync(CallToolResult reply)
        => RequireAsync(ScheduleAsync(reply));

    internal static Task<McpCallReceipt<SagaInspection>> RequiredSagaAsync(CallToolResult reply)
        => RequireAsync(SagaAsync(reply));

    private static async Task<McpCallReceipt<T>> RequireAsync<T>(Task<McpCallReceipt<T?>> operation) where T : class
    {
        var actual = await operation.ConfigureAwait(false);
        await Assert.That(actual.Value).IsNotNull();
        return new(actual.Value!, actual.RequestId);
    }

    private static async Task<TDomain?> SdkAsync<TResponse, TDomain>(Result<TResponse> result,
        Func<TResponse, TDomain?> select) where TResponse : class where TDomain : class
    {
        var response = await McpCallerAssertions.SdkSuccessAsync(result);
        await Assert.That(response).IsNotNull();
        return select(response);
    }

    private static async Task<McpCallReceipt<TDomain?>> OfficialAsync<TResponse, TDomain>(CallToolResult reply,
        string field, Func<TResponse, TDomain?> select) where TResponse : class where TDomain : class
    {
        var actual = await McpCallerAssertions.SuccessAsync<TResponse>(reply);
        await Assert.That(actual.Value).IsNotNull();
        var json = reply.StructuredContent!.Value.GetProperty(McpCallerProtocol.Result);
        await Assert.That(json.ValueKind).IsEqualTo(JsonValueKind.Object);
        await Assert.That(json.EnumerateObject().Select(property => property.Name))
            .IsEquivalentTo(new[] { field });
        return new(select(actual.Value), actual.RequestId);
    }
}
