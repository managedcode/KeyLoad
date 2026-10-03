using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.InternalSerialization;

internal sealed class NativeCheckpointBatchTests
{
    private const string Prefix = "keyload-native-checkpoint-";
    private const string GuidFormat = "N";
    private const string KeyA = "a";
    private const string KeyB = "b";
    private const long Position = 1;

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    public async Task EntireNativeBatchIsValidatedBeforeFirstApply(int invalidCase)
    {
        var path = Path.Combine(Path.GetTempPath(), Prefix + Guid.NewGuid().ToString(GuidFormat));
        var options = new ZoneTreeStoreOptions(Path.GetTempPath());
        var applied = 0;
        try
        {
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
            var metadata = new ZoneTreeCheckpointMetadata(ZoneTreePersistenceFormat.CheckpointVersion,
                KeyCodec.Version, Guid.NewGuid(), Position, 0);
            ZoneTreeCheckpointFrame.Write(stream, options, ZoneTreePersistenceFormat.CheckpointMagic,
                Position, NativeSerialization.Serialize(metadata), null);
            StorageMutation first = new(KeyCodec.Encode(KeyA), new byte[] { 1 });
            StorageMutation? second = invalidCase switch
            {
                0 => null,
                1 => new(ReadOnlyMemory<byte>.Empty, new byte[] { 2 }),
                2 => new(KeyCodec.Encode(KeyB), null),
                _ => new(KeyCodec.Encode(KeyA), new byte[] { 2 })
            };
            var records = new[] { first, second! };
            ZoneTreeCheckpointFrame.Write(stream, options, ZoneTreePersistenceFormat.CheckpointDataMagic,
                Position, NativeSerialization.Serialize(records), null);
            stream.Position = 0;
            var failure = Assert.ThrowsExactly<KeyLoadException>(() =>
                ZoneTreeCheckpointReader.Read(stream, options, _ => applied++));
            await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
            await Assert.That(applied).IsEqualTo(0);
        }
        finally
        { File.Delete(path); }
    }
}
