using System.Text;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal sealed class RestorePublicationOccupiedRegion(string target) : IAsyncDisposable
{
    private const string FileName = "original-destination-reservation.txt";
    private const string Contents = "independently-owned-destination";
    private const long FileStart = 0;
    private readonly Lock sync = new();
    private FileStream? owned;
    private Task? closing;
    private bool created;
    private bool released;

    internal void OccupyAfterReconciliation()
    {
        Directory.CreateDirectory(target);
        owned = new FileStream(Path.Combine(target, FileName), FileMode.CreateNew,
            FileAccess.ReadWrite, FileShare.None);
        created = true;
        owned.Write(Encoding.UTF8.GetBytes(Contents));
        owned.Flush(true);
    }

    internal async Task RequireRetainedAsync(CancellationToken token)
    {
        await Assert.That(created).IsTrue();
        var original = owned ?? throw new InvalidOperationException(nameof(RestorePublicationOccupiedRegion));
        var expected = Encoding.UTF8.GetBytes(Contents);
        var actual = new byte[expected.Length];
        original.Position = FileStart;
        await original.ReadExactlyAsync(actual, token).ConfigureAwait(false);
        await Assert.That(original.Length).IsEqualTo((long)expected.Length);
        await Assert.That(actual.AsSpan().SequenceEqual(expected)).IsTrue();
        await Assert.That(Directory.EnumerateFileSystemEntries(target).Order(StringComparer.Ordinal).ToArray())
            .IsEquivalentTo(new[] { Path.Combine(target, FileName) }, CollectionOrdering.Matching);
    }

    internal async Task RepairAsync()
    {
        await DisposeAsync().ConfigureAwait(false);
        if (created && released)
        { File.Delete(Path.Combine(target, FileName)); }
    }

    public ValueTask DisposeAsync()
    {
        lock (sync)
        {
            closing ??= CloseOriginalAsync();
            return new ValueTask(closing);
        }
    }

    private async Task CloseOriginalAsync()
    {
        if (owned is not null)
        { await owned.DisposeAsync().ConfigureAwait(false); }
        owned = null;
        released = true;
    }
}
