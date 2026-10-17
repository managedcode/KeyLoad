using System.Collections.Immutable;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const int EventVectorFirstResultVersion = 1;
    private const string InvalidEventVectorFirstAction = "The original event vector first issuance action is inconsistent.";

    private OperationResult ExecuteEventVectorFirstIssuance(IAtomicTransaction transaction,
        PrincipalRecord principal, EventVectorControlPhase phase, ReplicatedOperation operation,
        long position, AtomicPartitionPlacementResolution placement)
    {
        var row = phase.Action switch
        {
            EventVectorControlAction.AdmitFirstIntent => EventVectorFirstIntentAdmission.Execute(
                transaction, principal, phase, operation, Authorization, OperationLimitsOptions),
            EventVectorControlAction.CaptureFirstPhase or EventVectorControlAction.ReserveFirstDispatch
                => EventVectorFirstIssuanceTransitions.Execute(transaction, principal, phase,
                    Authorization, OperationLimitsOptions),
            EventVectorControlAction.AbandonFirstIssuance or EventVectorControlAction.CleanupAbandonedIssuance
                => EventVectorFirstIssuanceDisposition.Execute(transaction, principal, phase,
                    operation, Authorization, OperationLimitsOptions),
            _ => throw Errors.Fail(ErrorCode.Corruption, InvalidEventVectorFirstAction)
        };
        var receipt = new CommitReceipt(operation.Id,
            new CommitToken(placement.Incarnation, placement.Partition.AtomicPartitionId,
                position, placement.PlacementEpoch), ImmutableArray<MutationReceipt>.Empty, Durability);
        var result = new EventVectorFirstIssuanceResult(EventVectorFirstResultVersion, operation.Id, row, receipt);
        return Result(EventVectorFirstCleanupPublicResult.Retain(transaction, principal, phase, operation,
            result, new EventVectorAdmissionPolicy(OperationLimitsOptions)));
    }
}

