using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class OrleansWalLegacyBinaryTests
{
    private const ulong LegacyBinaryMagic = 0x324C4157444C4BUL;

    [Test]
    [Arguments(1, PayloadState.Complete)]
    [Arguments(2, PayloadState.Complete)]
    [Arguments(3, PayloadState.Complete)]
    [Arguments(4, PayloadState.Complete)]
    [Arguments(1, PayloadState.Missing)]
    [Arguments(2, PayloadState.Missing)]
    [Arguments(3, PayloadState.Missing)]
    [Arguments(4, PayloadState.Missing)]
    [Arguments(1, PayloadState.Torn)]
    [Arguments(2, PayloadState.Torn)]
    [Arguments(3, PayloadState.Torn)]
    [Arguments(4, PayloadState.Torn)]
    public async Task AcWal003And004GenericFrameTwoIsRefusedWithoutRewritingOrTruncatingFiles(
        int version, PayloadState state)
    {
        using var files = new WalFileFixture();
        await files.WriteIdentityAsync(files.Initialize() with { FormatVersion = version });
        using var legacySerializer = new WalSerializerFixture(useNativeByteCodec: false);
        var payload = legacySerializer.Serialize([new()
        {
            Key = new byte[] { 0x10 },
            Value = new byte[] { 0x30, 0x00, 0xFF },
            Kind = ZoneTreeJournalMutation.PutKind
        }]);
        var complete = WalFileFixture.CreateFrame(payload, magic: LegacyBinaryMagic);
        var journal = state switch
        {
            PayloadState.Complete => complete,
            PayloadState.Missing => complete[..WalFileFixture.HeaderBytes],
            PayloadState.Torn => complete[..^1],
            _ => throw new ArgumentOutOfRangeException(nameof(state))
        };
        await File.WriteAllBytesAsync(files.JournalPath, journal);

        await files.AssertRejectedUnchanged(journal, await File.ReadAllBytesAsync(files.IdentityPath), ErrorCode.FormatUnsupported);
    }

    internal enum PayloadState
    {
        Complete,
        Missing,
        Torn
    }
}
