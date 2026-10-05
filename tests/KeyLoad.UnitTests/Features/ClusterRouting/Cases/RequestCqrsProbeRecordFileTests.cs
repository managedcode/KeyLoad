using System.Net.Sockets;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;
using KeyLoad.Storage.IO;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class RequestCqrsProbeRecordFileTests
{
    private const string CanariedRecord = "private-record-canary";
    private const string SafeRegularFailure = "Offline input is not a supported regular file.";
    private static readonly TimeSpan ProbeBound = TimeSpan.FromSeconds(3);

    [Test]
    public async Task AcCrs004ReadsOwned0600RegularRecordBytesExactly()
    {
        if (OperatingSystem.IsWindows())
        { throw new PlatformNotSupportedException("Private file mode tests require a Unix host."); }
        using var directory = RequestCqrsProbeRecordFileFixture.Create();
        var path = directory.CreateRegular(RequestCqrsProbeRecordFileFixture.RecordName, [0x00, 0x71, 0xFF, 0x18]);
        var identity = OfflineRegularFile.Inspect(path);
        await Assert.That(identity.Length).IsEqualTo(4);
        await Assert.That(File.GetUnixFileMode(path)).IsEqualTo(RequestCqrsProbeRecordFileFixture.PrivateFileMode);

        byte[] expected = [0x00, 0x71, 0xFF, 0x18];
        var actual = RequestCqrsProbeFiles.ReadRecord(path);
        await Assert.That(actual.SequenceEqual(expected)).IsTrue();

        var maximum = RequestCqrsProbeCodecInput.PadOwner(RequestCqrsProbeProtocol.MaximumRecordBytes);
        var maximumPath = directory.CreateRegular(RequestCqrsProbeRecordFileFixture.MaximumName, maximum);
        var maximumBytes = RequestCqrsProbeFiles.ReadRecord(maximumPath);
        await Assert.That(maximumBytes.Length).IsEqualTo(RequestCqrsProbeProtocol.MaximumRecordBytes);
        await Assert.That(RequestCqrsProbeJson.ReadOwner(maximumBytes).SessionId)
            .IsEqualTo(RequestCqrsProbeCodecInput.SessionId);

        var oversized = directory.CreateRegular(RequestCqrsProbeRecordFileFixture.OversizedName,
            new byte[RequestCqrsProbeProtocol.MaximumRecordBytes + 1]);
        var invalid = Assert.ThrowsExactly<InvalidOperationException>(() => RequestCqrsProbeFiles.ReadRecord(oversized));
        await Assert.That(invalid.Message).IsEqualTo(RequestCqrsProbeProtocol.InvalidFiles);
        await Assert.That(invalid.Message.Contains(oversized, StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task AcCrs004RejectsLinksDirectoriesAndSocketsWithSafeDetails()
    {
        using var directory = RequestCqrsProbeRecordFileFixture.Create();
        var target = directory.CreateRegular(RequestCqrsProbeRecordFileFixture.RecordName,
            System.Text.Encoding.UTF8.GetBytes(CanariedRecord));
        var link = Path.Combine(directory.Root, RequestCqrsProbeRecordFileFixture.LinkName);
        File.CreateSymbolicLink(link, target);
        var nested = Path.Combine(directory.Root, RequestCqrsProbeRecordFileFixture.NestedDirectoryName);
        Directory.CreateDirectory(nested);
        var socketPath = Path.Combine(directory.Root, RequestCqrsProbeRecordFileFixture.SocketName);
        using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        socket.Bind(new UnixDomainSocketEndPoint(socketPath));

        await AssertRejectedWithoutDisclosureAsync(link);
        await AssertRejectedWithoutDisclosureAsync(nested);
        await AssertRejectedWithoutDisclosureAsync(socketPath);
        var retained = await File.ReadAllTextAsync(target);
        await Assert.That(retained).IsEqualTo(CanariedRecord);
    }

    [Test]
    public async Task AcCrs004RejectsFifoWithoutWaitingForAWriter()
    {
        using var directory = RequestCqrsProbeRecordFileFixture.Create();
        var fifo = Path.Combine(directory.Root, RequestCqrsProbeRecordFileFixture.FifoName);
        RequestCqrsProbeNativeFifo.Create(fifo);
        var failures = new List<Exception>();
        Task<byte[]>? read = null;
        try
        {
            read = Task.Factory.StartNew(() => RequestCqrsProbeFiles.ReadRecord(fifo), CancellationToken.None,
                TaskCreationOptions.LongRunning, TaskScheduler.Default);
            var completedWithoutWriter = await Task.WhenAny(read, Task.Delay(ProbeBound)) == read;
            await ServerFailureObserver.ObserveAsync(() => AssertFifoFailureAsync(read, fifo, completedWithoutWriter),
                failures);
        }
        finally
        {
            if (read is not null)
            {
                if (!read.IsCompleted)
                { ServerFailureObserver.Observe(() => OpenAndCloseWriter(fifo), failures); }
                await ServerFailureObserver.ObserveAsync(() => JoinReadAsync(read), failures);
            }
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task AssertFifoFailureAsync(Task<byte[]> read, string fifo, bool completedWithoutWriter)
    {
        if (!completedWithoutWriter)
        { OpenAndCloseWriter(fifo); }
        var failure = (await Assert.ThrowsExactlyAsync<KeyLoadException>(() => read))!;
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.FormatUnsupported);
        await Assert.That(failure.Message).IsEqualTo(SafeRegularFailure);
        await Assert.That(failure.Message.Contains(fifo, StringComparison.Ordinal)).IsFalse();
        await Assert.That(completedWithoutWriter).IsTrue();
    }

    private static void OpenAndCloseWriter(string fifo)
    {
        using var writer = new FileStream(fifo, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
    }

    private static async Task JoinReadAsync(Task<byte[]> read)
        => await ((Task)read).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

    private static async Task AssertRejectedWithoutDisclosureAsync(string path)
    {
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => RequestCqrsProbeFiles.ReadRecord(path));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.FormatUnsupported);
        await Assert.That(failure.Message).IsEqualTo(SafeRegularFailure);
        await Assert.That(failure.Message.Contains(path, StringComparison.Ordinal)).IsFalse();
        await Assert.That(failure.Message.Contains(CanariedRecord, StringComparison.Ordinal)).IsFalse();
    }
}
