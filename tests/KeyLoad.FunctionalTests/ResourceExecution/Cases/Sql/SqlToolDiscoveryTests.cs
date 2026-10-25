using KeyLoad.Server;

namespace KeyLoad.FunctionalTests.ResourceExecution;

/// <summary>AC-AISQL-007: the generic SQL tool reports dynamic effects and cannot call itself.</summary>
internal sealed class SqlToolDiscoveryTests
{
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
