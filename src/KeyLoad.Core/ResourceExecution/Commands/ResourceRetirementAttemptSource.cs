using System.Collections.Immutable;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private void RequireRetirementAttemptSource(IKeyValueView view, ResourceRetirementAttemptContextV1 context)
    {
        if (context.Purpose == ResourceRetirementAttemptPurpose.CleanupPage)
        {
            RequireCleanupRetirementAttemptSource(view, context);
            return;
        }
        if (context.Purpose == ResourceRetirementAttemptPurpose.SealBarrier)
        {
            var state = RequireRetirementSource(view, context.Retirement!);
            ResourceRetirementStateValidation.AllOwners(state);
            if (state.Stage != ResourceRetirementStage.Retiring)
            {
                throw Errors.Fail(ErrorCode.Conflict, ResourceRetirementProtocol.Changed);
            }
            return;
        }
        var preparation = RequirePreparationSource(view, context.Preparation!);
        if (preparation.BegunControl is not null || preparation.Reservations.Any(reservation =>
            ResourceRetirementValidation.Same(reservation.Receiver, context.Receiver)))
        {
            throw Errors.Fail(ErrorCode.Conflict, ResourceRetirementProtocol.Changed);
        }
    }

    private ResourceRetirementPhaseResultV1 WriteRetirementAttemptPointer(IAtomicTransaction transaction,
        ResourceRetirementAttemptContextV1 context, ResourceRetirementAttemptV1 attempt,
        ResourceRetirementPhaseReceiptV1 receipt)
    {
        if (context.Purpose == ResourceRetirementAttemptPurpose.CleanupPage)
        {
            ResourceRetirementAttemptStorage.WriteCleanupCurrent(transaction, context, attempt, Limits);
            return new(receipt, null, null)
            {
                CleanupAttempt = attempt
            };
        }
        if (context.Purpose == ResourceRetirementAttemptPurpose.SealBarrier)
        {
            var state = ResourceRetirementStorage.ReadControl(transaction, context.Retirement!.Lifetime, Limits)!
                with
            {
                SealAttempt = attempt
            };
            ResourceRetirementStorage.WriteState(transaction, state, Limits);
            return new(receipt, state, null);
        }
        var preparation = ResourceRetirementPreparationStorage.ReadPreparation(transaction, context.Preparation!.Lifetime, Limits)!;
        preparation = preparation with
        {
            OwnerAttempts = preparation.OwnerAttempts
            .Where(previous => !ResourceRetirementAttemptPointers.Belongs(previous, preparation.Control, context.Receiver!))
            .Append(attempt).ToImmutableArray()
        };
        var result = new ResourceRetirementPhaseResultV1(receipt, null, null) { PreparationState = preparation };
        ResourceRetirementValidation.Bound(result, Limits);
        ResourceRetirementPreparationStorage.WriteState(transaction, preparation, Limits);
        return result;
    }
}
