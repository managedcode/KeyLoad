using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using static KeyLoad.Storage.ZoneTree.ZoneTreePersistenceFormat;

namespace KeyLoad.Storage.ZoneTree;

internal static class ZoneTreeCatalogRestoreEntry
{
    private const string InvalidIdentity = "Catalog restore requires a distinct new incarnation and signing identity.";

    internal static StoreIdentity Restore(string backup, string destination,
        IOptions<ZoneTreeStorageExecutionOptions> executionOptions,
        Action<IAtomicTransaction, StoreIdentity, long, ReadOnlyMemory<byte>> verifyCatalog,
        Guid newIncarnation, byte[] newSigningKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(verifyCatalog);
        ArgumentNullException.ThrowIfNull(newSigningKey);
        if (newIncarnation == Guid.Empty || newSigningKey.Length != SigningKeyBytes)
        { throw Errors.Fail(ErrorCode.Validation, InvalidIdentity); }
        return ZoneTreeBackupRestoreRestore.Restore(backup, destination, executionOptions,
            newIncarnation, newSigningKey, verifyCatalog, cancellationToken);
    }

    internal static void RequireFreshIdentity(StoreIdentity original, Guid? incarnation, byte[]? signingKey)
    {
        if (incarnation is null || incarnation == Guid.Empty || incarnation == original.Incarnation
            || signingKey is null || signingKey.Length != SigningKeyBytes
            || CryptographicOperations.FixedTimeEquals(original.SigningKey.Span, signingKey))
        { throw Errors.Fail(ErrorCode.Validation, InvalidIdentity); }
    }
}
