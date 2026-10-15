using KeyLoad.Core;
using KeyLoad.Replication;
using KeyLoad.Server;
using P = KeyLoad.UnitTests.Features.ClusterReplication.CoordinatorShutdownProtocol;

namespace KeyLoad.UnitTests.Features.ClusterReplication;

internal sealed class CoordinatorShutdownAdmission
{
    internal CancellationTokenSource Lifetime { get; } = new();
    internal CommandAdmissionGovernor Governor { get; } = new(UnitAdmissionOptions.Command());
    internal AdmittedCommandInbox Inbox
    {
        get;
    }
    internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource Exited { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource released = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TestDatabase db;
    private FileNotFoundException? callbackFailure;
    internal FileNotFoundException? CallbackFailure => Volatile.Read(ref callbackFailure);
    internal Task? Worker
    {
        get; private set;
    }
    internal Task? Shutdown
    {
        get; private set;
    }
    internal Task? Disposal
    {
        get; private set;
    }
    internal Exception? ExpectedDisposal
    {
        get; set;
    }
    internal Exception? ExpectedActive
    {
        get; set;
    }
    internal Exception? ExpectedQueued
    {
        get; set;
    }
    private readonly List<AdmittedCommand> submitted = [];

    internal CoordinatorShutdownAdmission(TestDatabase db)
    {
        this.db = db;
        Inbox = new(Governor, UnitAdmissionOptions.Inbox());
    }

    internal AdmittedCommand Enqueue(string id)
    {
        var command = new CommandRequest(Guid.NewGuid(), db.Partition, [new PutDocument(P.Collection, id, P.Json)]);
        var operation = db.Database.NormalizeOperation(new(command.CommandId, OperationKind.Batch, P.Root,
            TimeProvider.System.GetUtcNow(), System.Text.Encoding.UTF8.GetString(JsonDefaults.Serialize(command))));
        var principal = db.Store.Read(view => db.Database.Principal(view, P.Root, TimeProvider.System.GetUtcNow()));
        var admitted = Inbox.Enqueue(operation, principal, System.Text.Encoding.UTF8.GetByteCount(operation.PayloadJson));
        submitted.Add(admitted);
        return admitted;
    }

    internal void Start() => Worker = RunAsync();
    internal Task Stop() => Shutdown ??= ReplicaCoordinatorShutdown.DrainAsync(Inbox, Lifetime, Worker ?? Task.CompletedTask);
    internal Task Dispose() => Disposal ??= DisposeCoreAsync();

    private async Task DisposeCoreAsync()
    {
        var failures = await ReplicaCoordinatorShutdown.JoinDisposalAsync(Stop()).ConfigureAwait(false);
        try
        {
            await ReplicaShutdownStage.ObserveAsync(Inbox.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        }
        catch (Exception failure)
        {
            failures.Add(failure);
            DisposeLifetime(failures);
            throw;
        }
        DisposeLifetime(failures);
    }

    private void DisposeLifetime(List<Exception> failures)
    {
        try
        {
            Lifetime.Dispose();
        }
        catch (Exception failure)
        {
            failures.Add(failure);
            ReplicaCoordinatorShutdown.ThrowIfAny(failures);
            throw;
        }
        ReplicaCoordinatorShutdown.ThrowIfAny(failures);
    }

    internal void Release() => released.TrySetResult();

    private async Task RunAsync()
    {
        var pending = await Inbox.ReadAsync(Lifetime.Token);
        if (pending is null)
        {
            return;
        }
        using var registration = Lifetime.Token.Register(ReadMissingImage);
        Entered.TrySetResult();
        try
        {
            await released.Task.ConfigureAwait(false);
            Lifetime.Token.ThrowIfCancellationRequested();
            pending.Complete(db.Database.ApplyEmbedded(pending.Operation, Lifetime.Token));
        }
        catch (OperationCanceledException)
        {
            pending.Fail(Errors.Fail(ErrorCode.UnknownWriteOutcome, ReplicaProtocol.InterruptedWrite));
        }
        finally
        {
            Exited.TrySetResult();
        }
    }

    private void ReadMissingImage()
    {
        try
        {
            using var missing = File.OpenRead(Path.Combine(db.Directory, P.MissingFile));
        }
        catch (FileNotFoundException failure)
        {
            Volatile.Write(ref callbackFailure, failure);
            throw;
        }
        finally
        {
            Cancelled.TrySetResult();
        }
    }

    internal async Task CleanupAsync(List<Exception> failures)
    {
        Task? closing = null;
        ServerFailureObserver.Observe(() => closing = Dispose(), failures);
        Release();
        if (closing is not null)
        {
            await ObserveExpectedAsync(() => closing, () => ExpectedDisposal, failures);
        }
        if (Worker is { } worker)
        {
            await ServerFailureObserver.ObserveAsync(() => worker, failures);
        }
        foreach (var pending in submitted)
        {
            await ObserveExpectedAsync(() => pending.Completion,
                () => pending.Operation == submitted.First().Operation ? ExpectedActive : ExpectedQueued, failures);
        }
    }

    private static Task ObserveExpectedAsync(Func<Task> task, Func<Exception?> expected, List<Exception> failures)
        => ServerFailureObserver.ObserveAsync(async () =>
        {
            try
            {
                await task().ConfigureAwait(false);
            }
            catch (Exception failure) when (ReferenceEquals(failure, expected()))
            {
            }
        }, failures);
}
