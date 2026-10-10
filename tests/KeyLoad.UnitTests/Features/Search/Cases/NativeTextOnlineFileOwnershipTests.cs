using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class NativeTextOnlineFileOwnershipTests
{
    [Test]
    public async Task GenuineUnlinkedHandleRetainsDiskAdmissionUntilJoinedCloseThenFullHealthyWriteAndRead()
    {
        using var database = new TestDatabase();
        var (provider, heldPath, healthyPath) = NativeTextOpenFileOwnershipFixture.Create(database);
        using var held = provider.CreateFileStream(heldPath, FileMode.Open, FileAccess.ReadWrite,
            FileShare.ReadWrite | FileShare.Delete);
        held.ToStream().Write(NativeTextOpenFileOwnershipFixture.Held);
        NativeTextOpenFileOwnershipFixture.FlushDurably(held);
        provider.DeleteFile(heldPath);
        await Assert.That(File.Exists(heldPath)).IsFalse();
        using var healthy = provider.CreateFileStream(healthyPath, FileMode.Open, FileAccess.ReadWrite,
            FileShare.ReadWrite | FileShare.Delete);
        var refused = Assert.ThrowsExactly<KeyLoadException>(() =>
            healthy.ToStream().Write(NativeTextOpenFileOwnershipFixture.Healthy));
        await Assert.That(refused.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(healthy.Length).IsEqualTo(0L);
        var refusedBytes = await File.ReadAllBytesAsync(healthyPath, TestContext.Current!.Execution.CancellationToken);
        await Assert.That(refusedBytes).IsEmpty();
        held.Dispose();
        await healthy.WriteAsync(NativeTextOpenFileOwnershipFixture.Healthy, TestContext.Current!.Execution.CancellationToken);
        NativeTextOpenFileOwnershipFixture.FlushDurably(healthy);
        healthy.Position = 0;
        var complete = new byte[NativeTextOpenFileOwnershipFixture.Healthy.Length];
        healthy.ToStream().ReadExactly(complete);
        await Assert.That(complete).IsEquivalentTo(NativeTextOpenFileOwnershipFixture.Healthy, CollectionOrdering.Matching);
        var persisted = await File.ReadAllBytesAsync(healthyPath, TestContext.Current!.Execution.CancellationToken);
        await Assert.That(persisted).IsEquivalentTo(NativeTextOpenFileOwnershipFixture.Healthy, CollectionOrdering.Matching);
    }

    [Test]
    public async Task OriginalCancelledWriteAndAsyncJoinedClosePreserveBytesThenFreshHandleReadsCompleteHealthyFile()
    {
        using var database = new TestDatabase();
        var token = TestContext.Current!.Execution.CancellationToken;
        var (provider, heldPath, healthyPath) = NativeTextOpenFileOwnershipFixture.Create(database);
        await using var held = provider.CreateFileStream(heldPath, FileMode.Open, FileAccess.ReadWrite,
            FileShare.ReadWrite | FileShare.Delete);
        held.ToStream().Write(NativeTextOpenFileOwnershipFixture.Held);
        NativeTextOpenFileOwnershipFixture.FlushDurably(held);
        provider.DeleteFile(heldPath);
        await using var healthy = provider.CreateFileStream(healthyPath, FileMode.Open, FileAccess.ReadWrite,
            FileShare.ReadWrite | FileShare.Delete);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await healthy.WriteAsync(NativeTextOpenFileOwnershipFixture.Healthy, cancelled.Token));
        await Assert.That(healthy.Length).IsEqualTo(0L);
        await Assert.That(await File.ReadAllBytesAsync(healthyPath, token)).IsEmpty();
        await held.DisposeAsync();
        await held.DisposeAsync();
        Assert.ThrowsExactly<ObjectDisposedException>(() => held.ToStream().WriteByte(0x61));
        await healthy.WriteAsync(NativeTextOpenFileOwnershipFixture.Healthy, token);
        NativeTextOpenFileOwnershipFixture.FlushDurably(healthy);
        await healthy.DisposeAsync();
        await using var reopened = provider.CreateFileStream(healthyPath, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        var complete = new byte[NativeTextOpenFileOwnershipFixture.Healthy.Length];
        reopened.ToStream().ReadExactly(complete);
        await Assert.That(complete).IsEquivalentTo(NativeTextOpenFileOwnershipFixture.Healthy,
            CollectionOrdering.Matching);
        await Assert.That(await File.ReadAllBytesAsync(healthyPath, token))
            .IsEquivalentTo(NativeTextOpenFileOwnershipFixture.Healthy, CollectionOrdering.Matching);
    }

    [Test]
    public async Task RealExclusiveOpenFailurePreservesOriginalHandleAndQuotaUntilJoinedCloseThenHealthyFile()
    {
        using var database = new TestDatabase();
        var token = TestContext.Current!.Execution.CancellationToken;
        var (provider, heldPath, healthyPath) = NativeTextOpenFileOwnershipFixture.Create(database);
        using var held = provider.CreateFileStream(heldPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        held.ToStream().Write(NativeTextOpenFileOwnershipFixture.Held);
        NativeTextOpenFileOwnershipFixture.FlushDurably(held);
        Assert.Throws<IOException>(() =>
        {
            using var conflicting = provider.CreateFileStream(heldPath, FileMode.Open,
                FileAccess.ReadWrite, FileShare.None);
        });
        held.Position = 0;
        var preserved = new byte[NativeTextOpenFileOwnershipFixture.Held.Length];
        held.ToStream().ReadExactly(preserved);
        await Assert.That(preserved).IsEquivalentTo(NativeTextOpenFileOwnershipFixture.Held,
            CollectionOrdering.Matching);
        await using var healthy = provider.CreateFileStream(healthyPath, FileMode.Open, FileAccess.ReadWrite,
            FileShare.ReadWrite | FileShare.Delete);
        var denied = Assert.ThrowsExactly<KeyLoadException>(() =>
            healthy.ToStream().Write(NativeTextOpenFileOwnershipFixture.Healthy));
        await Assert.That(denied.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(await File.ReadAllBytesAsync(healthyPath, token)).IsEmpty();
        held.Dispose();
        provider.DeleteFile(heldPath);
        await healthy.WriteAsync(NativeTextOpenFileOwnershipFixture.Healthy, token);
        NativeTextOpenFileOwnershipFixture.FlushDurably(healthy);
        await healthy.DisposeAsync();
        await Assert.That(await File.ReadAllBytesAsync(healthyPath, token))
            .IsEquivalentTo(NativeTextOpenFileOwnershipFixture.Healthy, CollectionOrdering.Matching);
    }
}
