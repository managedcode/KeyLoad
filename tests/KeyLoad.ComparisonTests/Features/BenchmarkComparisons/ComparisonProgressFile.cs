using System.Text;
using System.Threading.Channels;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>Serializes one latest marker to a small atomic file independently of test output capture.</summary>
internal sealed class ComparisonProgressFile
{
    private const string PendingSuffix = ".pending";
    private const string LinkedPath = "The progress path contains a symbolic link.";
    private const int BufferBytes = 512;
    private static readonly TimeSpan WriteDeadline = TimeSpan.FromSeconds(5);
    private readonly Channel<string> latest = Channel.CreateBounded<string>(new BoundedChannelOptions(1)
    {
        SingleReader = true,
        SingleWriter = false,
        FullMode = BoundedChannelFullMode.DropOldest,
        AllowSynchronousContinuations = false
    });
    private readonly string path;
    private readonly Task writer;
    private int writeFailures;

    internal ComparisonProgressFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        this.path = path;
        writer = Task.Run(WriteLatestAsync, CancellationToken.None);
    }

    internal bool HasWriteFailure => Volatile.Read(ref writeFailures) != 0;

    internal void Observe(string line)
    {
        if (ComparisonProgressLine.IsValid(line))
        {
            latest.Writer.TryWrite(line);
        }
    }

    internal Task StopAsync()
    {
        latest.Writer.TryComplete();
        return writer;
    }

    private async Task WriteLatestAsync()
    {
        await foreach (var line in latest.Reader.ReadAllAsync())
        {
            await TryWriteAsync(line);
        }
    }

    private async Task TryWriteAsync(string line)
    {
        var pending = path + PendingSuffix;
        var ownsPending = false;
        try
        {
            using var deadline = new CancellationTokenSource(WriteDeadline);
            var directory = Path.GetDirectoryName(path)!;
            RequireUnlinkedAncestors(directory);
            Directory.CreateDirectory(directory);
            RequireUnlinkedAncestors(directory);
            RequireUnlinkedFile(path);
            await using (var stream = new FileStream(pending, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, BufferBytes, FileOptions.Asynchronous))
            {
                ownsPending = true;
                await stream.WriteAsync(Encoding.UTF8.GetBytes(line + Environment.NewLine), deadline.Token);
                await stream.FlushAsync(deadline.Token);
            }
            RequireUnlinkedAncestors(directory);
            RequireUnlinkedFile(path);
            File.Move(pending, path, overwrite: true);
            ownsPending = false;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            Interlocked.Exchange(ref writeFailures, 1);
        }
        finally
        {
            if (ownsPending)
            {
                TryDeletePending(pending);
            }
        }
    }

    private static void RequireUnlinkedAncestors(string directory)
    {
        for (var ancestor = new DirectoryInfo(directory); ancestor is not null; ancestor = ancestor.Parent)
        {
            if (ancestor.LinkTarget is not null)
            {
                throw new IOException(LinkedPath);
            }
        }
    }

    private static void RequireUnlinkedFile(string file)
    {
        if (new FileInfo(file).LinkTarget is not null)
        {
            throw new IOException(LinkedPath);
        }
    }

    private void TryDeletePending(string pending)
    {
        try
        {
            File.Delete(pending);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Interlocked.Exchange(ref writeFailures, 1);
        }
    }
}
