using KeyLoad.Server;
using KeyLoad.UnitTests.Features.ClusterRouting;
using Microsoft.Extensions.Options;
namespace KeyLoad.UnitTests.Features.RelationalStorage;

internal static class SqlTypedInsertOwnershipEpochCommands
{
    internal static ReplicatedOperation Compile(ControlledPartitionMovementNode target, Guid id,
        long epoch, bool native, CancellationToken token)
    {
        var limits = Options.Create(target.Database.Limits);
        var query = UnitExecutionOptions.QueryExecution();
        McpDecodedOperation decoded;
        if (native)
        {
            var control = SqlOperationCompiler.CompileNativeSessionPlan(new(ControlledPartitionMovementCorpus.Partition,
                SqlTypedInsertOwnershipEpochLiteral.Arm(id, epoch)), limits, limits.Value.MaxBatchBytes, query,
                SqlTypedInsertOwnershipEpochLiteral.NoParameters, token);
            decoded = SqlOperationCompiler.CompileNativeSessionPlan(new(ControlledPartitionMovementCorpus.Partition,
                SqlTypedInsertOwnershipEpochLiteral.InsertSql, CommandId: control.ContextCommandId,
                OwnershipEpoch: control.ContextOwnershipEpoch), limits, limits.Value.MaxBatchBytes, query,
                SqlTypedInsertOwnershipEpochLiteral.NoParameters, token).Operation
                ?? throw new InvalidOperationException(SqlTypedInsertOwnershipEpochLiteral.MissingOperation);
        }
        else
        {
            decoded = SqlOperationCompiler.Compile(new(ControlledPartitionMovementCorpus.Partition,
                SqlTypedInsertOwnershipEpochLiteral.InsertSql, CommandId: id, OwnershipEpoch: epoch), limits,
                limits.Value.MaxBatchBytes, query, token);
        }
        return target.Database.CreateNativeOperation(OperationKind.Batch, decoded.CommandId, SqlTypedInsertRetiredLiteral.Writer,
            target.Database.EvaluationClock.GetUtcNow(), decoded.Payload);
    }
}
