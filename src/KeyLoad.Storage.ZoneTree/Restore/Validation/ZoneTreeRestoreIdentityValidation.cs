using System.Security.Cryptography;
using static KeyLoad.Storage.ZoneTree.ZoneTreePersistenceFormat;

namespace KeyLoad.Storage.ZoneTree;

/// <summary>Restored files receive a distinct incarnation and signing identity before publication.</summary>
internal static class ZoneTreeRestoreIdentityValidation
{
    private const string InvalidIdentity = "Restore requires a distinct new incarnation and signing identity.";

    internal static void RequireFresh(StoreIdentity original, StoreIdentity restored)
    {
        if (restored.Incarnation == Guid.Empty || restored.Incarnation == original.Incarnation
            || restored.SigningKey.Length != SigningKeyBytes
            || CryptographicOperations.FixedTimeEquals(original.SigningKey.Span, restored.SigningKey.Span))
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidIdentity);
        }
    }
}
