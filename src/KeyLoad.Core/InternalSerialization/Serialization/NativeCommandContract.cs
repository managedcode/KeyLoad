namespace KeyLoad.Core.Features.InternalSerialization;

internal static class NativeCommandContract
{
    internal const string Alias = "keyload.core.v1.NativeCommandPayload";
    internal const string InvalidMarker = "The native command failure marker is invalid.";
    internal const string InvalidJson = "The operation contains invalid protocol JSON.";
    internal const string Unsupported = "The operation is unsupported.";
    internal const uint ValueId = 0;
    internal const uint ErrorId = 1;
    internal const uint SafeDetailId = 2;
    internal const uint AuthorityId = 3;
    internal const uint SignatureId = 4;
    internal const uint RetryDecisionsId = 5;
    internal const uint ResourceAdmissionId = 7;
    internal const string MissingAuthority = "The native command authority is missing.";
    internal const string InvalidSignature = "The native command authority signature is invalid.";
    internal const string MismatchedAuthority = "The native command authority does not match its scope or payload.";
}
