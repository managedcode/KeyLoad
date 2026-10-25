using System.Text.Json;
using KeyLoad.Server;
using KeyLoad.UnitTests.GraphTraversal;
using KeyLoad.UnitTests.RelationalStorage;
using ManagedCode.Communication;
using P = KeyLoad.UnitTests.QueryExecution.ScriptDomOrdinalParameterProtocol;

namespace KeyLoad.UnitTests.QueryExecution;

internal static class ScriptDomOrdinalWriteAdmission
{
    internal static Task RunAsync(TestDatabase fixture, SqlTypedInsertState state, CancellationToken token)
        => GraphLabelUseOperations.ObserveAsync(fixture.Store, async () =>
        {
            var values = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                [SqlTypedInsertLiteral.ActiveColumn.ToUpperInvariant()] = JsonSerializer.SerializeToElement(true)
            };
            var before = JsonDefaults.Serialize(values);
            var limits = UnitExecutionOptions.DatabaseLimits();
            McpDecodedOperation? partial = null;
            Result<object>? outcome = null;
            var failure = Assert.ThrowsExactly<KeyLoadException>(() =>
            {
                partial = SqlOperationCompiler.Compile(new(fixture.Partition, SqlTypedInsertLiteral.Sql, values,
                    CommandId: state.Created.Command.CommandId), UnitMcpOptions.CanonicalPayloadBytes,
                    UnitExecutionOptions.QueryExecution(), token);
                outcome = SqlTypedInsertLiteral.Apply(fixture.Database, partial, state.Created.At);
            });
            await Assert.That(partial).IsNull();
            await Assert.That(outcome).IsNull();
            await Assert.That(failure.Code).IsEqualTo(ErrorCode.Validation);
            await Assert.That(failure.Message).IsEqualTo(P.InvalidArguments);
            await Assert.That(JsonDefaults.Serialize(values).AsSpan().SequenceEqual(before)).IsTrue();
            var exact = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                [SqlTypedInsertLiteral.ActiveColumn] = JsonSerializer.SerializeToElement(true)
            };
            var repaired = SqlOperationCompiler.Compile(new(fixture.Partition, SqlTypedInsertLiteral.Sql, exact,
                CommandId: state.Created.Command.CommandId), UnitMcpOptions.CanonicalPayloadBytes,
                UnitExecutionOptions.QueryExecution(), token);
            await SqlTypedInsertAssertions.SameAsync(state.Created.Result,
                SqlTypedInsertLiteral.Apply(fixture.Database, repaired, state.Created.At));
            await SqlTypedInsertHot.ReplayAsync(fixture, state.Created);
            await SqlTypedInsertAssertions.ImageAsync(fixture, fixture.Store, fixture.Database, repaired: false);
        });
}
