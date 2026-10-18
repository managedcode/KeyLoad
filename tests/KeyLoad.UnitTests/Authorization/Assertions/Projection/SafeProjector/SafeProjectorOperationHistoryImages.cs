using ManagedCode.Communication.Commands;
using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.Authorization;

internal static class SafeProjectorOperationHistoryImages
{
    private const string InvalidLocator = "The original safe projector locator differs from its canonical scope.";
    private const string Missing = "The original safe projector history bytes are absent.";
    private const string Unsupported = "The safe projector history contains an unsupported original operation.";

    internal static SafeProjectorOperationOutcomeImage Capture(SafeProjectorOperationDatabase owner, Command<DatabaseCommand> operation)
    {
        var global = Global(operation);
        var key = global ? KeySpace.GlobalOutcome(operation.UserId!, operation.CommandId)
            : KeySpace.PartitionOutcome(owner.Source.Partition, operation.UserId!, operation.CommandId);
        return owner.Store.Read(view =>
        {
            var outcome = view.ReadOwnedValue(key) ?? throw new InvalidOperationException(Missing);
            var locator = view.ReadOwnedValue(KeySpace.OutcomeLocatorV2(
                owner.Source.Partition, operation.UserId!, operation.CommandId));
            var validLocator = global ? locator is null : locator is not null && locator.AsSpan().SequenceEqual(key);
            if (!validLocator)
            {
                throw new InvalidOperationException(InvalidLocator);
            }
            return new SafeProjectorOperationOutcomeImage(outcome, locator);
        });
    }

    internal static async Task UnchangedAsync(SafeProjectorOperationDatabase owner, SafeProjectorOperationCommand command)
    {
        var actual = Capture(owner, command.Operation);
        await Assert.That(actual.Outcome.AsSpan().SequenceEqual(command.OriginalOutcome.Outcome)).IsTrue();
        var expectedLocator = command.OriginalOutcome.Locator;
        await Assert.That(actual.Locator is null ? expectedLocator is null
            : expectedLocator is not null && actual.Locator.AsSpan().SequenceEqual(expectedLocator)).IsTrue();
    }

    private static bool Global(Command<DatabaseCommand> operation) => operation.Value!.Kind switch
    {
        OperationKind.ConfigureResource or OperationKind.ConfigurePrincipal => true,
        OperationKind.Batch => false,
        _ => throw new InvalidOperationException(Unsupported)
    };
}
