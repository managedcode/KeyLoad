using System.Collections.Immutable;
using System.Text.Json;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using P = KeyLoad.IntegrationTests.Features.DatabaseComposition.FunctionalGateCompositionProtocol;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

internal static class FunctionalGateCompositionCalls
{
    private const string IdentifierParameter = "id";

    internal static async Task<T> SqlAsync<T>(FunctionalGateContext context, SqlOperationRequest request,
        bool official, bool write, CancellationToken token) where T : class
    {
        if (official)
        {
            return await context.McpAsync<T>(write ? context.Writer : context.Reader,
                SqlOperationProtocol.ToolName, request, token);
        }
        var caller = write ? context.Writer : context.Reader;
        var actual = await context.SdkAsync(caller.Sdk.ExecuteSqlAsync(request, token));
        return actual.Deserialize<T>(JsonDefaults.Options) ?? throw new InvalidOperationException();
    }

    internal static Task<CommitReceipt> CommandAsync(FunctionalGateContext context, CommandRequest command,
        bool official, CancellationToken token)
        => SqlAsync<CommitReceipt>(context, SqlClusterProtocol.Call(context.Partition,
            McpCallerTools.DocumentsCommit, command) with
        {
            CommandId = command.CommandId
        }, official, true, token);

    internal static async Task ReceiptAsync(FunctionalGateContext context, CommitReceipt actual,
        Guid commandId, ImmutableArray<MutationReceipt> effects)
    {
        await Assert.That(actual.Token.AtomicPartitionId).IsEqualTo(context.Partition.AtomicPartitionId);
        await Assert.That(actual.Token.Position).IsGreaterThan(P.Absent);
        await NativeSubmissionColdAssertions.ReceiptAsync(actual,
            new(commandId, actual.Token, effects, DurabilityProfile.ProcessDurable, context.ReplicaCount));
    }

    internal static SqlOperationRequest Read(FunctionalGateContext context, string collection, string id)
        => new(context.Partition, "SELECT * FROM [" + collection + "] WHERE id = @id",
            new(StringComparer.Ordinal)
            {
                [IdentifierParameter] = JsonSerializer.SerializeToElement(id)
            });
}
