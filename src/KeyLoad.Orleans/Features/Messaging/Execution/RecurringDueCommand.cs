using KeyLoad.Core.Features.Messaging;

namespace KeyLoad.Orleans;

internal static class RecurringDueCommand
{
    internal static Mutation Create(DueWorkHint hint)
        => hint.Kind switch
        {
            DueWorkKind.Schedule => new EmitRecurringOccurrences(hint.Lane, hint.Id, hint.Generation),
            DueWorkKind.Saga => new ExpireSaga(hint.Lane, hint.Id, hint.Revision),
            _ => throw Errors.Fail(ErrorCode.Validation, DueCoordinatorFields.InvalidHint)
        };
}
