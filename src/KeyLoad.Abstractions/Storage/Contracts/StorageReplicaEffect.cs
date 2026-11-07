namespace KeyLoad.Storage;

/// <summary>An exact replica write whose buffers belong to an immutable publication.</summary>
[Orleans.GenerateSerializer, Orleans.Immutable, Orleans.Alias(StorageReplicaEffect.SerializerAlias)]
public readonly record struct StorageReplicaEffect
{
    internal const string SerializerAlias = "keyload.storage-replica-effect";

    [Orleans.Id(0)] private byte[]? KeyBuffer { get; init; }
    [Orleans.Id(1)] private byte[]? ValueBuffer { get; init; }

    internal ReadOnlyMemory<byte> Key => KeyBuffer;
    internal ReadOnlyMemory<byte>? Value => ValueBuffer?.AsMemory();

    // Only the native staging owner captures and borrows these already-owned buffers.
    internal static StorageReplicaEffect FromOwnedBuffers(byte[] key, byte[]? value)
        => new() { KeyBuffer = key, ValueBuffer = value };

    internal (byte[]? Key, byte[]? Value) BorrowOwnedBuffers() => (KeyBuffer, ValueBuffer);
}
