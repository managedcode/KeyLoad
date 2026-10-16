using System.Text.Json;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Exercises the original acknowledged token through both real SQL CALL adapters across movement and cold owners.</summary>
internal static class CurrentOwnershipTokenSqlRf3Assertions
{
    internal static async Task ReadAsync(PartitionMovementPublicParentRf3Seed seed, EntityRef reference,
        CommitToken minimum, DocumentResult expected, CancellationToken token)
    {
        var call = SqlRf3Protocol.Call(reference.Partition, McpCallerTools.DocumentsGet,
            new GetDocumentRequest(reference, minimum));
        foreach (var sdk in new[] { seed.Source, seed.Target })
        {
            var actual = await SqlRf3Protocol.SdkAsync<DocumentReadResult>(sdk, call, token);
            await SqlRf3Protocol.EqualAsync(expected, actual.Document);
        }
        var official = await SqlRf3Protocol.McpAsync<DocumentReadResult>(seed.Official, call, token);
        await SqlRf3Protocol.EqualAsync(expected, official.Document);
    }

    internal static async Task RefusedAsync<T>(PartitionMovementPublicParentRf3Seed seed, string operation,
        T request, string sdkDetail, string officialDetail, CancellationToken token)
    {
        var call = SqlRf3Protocol.Call(seed.Partition, operation, request);
        foreach (var sdk in new[] { seed.Source, seed.Target })
        {
            var actual = await sdk.ExecuteSqlAsync(call, token);
            await Assert.That(actual.IsSuccess).IsFalse();
            await Assert.That(actual.Value.ValueKind).IsEqualTo(JsonValueKind.Undefined);
            await PartitionMovementPublicRefusalRf3Oracle.SdkAsync(actual.Problem,
                ErrorCode.TokenInvalidated, sdkDetail);
        }
        var official = await seed.Official.CallAsync(SqlOperationProtocol.ToolName, call, token);
        await PartitionMovementPublicRefusalRf3Oracle.McpAsync(official,
            ErrorCode.TokenInvalidated, officialDetail);
    }

    internal static async Task WaitAsync(PartitionMovementPublicParentRf3Seed seed, WaitForIndexRequest request,
        WaitForIndexResult expected, CancellationToken token)
    {
        var call = SqlRf3Protocol.Call(request.Partition, WaitForIndexProtocol.Tool, request);
        foreach (var sdk in new[] { seed.Source, seed.Target })
        {
            var actual = await SqlRf3Protocol.SdkAsync<WaitForIndexResult>(sdk, call, token);
            await CurrentOwnershipTokenAfterMovementRf3ReadAssertions.WaitAsync(actual,
                request.MinimumToken, expected.SchemaVersion);
            await Assert.That(actual.PolicyEpoch).IsEqualTo(expected.PolicyEpoch);
        }
        var official = await SqlRf3Protocol.McpAsync<WaitForIndexResult>(seed.Official, call, token);
        await CurrentOwnershipTokenAfterMovementRf3ReadAssertions.WaitAsync(official,
            request.MinimumToken, expected.SchemaVersion);
        await Assert.That(official.PolicyEpoch).IsEqualTo(expected.PolicyEpoch);
    }
}
