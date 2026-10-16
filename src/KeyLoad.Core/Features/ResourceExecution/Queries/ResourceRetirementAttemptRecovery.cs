using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal ResourceRetirementAttemptV1 ReadResourceRetirementAttempt(string principalId,
        ResourceRetirementAttemptContextV1 context, Guid attemptId, ReadExecutionBudget work)
    {
        work.Check();
        var result = Store.Read(raw =>
        {
            var view = work.CreateView(raw);
            RequireInventoryAdministrator(view, principalId);
            ResourceRetirementAttemptContextValidation.Require(context, Limits);
            if (context.Purpose is ResourceRetirementAttemptPurpose.SealBarrier or ResourceRetirementAttemptPurpose.CleanupPage)
            {
                var state = ResourceRetirementStorage.ReadControl(view, context.Retirement!.Lifetime, Limits)
                    ?? throw Errors.Fail(context.Purpose == ResourceRetirementAttemptPurpose.CleanupPage
                        ? ErrorCode.RecoveryRequired : ErrorCode.NotFound, ResourceRetirementProtocol.Missing);
                RequireSameRetirement(state.Control, context.Retirement!);
                RequireRetirementBeginOutcome(view, state.Control);
            }
            else
            {
                _ = RequirePreparationSource(view, context.Preparation!);
            }
            var current = ResourceRetirementAttemptStorage.Current(view, context, Limits);
            var attempt = (context.Purpose == ResourceRetirementAttemptPurpose.CleanupPage && attemptId == Guid.Empty
                ? current : ResourceRetirementAttemptStorage.Read(view, context, attemptId, Limits))
                ?? throw Errors.Fail(ErrorCode.NotFound, ResourceRetirementProtocol.Missing);
            if (current is null || attempt.Ordinal > current.Ordinal
                || attempt.Ordinal == current.Ordinal && !ResourceRetirementValidation.Same(current, attempt))
            {
                throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
            }
            return attempt;
        });
        work.CheckResult(result);
        work.Check();
        return result;
    }
}
