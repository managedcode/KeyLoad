namespace KeyLoad.Server.Features.DocumentStorage;

internal static class ControlledDocumentFailureProtocol
{
    internal const string MissingDurableOutcome = "The applied command has no durable outcome.";
    internal const string OriginalBudgetFailure = "KeyLoad.ControlledDocument.OriginalBudgetFailure";
    internal const string OriginalOutcomeFailure = "KeyLoad.ControlledDocument.OriginalOutcomeFailure";
}
