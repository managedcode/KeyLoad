using System.Diagnostics;
using Microsoft.Extensions.Options;
using ZoneTree;
using ZoneTree.Comparers;
using ZoneTree.Options;
using ZoneTree.Serializers;

namespace KeyLoad.BenchmarkScenarios.Features.BenchmarkComparisons;

internal sealed class ScaledRawStorageZoneTreeEngine : IDisposable
{
    private const string DirectoryPrefix = "keyload-scaled-zonetree-";
    private const string GuidFormat = "N";
    private const int MutableSegmentSlackRecords = 2;
    private const string LengthMessage = "ZoneTree returned a value with an unexpected length.";
    private const string SeedLengthMessage = "The generated seed length differs from the immutable value arena.";
    private const string DeadlineMessage = "The scaled preparation deadline expired.";
    private const string ResidenceMessage = "ZoneTree moved scaled records outside the bounded mutable segment.";
    private readonly ScaledStorageExecutionOptions settings;
    private readonly ScaledRawStorageCorpus _corpus;
    private readonly ScaledRawStorageValueArena _arena;
    private readonly byte[] _readScratch;
    private readonly long _deadlineStart;
    private readonly CancellationToken _token;
    private string? _directory;
    private IZoneTree<Memory<byte>, Memory<byte>>? _tree;

    internal ScaledRawStorageZoneTreeEngine(ScaledRawStorageCorpus corpus, ScaledRawStorageValueArena arena,
        byte[] readScratch, long deadlineStart, CancellationToken token, IOptions<ScaledStorageExecutionOptions> executionOptions)
    {
        settings = executionOptions.Value;
        _corpus = corpus;
        _arena = arena;
        _readScratch = readScratch;
        _deadlineStart = deadlineStart;
        _token = token;
    }

    public void Initialize()
    {
        var directory = Path.Combine(Path.GetTempPath(), DirectoryPrefix + Guid.NewGuid().ToString(GuidFormat));
        _directory = directory;
        CheckPreparation();
        var mutableBound = Math.Max(settings.MinimumMutableSegmentRecords,
            checked(_corpus.RecordCount + MutableSegmentSlackRecords));
        _tree = CreateTree(directory, mutableBound).OpenOrCreate();
    }

    public string? Directory => _directory;

    public bool TryRead(int index, out ReadOnlyMemory<byte> value)
    {
        if (!_tree!.TryGet(_corpus.Key(index), out var stored) || stored.IsEmpty)
        {
            value = ReadOnlyMemory<byte>.Empty;
            return false;
        }

        if (stored.Length != _readScratch.Length)
        {
            throw new InvalidOperationException(LengthMessage);
        }

        stored.Span.CopyTo(_readScratch);
        value = _readScratch.AsMemory();
        GC.KeepAlive(_arena);
        return true;
    }

    public void Upsert(int index, ReadOnlyMemory<byte> value)
    {
        var retainedValue = _arena.Value(index);
        if (value.Length != retainedValue.Length)
        {
            throw new InvalidOperationException(SeedLengthMessage);
        }

        _tree!.Upsert(_corpus.Key(index), retainedValue);
        GC.KeepAlive(_arena);
    }

    public ScaledRawStorageNativeSnapshot Capture()
    {
        const int EmptyReadOnlySegmentsCount = 0;
        const int EmptyReadOnlySegmentsRecordCount = 0;
        const int EmptyDiskRecords = 0;

        var maintenance = _tree!.Maintenance;
        var resident = maintenance.InMemoryRecordCount;
        var diskRecords = maintenance.TotalRecordCount - resident;
        if (resident != _corpus.RecordCount || maintenance.TotalRecordCount != _corpus.RecordCount
            || maintenance.ReadOnlySegmentsCount != EmptyReadOnlySegmentsCount || maintenance.MutableSegmentRecordCount != resident
            || maintenance.ReadOnlySegmentsRecordCount != EmptyReadOnlySegmentsRecordCount || diskRecords != EmptyDiskRecords)
        {
            throw new InvalidOperationException(ResidenceMessage);
        }

        return new ScaledRawStorageNativeSnapshot(resident);
    }

    public void Dispose()
    {
        var failures = new List<Exception>();
        DisposeOwned(failures);
        RawStorageFixtureFailures.Throw(null, failures);
    }

    private static ZoneTreeFactory<Memory<byte>, Memory<byte>> CreateTree(string directory, int mutableBound)
        => new ZoneTreeFactory<Memory<byte>, Memory<byte>>()
            .SetDataDirectory(directory)
            .SetMutableSegmentMaxItemCount(mutableBound)
            .SetComparer(new ScaledMemoryComparer())
            .SetKeySerializer(new ByteArraySerializer())
            .SetValueSerializer(new ByteArraySerializer())
            .SetIsDeletedDelegate(static (in Memory<byte> key, in Memory<byte> value) => value.IsEmpty)
            .SetMarkValueDeletedDelegate(static (ref Memory<byte> value) => value = Memory<byte>.Empty)
            .ConfigureWriteAheadLogOptions(static options => options.WriteAheadLogMode = WriteAheadLogMode.None)
            .ConfigureDiskSegmentOptions(static options => options.CompressionMethod = CompressionMethod.None);

    private void CheckPreparation()
    {
        _token.ThrowIfCancellationRequested();
        if (Stopwatch.GetElapsedTime(_deadlineStart) >= settings.PreparationTimeout)
        {
            throw new TimeoutException(DeadlineMessage);
        }
    }

    private void DisposeOwned(List<Exception> failures)
    {
        if (_tree is not null)
        {
            try
            {
                _tree.Dispose();
                _tree = null;
            }
            catch (Exception failure) when (RawStorageFixtureFailures.IsNonFatal(failure))
            {
                failures.Add(failure);
                return;
            }
        }

        if (_directory is not null && DeleteDirectory(_directory, failures))
        {
            _directory = null;
        }
    }

    private static bool DeleteDirectory(string path, List<Exception> failures)
    {
        if (!System.IO.Directory.Exists(path))
        {
            return true;
        }

        return RawStorageFixtureFailures.Capture(() => System.IO.Directory.Delete(path, recursive: true), failures);
    }

    private sealed class ScaledMemoryComparer : IRefComparer<Memory<byte>>
    {
        public int Compare(in Memory<byte> left, in Memory<byte> right)
            => left.Span.SequenceCompareTo(right.Span);
    }
}
