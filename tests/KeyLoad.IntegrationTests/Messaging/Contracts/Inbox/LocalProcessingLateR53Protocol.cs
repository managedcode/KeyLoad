namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class LocalProcessingLateR53Protocol
{
    internal const string Message = "late-prefix-work";
    internal const string Document = "late-prefix-result";
    internal const string PutKind = "putDocument";
    internal const string EnqueueKind = "enqueue";
    internal const string AckKind = "Ack";
    internal const string RevisionDetail = "The expected revision does not match.";
    internal const string EffectDetail = "The inbox input was already completed with different effects.";
    internal const string ChangedDetail = "The principal policy changed since this command was evaluated.";
    internal const string SafeGeneric = "The database operation could not be completed.";
    internal const string SafePermission = "The authenticated principal is not permitted to perform this operation.";
    internal const string Missing = "The original local processing snapshot is unavailable.";
}
