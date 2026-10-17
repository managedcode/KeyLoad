using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class PostgresErrorReceiverAssertions
{
    internal static async Task TerminalAsync(PostgresErrorReceiverControl control, Guid arm,
        PostgresErrorDeliveryWitness buffered, CancellationToken token)
    {
        await global::TUnit.Assertions.Assert.That(buffered.PrimaryFirst).IsFalse();
        await global::TUnit.Assertions.Assert.That(buffered.Failures).IsEqualTo(PostgresRf3Protocol.FirstColumn);
        var receiver = await control.WaitAsync(McpCallerProtocol.Node3, arm, PostgresErrorDeliveryPhase.ReceiverClosed, token);
        await global::TUnit.Assertions.Assert.That(receiver.Connection).IsEqualTo(buffered.Connection);
        await global::TUnit.Assertions.Assert.That(receiver.PrimaryFirst).IsFalse();
        await global::TUnit.Assertions.Assert.That(receiver.Failures).IsEqualTo(PostgresRf3Protocol.FirstColumn);
        foreach (var phase in new[] { PostgresErrorDeliveryPhase.LedgerJoined,
            PostgresErrorDeliveryPhase.SessionJoined, PostgresErrorDeliveryPhase.ConnectionJoined })
        {
            var actual = await control.WaitAsync(McpCallerProtocol.Node3, arm, phase, token);
            var expected = new PostgresErrorDeliveryWitness(control.Session, arm, buffered.Connection,
                phase, true, ErrorCode.PermissionDenied, PostgresErrorReceiverProtocol.ExpectedFailures);
            await SqlRf3Protocol.EqualAsync(expected, actual);
        }
    }
}
