using KeyLoad.Server.Features.Search;
using Microsoft.Extensions.Options;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class NativeTextFileStreamBufferPolicyTests
{
    [Test]
    [Arguments(1, null, 1L)]
    [Arguments(4, null, 0L)]
    [Arguments(4, 0, 1L)]
    [Arguments(4, 1, 1L)]
    [Arguments(4, 4_096, 0L)]
    public async Task ConfiguredCapPreservesNativeUnbufferedAndSmallerCallerRequests(
        int configuredBuffer, int? requestedBuffer, long expectedUnflushedLength)
    {
        using var database = new TestDatabase();
        var generation = NativeTextOwnershipFixture.CreateGeneration(database,
            TestContext.Current!.Execution.CancellationToken);
        var root = Directory.GetParent(generation)!.FullName;
        var native = Path.Combine(generation, NativeTextProtocol.NativeDirectory);
        var options = UnitNativeTextOptions.Execution(new NativeTextExecutionOptions { FileBufferBytes = configuredBuffer });
        var provider = new NativeTextFileStreamProvider(root, Path.GetFileName(generation),
            database.Store.Identity.NodeId, options);
        var path = Path.Combine(native, "buffer-policy.bin");

        var unflushedLength = WriteAndObserveUnflushedLength(provider, path, requestedBuffer);

        await Assert.That(unflushedLength).IsEqualTo(expectedUnflushedLength);
        var actual = await File.ReadAllBytesAsync(path, TestContext.Current!.Execution.CancellationToken);
        await Assert.That(actual).IsEquivalentTo(new byte[] { 0x42 }, CollectionOrdering.Matching);
    }

    [Test]
    [Arguments(0)]
    [Arguments(4_097)]
    public async Task InvalidConfiguredBufferIsRejectedBeforeAnyFileOwnership(int configuredBuffer)
    {
        using var database = new TestDatabase();
        var filesBefore = Directory.EnumerateFiles(database.Directory, "*", SearchOption.AllDirectories).Count();
        var options = Options.Create(new NativeTextExecutionOptions { FileBufferBytes = configuredBuffer });

        var failure = Assert.ThrowsExactly<OptionsValidationException>(() =>
            _ = new NativeTextFileStreamProvider(database.Directory, "unused-generation", database.Store.Identity.NodeId, options));

        await Assert.That(failure.OptionsType).IsEqualTo(typeof(NativeTextExecutionOptions));
        await Assert.That(Directory.EnumerateFiles(database.Directory, "*", SearchOption.AllDirectories).Count())
            .IsEqualTo(filesBefore);
    }

    private static long WriteAndObserveUnflushedLength(NativeTextFileStreamProvider provider,
        string path, int? requestedBuffer)
    {
        using var file = requestedBuffer is { } size
            ? provider.CreateFileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read, size)
            : provider.CreateFileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        file.ToStream().WriteByte(0x42);
        return new FileInfo(path).Length;
    }
}
