using KeyLoad.Core;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

/// <summary>AC-AISQL-007: generic SQL uses the bounded heavy data lane and preserves direct control capacity.</summary>
internal sealed class SqlAdmissionTests
{
    private const string Root = "root";
    private const string Tenant = "tenant";
    private const string QueryRoute = "/v1/query";
    private const string DeliveryRoute = "/v1/queues/delivery";
    private const int BodyBytes = 128;
    private static readonly PrincipalRecord Principal = new(Root, Tenant, [], []);

    [Test]
    public async Task AcAiSql007SqlAndQueryReserveTheSameWorkingBudgetBeforePrincipalBinding()
    {
        var governor = new HttpAdmissionGovernor();
        long queryBytes;
        using (var query = governor.Begin(QueryRoute, BodyBytes))
        {
            queryBytes = governor.Status().Node.RetainedBytes;
        }
        using (var sql = governor.Begin(SqlOperationProtocol.Route, BodyBytes))
        {
            await Assert.That(governor.Status().Node.RetainedBytes).IsEqualTo(queryBytes);
            await Assert.That(governor.Status().VerifiedScopes.Commands).IsEqualTo(0);
            sql.Bind(Principal);
            await Assert.That(governor.Status().VerifiedScopes.Commands).IsEqualTo(1);
            await Assert.That(governor.Status().Node.ControlCommands).IsEqualTo(0);
        }
        await Assert.That(governor.Status().Node.RetainedBytes).IsEqualTo(0);
        await Assert.That(governor.Status().VerifiedScopes.Commands).IsEqualTo(0);
    }

    [Test]
    public async Task AcAiSql007SqlDataSaturationCannotConsumeDirectControlProgress()
    {
        var governor = new HttpAdmissionGovernor(new() { MaxRequests = 1, MaxPrincipalRequests = 1 });
        using var sql = governor.Begin(SqlOperationProtocol.Route, BodyBytes);
        sql.Bind(Principal);
        var rejected = Assert.ThrowsExactly<KeyLoadException>(() => governor.Begin(SqlOperationProtocol.Route, BodyBytes));
        await Assert.That(rejected.Code).IsEqualTo(ErrorCode.ResourceExhausted);
        using var control = governor.Begin(DeliveryRoute, BodyBytes);
        control.Bind(Principal);
        await Assert.That(governor.Status().Node.ControlCommands).IsEqualTo(1);
        await Assert.That(governor.Status().VerifiedScopes.ControlCommands).IsEqualTo(1);
    }

    [Test]
    public async Task AcAiSql007SqlDiscoveryReportsDynamicEffectsAndCannotBeCalledRecursively()
    {
        await Assert.That(McpOperationCatalog.TryGetTool(SqlOperationProtocol.ToolName, out var sql)).IsTrue();
        await Assert.That(sql!.IsAdapter).IsTrue();
        await Assert.That(sql.ReadKind).IsNull();
        await Assert.That(sql.CommandKind).IsNull();
        await Assert.That(sql.ReadOnly || sql.Idempotent).IsFalse();
        await Assert.That(sql.Destructive).IsTrue();
        await Assert.That(McpOperationCatalog.TryGet(SqlOperationProtocol.ToolName, out _)).IsFalse();
    }
}
