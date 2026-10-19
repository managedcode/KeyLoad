using KeyLoad.Server.Features.Search;
using Microsoft.Extensions.Options;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class TextFileStreamBufferPolicyTests
{
    [Test]
    [Arguments(1, null, 1L)]
    [Arguments(4, null, 0L)]
    [Arguments(4, 0, 1L)]
    [Arguments(4, 1, 1L)]
    [Arguments(4, 4_096, 0L)]
    public async Task ConfiguredCapPreservesUnbufferedAndSmallerCallerRequests(
        int configuredBuffer, int? requestedBuffer, long expectedUnflushedLength)
    {
        using var database = new TestDatabase();
        var generation = TextOwnershipFixture.CreateGeneration(database,
            TestContext.Current!.Execution.CancellationToken);
        var root = Directory.GetParent(generation)!.FullName;
        var execution = Path.Combine(generation, TextProtocol.RuntimeDirectoryCore);
        var options = UnitTextOptions.Execution(new TextExecutionOptions { FileBufferBytes = configuredBuffer });
        var provider = new TextFileStreamProvider(root, Path.GetFileName(generation),
            database.Store.Identity.NodeId, options);
        var path = Path.Combine(execution, "buffer-policy.bin");

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
        var options = Options.Create(new TextExecutionOptions { FileBufferBytes = configuredBuffer });

        var failure = Assert.ThrowsExactly<OptionsValidationException>(() =>
            _ = new TextFileStreamProvider(database.Directory, "unused-generation", database.Store.Identity.NodeId, options));

        await Assert.That(failure.OptionsType).IsEqualTo(typeof(TextExecutionOptions));
        await Assert.That(Directory.EnumerateFiles(database.Directory, "*", SearchOption.AllDirectories).Count())
            .IsEqualTo(filesBefore);
    }

    private static long WriteAndObserveUnflushedLength(TextFileStreamProvider provider,
        string path, int? requestedBuffer)
    {
        using var file = requestedBuffer is { } size
            ? provider.CreateFileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read, size)
            : provider.CreateFileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        file.ToStream().WriteByte(0x42);
        return new FileInfo(path).Length;
    }
}
