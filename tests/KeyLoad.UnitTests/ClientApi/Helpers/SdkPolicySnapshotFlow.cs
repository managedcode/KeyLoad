using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.ClientApi;

internal static class SdkPolicySnapshotFlow
{
    private const string ApiKey = "sdk-retained-policy-test-key";
    private const string Tenant = "sdk-policy-tenant";
    private const string Database = "sdk-policy-database";
    private const string Domain = "sdk-policy-domain";
    private const string PartitionKey = "sdk-policy-partition";
    private const string Select = "SELECT 1";
    private const string NestedSelect = "/* /* */ */ SELECT 1";
    private const string SafeDetail = "retained-policy-probe";
    private const string NodeId = "retained-policy-healthy";
    private const string ContentType = "application/json";
    private const string OwnershipLost = "OwnershipLost";
    private const string PermissionDenied = "PermissionDenied";
    private const string Cancelled = "Cancelled";
    private const string UnknownWriteOutcome = "UnknownWriteOutcome";
    private const string ServerUnavailable = "The server response is unavailable.";
    private const string ReadUnavailable = "The read response is unavailable.";
    private const string WriteUnavailable = "The write response is unavailable. Retry the same command ID.";

    internal static async Task RunAsync(List<Exception> failures, CancellationToken token)
    {
        var calls = 0;
        var healthy = new NodeStatus(NodeId, Guid.Empty, 1L, NodeId, 3,
            DurabilityProfile.QuorumProcessDurable, true, Environment.ProcessId);
        await using var server = await KeyLoadClientKestrelServer.StartAsync(async context =>
        {
            var count = Interlocked.Increment(ref calls);
            context.Response.ContentType = ContentType;
            if (count == 3)
            {
                await JsonSerializer.SerializeAsync(context.Response.Body, healthy, JsonDefaults.Options,
                    context.RequestAborted);
                return;
            }
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await JsonSerializer.SerializeAsync(context.Response.Body,
                Errors.Problem(ErrorCode.PermissionDenied, SafeDetail), JsonDefaults.Options, context.RequestAborted);
        });
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var configured = new KeyLoadClientExecutionOptions
            {
                MaximumProblemBodyBytes = 1,
                MaximumSqlInspectionBytes = 512,
                MaximumSqlInspectionDepth = 2,
                SqlBudgetCheckInterval = 32
            };
            var options = Options.Create(configured);
            var retained = new KeyLoadClient(server.Client, ApiKey, options);
            configured.MaximumProblemBodyBytes = 65_536;
            await SdkPolicySnapshotAssertions.ProblemAsync(await retained.StatusAsync(token),
                OwnershipLost, 503, ServerUnavailable);
            var expanded = new KeyLoadClient(server.Client, ApiKey, options);
            await SdkPolicySnapshotAssertions.ProblemAsync(await expanded.StatusAsync(token),
                PermissionDenied, 403, SafeDetail);
            using var cancellation = new CancellationTokenSource();
            await cancellation.CancelAsync();
            configured.MaximumSqlInspectionBytes = 1;
            await SqlPairAsync(retained, new KeyLoadClient(server.Client, ApiKey, options), Select, cancellation.Token);
            configured.MaximumSqlInspectionBytes = 512;
            configured.MaximumSqlInspectionDepth = 1;
            await SqlPairAsync(retained, new KeyLoadClient(server.Client, ApiKey, options), NestedSelect, cancellation.Token);
            configured.MaximumSqlInspectionDepth = 2;
            configured.SqlBudgetCheckInterval = 1;
            await SqlPairAsync(retained, new KeyLoadClient(server.Client, ApiKey, options), Select, cancellation.Token);
            configured.SqlBudgetCheckInterval = 32;
            configured.MaximumProblemBodyBytes = 0;
            var invalid = Assert.ThrowsExactly<OptionsValidationException>(
                () => _ = new KeyLoadClient(server.Client, ApiKey, options));
            await Assert.That(invalid.OptionsType).IsEqualTo(typeof(KeyLoadClientExecutionOptions));
            await Assert.That(invalid.OptionsName).IsEqualTo(KeyLoadClientExecutionOptions.SectionName);
            await Assert.That(invalid.Failures.Single()).IsEqualTo(KeyLoadClientExecutionOptions.ValidationMessage);
            await Assert.That(calls).IsEqualTo(2);
            configured.MaximumProblemBodyBytes = 65_536;
            var result = await retained.StatusAsync(token);
            await Assert.That(result.IsSuccess).IsTrue();
            await SdkPolicySnapshotAssertions.HealthyAsync(result.Value!, healthy);
            await Assert.That(calls).IsEqualTo(3);
        }, failures);
    }

    private static async Task SqlPairAsync(KeyLoadClient retained, KeyLoadClient changed, string sql,
        CancellationToken cancellation)
    {
        var request = new SqlOperationRequest(new(Tenant, Database, Domain, PartitionKey), sql);
        await SdkPolicySnapshotAssertions.ProblemAsync(await retained.ExecuteSqlAsync(request, cancellation),
            Cancelled, 400, ReadUnavailable);
        await SdkPolicySnapshotAssertions.ProblemAsync(await changed.ExecuteSqlAsync(request, cancellation),
            UnknownWriteOutcome, 503, WriteUnavailable);
    }
}
