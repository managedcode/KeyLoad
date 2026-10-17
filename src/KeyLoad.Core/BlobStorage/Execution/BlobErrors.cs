namespace KeyLoad.Core.Features.BlobStorage;

internal static class BlobErrors
{
    internal const string Invalid = "The blob request or policy is invalid.";
    internal const string Damaged = "The persisted blob records are inconsistent.";
    internal const string Unsupported = "The blob record format is unsupported.";
    internal const string Fenced = "Offline blob restore normalization must complete.";
    internal const string Denied = "The persisted principal cannot access this blob.";
    internal const string Lifetime = "The selected upload lifetime is unavailable.";
    internal const string Revision = "The blob head revision changed.";
    internal const string Capacity = "The blob reservation quota is exhausted.";
    internal const string Conflict = "The blob upload conflicts with its current state.";
    internal const string Missing = "The published blob is unavailable.";
    internal const string CatalogProofBudget = "The initial blob catalog proof exceeds its bounded work limit.";
    internal const string RestoreWorkBudget = "The blob restore record exceeds the configured page byte budget.";
    internal static KeyLoadException Corruption() => Errors.Fail(ErrorCode.Corruption, Damaged);
    internal static KeyLoadException Validation() => Errors.Fail(ErrorCode.Validation, Invalid);
    internal static KeyLoadException Token() => Errors.Fail(ErrorCode.TokenInvalidated, Lifetime);
    internal static KeyLoadException StateConflict() => Errors.Fail(ErrorCode.Conflict, Conflict);
}
