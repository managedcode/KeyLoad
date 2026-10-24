using System.Security.Cryptography;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.BackupRestore;

internal sealed class CurrentJournalSignatureAuthority : IDisposable
{
    private const byte SigningByte = 11;
    internal Guid Incarnation { get; } = Guid.NewGuid();
    internal byte[] Key { get; } = Enumerable.Repeat(SigningByte, ZoneTreePersistenceFormat.SigningKeyBytes).ToArray();

    internal StoreIdentity Restore(string backup, string target)
        => ZoneTreeStore.Restore(backup, target, UnitExecutionOptions.StorageExecution(), Incarnation, Key);

    internal async Task RequireAsync(StoreIdentity identity, StoreIdentity source)
    {
        await Assert.That(identity.NodeId).IsNotEqualTo(source.NodeId);
        await Assert.That(identity.Incarnation).IsEqualTo(Incarnation);
        await Assert.That(identity.SigningKey.Span.SequenceEqual(Key)).IsTrue();
        await Assert.That(identity.Durability).IsEqualTo(source.Durability);
        await Assert.That(identity.FormatVersion).IsEqualTo(source.FormatVersion);
        await Assert.That(identity.DispatchPaused).IsTrue();
    }

    public void Dispose() => CryptographicOperations.ZeroMemory(Key);
}
