using ManagedCode.Communication;
using ManagedCode.Communication.Commands;
using KeyLoad.Core.Features.Search;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private Result<object> ExecuteBm25CorpusPublication(IAtomicTransaction transaction,
        PrincipalRecord administrator, Command<DatabaseCommand> operation, long position)
    {
        var phase = Payload<Bm25CorpusPublicationPhaseCommand>(operation);
        RequireEnvelopeId(operation, phase.CommandId);
        var principal = RequireBm25Subject(transaction, administrator, phase, operation.Timestamp);
        var current = phase.Current;
        if (operation.Timestamp >= phase.OriginalExpiry)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, Bm25CorpusProtocol.Changed);
        }
        var resource = Resource(transaction, current.Partition, current.Collection, ResourceKind.Collection);
        var placement = ReadPlacementWitness(transaction, current.Partition);
        if (!Bm25CorpusDependencies.Matches(this, transaction, principal, resource, current,
            placement, Limits.MaxBatchBytes))
        {
            throw Errors.Fail(ErrorCode.Conflict, Bm25CorpusProtocol.Changed);
        }
        var key = Bm25CorpusKeys.Current(current);
        var previous = transaction.GetRecord<Bm25CorpusCurrentV1>(key);
        if (previous?.Generation != phase.ExpectedGeneration)
        {
            throw Errors.Fail(ErrorCode.Conflict, Bm25CorpusProtocol.Changed);
        }
        Bm25CorpusFamilyAdmission.Require(transaction, key, current, Limits);
        transaction.PutRecord(key, current);
        return Result(new CommitReceipt(operation.CommandId, Token(transaction, current.Partition, position), [], Durability, AcknowledgedPersistedCopies));
    }
}
