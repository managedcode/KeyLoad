using KeyLoad.Core.Search;
using KeyLoad.Storage;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Core;
/// <summary>Executes SearchCommands operations against the caller's database context.</summary>
public static class SearchCommands
{
    internal static Result<object> ExecuteBm25CorpusPublication(this DatabaseEngine engine, IAtomicTransaction transaction, PrincipalRecord administrator, Command<DatabaseCommand> operation, long position)
    {
        var phase = OperationProtocol.Payload<Bm25CorpusPublicationPhaseCommand>(operation);
        OperationProtocol.RequireEnvelopeId(operation, phase.CommandId);
        var principal = engine.RequireBm25Subject(transaction, administrator, phase, operation.Timestamp);
        var current = phase.Current;
        if (operation.Timestamp >= phase.OriginalExpiry)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, Bm25CorpusProtocol.Changed);
        }

        var resource = engine.Resource(transaction, current.Partition, current.Collection, ResourceKind.Collection);
        var placement = engine.ReadPlacementWitness(transaction, current.Partition);
        if (!Bm25CorpusDependencies.Matches(engine, transaction, principal, resource, current, placement, engine.Limits.MaxBatchBytes))
        {
            throw Errors.Fail(ErrorCode.Conflict, Bm25CorpusProtocol.Changed);
        }

        var key = Bm25CorpusKeys.Current(current);
        var previous = transaction.GetRecord<Bm25CorpusCurrent>(key);
        if (previous?.Generation != phase.ExpectedGeneration)
        {
            throw Errors.Fail(ErrorCode.Conflict, Bm25CorpusProtocol.Changed);
        }

        Bm25CorpusFamilyAdmission.Require(transaction, key, current, engine.Limits);
        transaction.PutRecord(key, current);
        return OperationProtocol.Result(new CommitReceipt(operation.CommandId, engine.Token(transaction, current.Partition, position), [], engine.Durability, engine.AcknowledgedPersistedCopies));
    }
}
