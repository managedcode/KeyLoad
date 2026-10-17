namespace KeyLoad.Server;

internal static class NativeSignedPurposeProtocol
{
    internal const string ArmFile = "purpose-arm.json";
    internal const string IssuedFile = "purpose-issued.json";
    internal const string RejectedFile = "purpose-rejected.json";
    internal const string Issued = "original-issued";
    internal const string Rejected = "mac-decoded-purpose-rejected";
    internal const string InvalidPurpose = "keyload-protected-unsupported-request-purpose";
    internal const int DigestHexCharacters = System.Security.Cryptography.SHA256.HashSizeInBytes * 2;
    internal const string Invalid = "NativeSignedPurposeInvalid";
}
