using ZoneTree;
using ZoneTree.Comparers;
using ZoneTree.Options;
using ZoneTree.Serializers;

namespace KeyLoad.BenchmarkScenarios.Features.BenchmarkComparisons;

internal sealed class RawStorageFixtureZoneTreeEngine : IRawStorageFixtureEngine
{
    private const string TemporaryDirectoryPrefix = "keyload-raw-storage-";
    private const string GuidFormat = "N";
    private const string UnexpectedLengthMessage = "ZoneTree returned a value with an unexpected byte length.";
    private IZoneTree<Memory<byte>, Memory<byte>>? tree;
    private string? directory;
    private readonly RawStorageCorpus corpus;
    private readonly byte[] readScratch;

    internal RawStorageFixtureZoneTreeEngine(RawStorageCorpus corpus, byte[] readScratch)
    {
        this.corpus = corpus;
        this.readScratch = readScratch;
        directory = Path.Combine(Path.GetTempPath(), TemporaryDirectoryPrefix + Guid.NewGuid().ToString(GuidFormat));
        try
        {
            tree = CreateTree(directory).OpenOrCreate();
        }
        catch (Exception primary)
        {
            var failures = new List<Exception>();
            DisposeOwnedResources(failures);
            RawStorageFixtureFailures.Throw(primary, failures);
            throw;
        }
    }

    public string? Directory => directory;

    public bool TryRead(int index, out ReadOnlyMemory<byte> value)
    {
        if (!tree!.TryGet(corpus.Key(index), out var stored) || stored.IsEmpty)
        {
            value = ReadOnlyMemory<byte>.Empty;
            return false;
        }

        if (stored.Length != readScratch.Length)
        {
            throw new InvalidOperationException(UnexpectedLengthMessage);
        }

        stored.Span.CopyTo(readScratch);
        value = readScratch.AsMemory(0, stored.Length);
        return true;
    }

    public void Upsert(int index, bool alternate)
        => tree!.Upsert(corpus.Key(index), corpus.Value(index, alternate));

    public bool Delete(int index) => tree!.TryDelete(corpus.Key(index), out _);

    public void Dispose()
    {
        var failures = new List<Exception>();
        DisposeOwnedResources(failures);
        RawStorageFixtureFailures.Throw(null, failures);
    }

    private static ZoneTreeFactory<Memory<byte>, Memory<byte>> CreateTree(string dataDirectory)
        => new ZoneTreeFactory<Memory<byte>, Memory<byte>>()
            .SetDataDirectory(dataDirectory)
            .SetComparer(new RawStorageMemoryComparer())
            .SetKeySerializer(new ByteArraySerializer())
            .SetValueSerializer(new ByteArraySerializer())
            .SetIsDeletedDelegate(static (in Memory<byte> key, in Memory<byte> value) => value.IsEmpty)
            .SetMarkValueDeletedDelegate(static (ref Memory<byte> value) => value = Memory<byte>.Empty)
            .ConfigureWriteAheadLogOptions(static options => options.WriteAheadLogMode = WriteAheadLogMode.None)
            .ConfigureDiskSegmentOptions(static options => options.CompressionMethod = CompressionMethod.None);

    private void DisposeOwnedResources(List<Exception> failures)
    {
        if (tree is not null)
        {
            try
            {
                tree.Dispose();
                tree = null;
            }
            catch (Exception failure) when (RawStorageFixtureFailures.IsNonFatal(failure))
            {
                failures.Add(failure);
                return;
            }
        }

        if (directory is not null && !System.IO.Directory.Exists(directory))
        {
            directory = null;
        }
        else if (directory is not null
            && RawStorageFixtureFailures.Capture(() => System.IO.Directory.Delete(directory, recursive: true), failures))
        {
            directory = null;
        }
    }

    private sealed class RawStorageMemoryComparer : IRefComparer<Memory<byte>>
    {
        public int Compare(in Memory<byte> left, in Memory<byte> right) => left.Span.SequenceCompareTo(right.Span);
    }
}
