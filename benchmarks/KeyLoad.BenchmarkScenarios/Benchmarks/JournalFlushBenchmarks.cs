using System.Security.Cryptography;
using BenchmarkDotNet.Attributes;

namespace KeyLoad.BenchmarkScenarios.BenchmarkComparisons;

/// <summary>Measures one durable journal-sized append, the floor under every acknowledged single-command commit.</summary>
/// <remarks>A local development measurement: each operation appends one frame-sized record and flushes it to the
/// device, once on a write-through stream as the store journal opens it and once on a buffered stream, both with
/// the store's configured file buffer.</remarks>
public class JournalFlushBenchmarks : IDisposable
{
    private const int FrameBytes = 1024;
    private const string DirectoryPrefix = "keyload-flush-";
    private const string GuidFormat = "N";
    private const string WriteThroughName = "write-through.journal";
    private const string BufferedName = "buffered.journal";
    private const string ActiveSetupMessage = "The flush benchmark fixture already has an active setup.";
    private const string InactiveSetupMessage = "The flush benchmark fixture is not initialized.";

    private readonly byte[] frame = new byte[FrameBytes];
    private readonly string directory = Path.Combine(AppContext.BaseDirectory,
        DirectoryPrefix + Guid.NewGuid().ToString(GuidFormat));
    private FileStream? writeThrough;
    private FileStream? buffered;
    private bool disposed;

    /// <summary>Creates the two private journal files.</summary>
    [GlobalSetup]
    public void Setup()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (writeThrough is not null || buffered is not null)
        {
            throw new InvalidOperationException(ActiveSetupMessage);
        }

        var bufferBytes = EmbeddedBenchmarkRuntimeRegistration.Read().Storage.Value.FileBufferBytes;
        Directory.CreateDirectory(directory);
        RandomNumberGenerator.Fill(frame);
        writeThrough = new(Path.Combine(directory, WriteThroughName), FileMode.CreateNew, FileAccess.ReadWrite,
            FileShare.Read, bufferBytes, FileOptions.WriteThrough);
        buffered = new(Path.Combine(directory, BufferedName), FileMode.CreateNew, FileAccess.ReadWrite,
            FileShare.Read, bufferBytes, FileOptions.None);
    }

    /// <summary>Appends one frame to the write-through stream and flushes it to the device.</summary>
    [Benchmark(Baseline = true)]
    public void WriteThroughAppendAndFlush() => AppendAndFlush(writeThrough);

    /// <summary>Appends one frame to the buffered stream and flushes it to the device.</summary>
    [Benchmark]
    public void BufferedAppendAndFlush() => AppendAndFlush(buffered);

    /// <summary>Releases the fixture's files and directory.</summary>
    [GlobalCleanup]
    public void Cleanup() => Dispose();

    /// <summary>Releases fixture resources; repeated disposal is harmless.</summary>
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Releases the owned resources when disposal is requested.</summary>
    protected virtual void Dispose(bool disposing)
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        if (!disposing)
        {
            return;
        }

        try
        {
            writeThrough?.Dispose();
            buffered?.Dispose();
        }
        finally
        {
            writeThrough = null;
            buffered = null;
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private void AppendAndFlush(FileStream? stream)
    {
        var journal = stream ?? throw new InvalidOperationException(InactiveSetupMessage);
        journal.Write(frame);
        journal.Flush(flushToDisk: true);
    }
}
