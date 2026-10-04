using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class OrleansWalLegacyBinaryTests
{
    private const ulong LegacyBinaryMagic = 0x324C4157444C4BUL;
    private const ulong LegacyNativeMagic = 0x334C4157444C4BUL;

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
    [Arguments(5, PayloadState.Complete)]
    [Arguments(5, PayloadState.Missing)]
    [Arguments(5, PayloadState.Torn)]
    [Arguments(6, PayloadState.Complete)]
    [Arguments(6, PayloadState.Missing)]
    [Arguments(6, PayloadState.Torn)]
    [Arguments(7, PayloadState.Complete)]
    [Arguments(7, PayloadState.Missing)]
    [Arguments(7, PayloadState.Torn)]
    public async Task AcWal003And004GenericFrameTwoIsRefusedWithoutRewritingOrTruncatingFiles(
        int version, PayloadState state)
    {
        using var files = new WalFileFixture();
        var identity = files.Initialize() with { FormatVersion = version };
        await (version == WalFileFixture.CurrentIdentityVersion
            ? files.WriteIdentityAsync(identity) : files.WriteLegacyJsonIdentityAsync(identity));
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

        var before = await files.CaptureFilesAsync();
        await files.AssertRejectedUnchanged(journal, await File.ReadAllBytesAsync(files.IdentityPath), ErrorCode.FormatUnsupported);
        await files.AssertFilesUnchangedAsync(before);
    }

    [Test]
    [Arguments(PayloadState.Complete)]
    [Arguments(PayloadState.Missing)]
    [Arguments(PayloadState.Torn)]
    public async Task AcIs007NativeFrameThreeRefusesWithoutReinterpretationOnCurrentIdentity(PayloadState state)
    {
        using var files = new WalFileFixture();
        files.Initialize();
        using var serializer = new WalSerializerFixture();
        var payload = serializer.Serialize([new()
        {
            Key = new byte[] { 0x10 }, Value = new byte[] { 0x30, 0x00, 0xFF }, Kind = ZoneTreeJournalMutation.PutKind
        }]);
        var complete = WalFileFixture.CreateFrame(payload, magic: LegacyNativeMagic);
        var journal = state switch
        {
            PayloadState.Complete => complete,
            PayloadState.Missing => complete[..WalFileFixture.HeaderBytes],
            PayloadState.Torn => complete[..^1],
            _ => throw new ArgumentOutOfRangeException(nameof(state))
        };
        await File.WriteAllBytesAsync(files.JournalPath, journal);
        var before = await files.CaptureFilesAsync();
        await files.AssertRejectedUnchanged(journal, await File.ReadAllBytesAsync(files.IdentityPath), ErrorCode.FormatUnsupported);
        await files.AssertFilesUnchangedAsync(before);
    }

    internal enum PayloadState
    {
        Complete,
        Missing,
        Torn
    }
}
