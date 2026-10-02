using System.Runtime.ExceptionServices;
using System.Threading.Channels;
using KeyLoad.Replication;
using TUnit.Assertions.Exceptions;

namespace KeyLoad.RecoveryTests;

/// <summary>AC-REP-004: complements real-node recovery with terminal cleanup of a genuine filesystem task failure.</summary>
internal sealed class ReplicaMaterializerShutdownTests
{
    private const int ImmediateWaitMilliseconds = 0;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);
    private static CancellationToken TestToken => TestContext.Current!.Execution.CancellationToken;

    /// <summary>The pure helper drains actual file work, preserves its original exception and closes its real synchronization resources.</summary>
    [Test]
    public async Task FilesystemFailurePreservesOriginalErrorAfterPendingWorkAndTerminalCleanup()
    {
        using var deadline = new CancellationTokenSource(Timeout, TimeProvider.System);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, TestToken);
        await using var fixture = new ReplicaMaterializerShutdownFixture(linked.Token);
        await fixture.RunAsync(async () =>
        {
            await fixture.Entered.WaitAsync(linked.Token);
            await Assert.That(File.Exists(fixture.MissingFilePath)).IsFalse();
            var terminal = fixture.BeginShutdown();
            try
            {
                await Assert.That(terminal.IsCompleted).IsFalse();
                await Assert.That(fixture.Worker.IsCompleted).IsFalse();
                await Assert.That(fixture.Lifetime.IsCancellationRequested).IsTrue();
                await Assert.That(fixture.Writer.TryWrite(true)).IsFalse();
                await Assert.That(await fixture.Writer.WaitToWriteAsync(linked.Token)).IsFalse();
                await Assert.That(fixture.TerminalPublished.IsCompleted).IsFalse();
            }
            finally { fixture.Release(); }
            var failure = await Assert.ThrowsExactlyAsync<FileNotFoundException>(() => terminal.WaitAsync(linked.Token));
            await Assert.That(fixture.FilesystemFailure).IsNotNull();
            await Assert.That(failure).IsSameReferenceAs(fixture.FilesystemFailure);
            await Assert.That(terminal.IsFaulted).IsTrue();
            await Assert.That(await fixture.TerminalPublished.WaitAsync(linked.Token)).IsTrue();
            await Assert.That(Assert.ThrowsExactly<ObjectDisposedException>(() => fixture.ApplyGate.Wait(ImmediateWaitMilliseconds))).IsNotNull();
            await Assert.That(Assert.ThrowsExactly<ObjectDisposedException>(() => fixture.ProtocolGate.Wait(ImmediateWaitMilliseconds))).IsNotNull();
            await Assert.That(Assert.ThrowsExactly<ObjectDisposedException>(() => _ = fixture.Lifetime.Token)).IsNotNull();
        });
    }
}

internal sealed class ReplicaMaterializerShutdownFixture : IAsyncDisposable
{
    private const string DirectoryPrefix = "keyload-materializer-shutdown-";
    private const string MissingFileName = "missing-image.bin";
    private const int SingleOwner = 1;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);
    private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory(DirectoryPrefix);
    private readonly Channel<bool> work = Channel.CreateBounded<bool>(SingleOwner);
    private readonly TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource released = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> published = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<Exception> failures = [];
    private FileNotFoundException? filesystemFailure;
    private Task? terminal;
    private Task? cleanup;
    private bool scenarioPassed;
    private bool runStarted;

    internal ReplicaMaterializerShutdownFixture(CancellationToken cancellationToken)
    {
        MissingFilePath = Path.Combine(directory.FullName, MissingFileName);
        Worker = Task.Run(() => OpenMissingFileAsync(cancellationToken), CancellationToken.None);
    }

    internal CancellationTokenSource Lifetime { get; } = new();
    internal SemaphoreSlim ApplyGate { get; } = new(SingleOwner, SingleOwner);
    internal SemaphoreSlim ProtocolGate { get; } = new(SingleOwner, SingleOwner);
    internal string MissingFilePath { get; }
    internal Task Worker { get; }
    internal Task Entered => entered.Task;
    internal Task<bool> TerminalPublished => published.Task;
    internal ChannelWriter<bool> Writer => work.Writer;
    internal FileNotFoundException? FilesystemFailure => Volatile.Read(ref filesystemFailure);

    internal Task BeginShutdown() => terminal ??= ReplicaMaterializerShutdown.DisposeAsync(Lifetime, Writer, Worker,
        ApplyGate, ProtocolGate, () => published.TrySetResult(Worker.IsCompleted));

    internal void Release() => released.TrySetResult();

    private async Task OpenMissingFileAsync(CancellationToken cancellationToken)
    {
        entered.TrySetResult();
        await released.Task.WaitAsync(Timeout, TimeProvider.System, cancellationToken);
        try
        {
            using var image = File.OpenRead(MissingFilePath);
        }
        catch (FileNotFoundException error)
        {
            Volatile.Write(ref filesystemFailure, error);
            throw;
        }
    }

    internal async Task RunAsync(Func<Task> scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        runStarted = true;
        try
        {
            await scenario();
            scenarioPassed = true;
        }
        catch (AssertionException error) { AddFailure(error); }
        catch (IOException error) { AddFailure(error); }
        catch (InvalidOperationException error) { AddFailure(error); }
        catch (OperationCanceledException error) { AddFailure(error); }
        catch (TimeoutException error) { AddFailure(error); }
        catch (KeyLoadException error) { AddFailure(error); }
        finally { await EnsureCleanupAsync(); }
        ThrowFailures();
    }

    /// <summary>Releases pending file work and observes every real task before closing resources and removing the private directory.</summary>
    /// <returns>Completion after owned tasks and resources have been handled without losing scenario or cleanup failures.</returns>
    public async ValueTask DisposeAsync()
    {
        await EnsureCleanupAsync();
        if (!runStarted)
        {
            ThrowFailures();
        }
    }

    private Task EnsureCleanupAsync() => cleanup ??= CleanupAsync();

    private async Task CleanupAsync()
    {
        Release();
        Writer.TryComplete();
        await ObserveAsync(Worker);
        if (terminal is not null)
        {
            await ObserveAsync(terminal);
        }
        Attempt(ApplyGate.Dispose);
        Attempt(ProtocolGate.Dispose);
        Attempt(Lifetime.Dispose);
        Attempt(() => directory.Delete(true));
    }

    private async Task ObserveAsync(Task task)
    {
        try
        {
            // Release is unconditional; cleanup waits for the actual file task before deleting its directory.
            await task.ConfigureAwait(false);
        }
        catch (FileNotFoundException error)
        {
            // Only the filesystem error already asserted by the fully successful scenario is expected on repeated observation.
            if (!scenarioPassed || !ReferenceEquals(error, FilesystemFailure))
            {
                AddFailure(error);
            }
        }
    }

    private void Attempt(Action action)
    {
        try
        { action(); }
        catch (IOException error) { AddFailure(error); }
        catch (UnauthorizedAccessException error) { AddFailure(error); }
        catch (ObjectDisposedException error) { AddFailure(error); }
        catch (InvalidOperationException error) { AddFailure(error); }
    }

    private void AddFailure(Exception error)
    {
        if (!failures.Contains(error))
        {
            failures.Add(error);
        }
    }

    private void ThrowFailures()
    {
        if (failures.Count == 1)
        {
            ExceptionDispatchInfo.Capture(failures[0]).Throw();
        }
        if (failures.Count > 1)
        {
            throw new AggregateException(failures);
        }
    }
}
