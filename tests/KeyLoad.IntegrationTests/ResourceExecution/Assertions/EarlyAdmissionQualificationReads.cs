using System.Text.Json;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.OrleansExecution;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using static KeyLoad.IntegrationTests.Features.ResourceExecution.EarlyAdmissionQualificationProtocol;

namespace KeyLoad.IntegrationTests.Features.ResourceExecution;

internal static class EarlyAdmissionQualificationReads
{
    internal static async Task WaveAsync(RequestCqrsClusterCallers root, RequestCqrsClusterCallers reader,
        EarlyAdmissionQualificationState state, bool malformed, ErrorCode? error, bool held, CancellationToken token)
    {
        var query = new QueryRequest(state.Partition, malformed ? InvalidQuery : EarlyAdmissionQualificationProtocol.Query, AllowFullScan: true);
        var sql = new SqlOperationRequest(state.Partition, query.Sql, AllowFullScan: true);
        foreach (var route in Enum.GetValues<EarlyAdmissionQualificationRoute>())
        {
            await EarlyAdmissionQualificationCurrent.ObserveAsync(root, state, async () =>
            {
                if (route == EarlyAdmissionQualificationRoute.SdkQuery)
                {
                    var result = await reader.Sdk.QueryAsync(query, token);
                    if (error is { } code)
                    {
                        await Assert.That(result.IsFailed).IsTrue();
                        await Assert.That(result.Value).IsNull();
                        await SqlClusterProtocol.EqualAsync(Errors.Problem(code, Detail(code)), result.Problem);
                    }
                    else
                    {
                        await EarlyAdmissionQualificationCurrent.PageAsync(await McpCallerAssertions.SdkSuccessAsync(result));
                    }
                    return;
                }
                if (route == EarlyAdmissionQualificationRoute.SdkSql)
                {
                    var result = await reader.Sdk.ExecuteSqlAsync(sql, token);
                    if (error is { } code)
                    {
                        await Assert.That(result.IsFailed).IsTrue();
                        await Assert.That(result.Value.ValueKind).IsEqualTo(JsonValueKind.Undefined);
                        await SqlClusterProtocol.EqualAsync(Errors.Problem(code, Detail(code)), result.Problem);
                    }
                    else
                    {
                        await EarlyAdmissionQualificationCurrent.PageAsync(
                        (await McpCallerAssertions.SdkSuccessAsync(result)).Deserialize<QueryPage>(JsonDefaults.Options)!);
                    }
                    return;
                }
                var direct = route == EarlyAdmissionQualificationRoute.McpQuery;
                var reply = direct ? await reader.Mcp.CallAsync(McpCallerTools.QueryExecute, query, token)
                    : await reader.Mcp.CallAsync(SqlOperationProtocol.ToolName, sql, token);
                if (error is { } officialCode)
                {
                    await McpCallerAssertions.ErrorAsync(reply, officialCode,
                        dispatched: !held && (direct || !malformed));
                    var safeDetail = officialCode == ErrorCode.ResourceExhausted ? PublicBusyDetail
                        : officialCode == ErrorCode.Validation ? PublicInvalidDetail : ScopeDetail;
                    await Assert.That(JsonElement.DeepEquals(JsonSerializer.SerializeToElement(
                        Errors.Problem(officialCode, safeDetail), JsonDefaults.Options),
                        reply.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error))).IsTrue();
                    await McpCallerAssertions.DoesNotDiscloseAsync(reply, state.Identity.Secret, InvalidQuery);
                }
                else
                {
                    await EarlyAdmissionQualificationCurrent.PageAsync(
                    (await McpCallerAssertions.SuccessAsync<QueryPage>(reply)).Value);
                }
            }, held, token);
        }
    }
    private static string Detail(ErrorCode code) => code == ErrorCode.ResourceExhausted ? BusyDetail
        : code == ErrorCode.Validation ? InvalidDetail : ScopeDetail;
}
