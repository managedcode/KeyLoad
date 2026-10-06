using Microsoft.Win32.SafeHandles;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class ExistingStoreInspectorLifetime : IAsyncDisposable
{
    private const int ExecutionDeadlineSeconds = 30;
    private const string MissingOuterOwnerMessage = "The existing outer node owner lock is missing.";
    private const string OuterOwnerLostMessage = "The inspector parent did not retain exclusive outer ownership.";
    private readonly List<Exception> failures = [];
    private FileStream? owner;
    private SafeFileHandle? ownerHandle;
    private ExistingStoreInspectorSession? session;
    private CancellationTokenSource? deadline;
    private CancellationTokenSource? timeout;
    private ExistingStoreInspectorExit? result;
    private string assemblySha256 = string.Empty;
    private bool canceled;

    private ExistingStoreInspectorLifetime() { }

    internal static async Task<ExistingStoreInspectorExit> RunAsync(string payload, string outerOwnerPath,
        bool cancelWhenReady, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outerOwnerPath);
        cancellationToken.ThrowIfCancellationRequested();
        var lifetime = new ExistingStoreInspectorLifetime();
        await ExistingStoreInspectorFailureJoin.ObserveAsync(
            lifetime.ExecuteAsync(payload, outerOwnerPath, cancelWhenReady, cancellationToken), lifetime.failures);
        await ExistingStoreInspectorFailureJoin.ObserveAsync(lifetime.DisposeAsync().AsTask(), lifetime.failures);
        ExistingStoreInspectorFailureJoin.Throw(lifetime.failures);
        return lifetime.result!;
    }

    private async Task ExecuteAsync(string payload, string outerOwnerPath,
        bool cancelWhenReady, CancellationToken cancellationToken)
    {
        owner = OpenOuterOwner(outerOwnerPath);
        ownerHandle = owner.SafeFileHandle;
        var (startInfo, assemblyPath) = ExistingStoreInspectorLaunchSettings.Create();
        assemblySha256 = await HashAssemblyAsync(assemblyPath);
        session = await ExistingStoreInspectorSession.StartAsync(startInfo, payload);
        timeout = new CancellationTokenSource(TimeSpan.FromSeconds(ExecutionDeadlineSeconds), TimeProvider.System);
        deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            canceled = await session.WaitAsync(cancelWhenReady, deadline.Token);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            canceled = true;
        }
        if (canceled)
        {
            AssertOuterOwnerHeld(outerOwnerPath);
            await deadline.CancelAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (session is not null)
        {
            var settlement = session.SettleAsync(failures, killImmediately: canceled || failures.Count > 0);
            var settled = await ExistingStoreInspectorFailureJoin.ObserveAsync(settlement, failures);
            if (!session.OriginalTasksJoined)
            {
                await ExistingStoreInspectorSessionStartFailure.RetainUnsettledAsync(session.Process, failures);
            }
            if (settled)
            {
                var cleanupWarning = await settlement;
                ExistingStoreInspectorFailureJoin.Capture(() => result = ExistingStoreInspectorProcess.CreateExit(
                    session, assemblySha256, canceled, cleanupWarning, ownerReleased: false), failures);
            }
            session.Release(failures);
        }
        try
        {
            DisposeDeadlineOwned();
        }
        catch (AggregateException owned)
        {
            ExistingStoreInspectorFailureJoin.Add(owned.InnerExceptions[0], failures);
        }
        if (owner is not null)
        {
            await ExistingStoreInspectorFailureJoin.ObserveAsync(ReleaseOwnerAsync(), failures);
        }
    }

    private void DisposeDeadlineOwned()
    {
        try
        {
            deadline?.Dispose();
            timeout?.Dispose();
        }
        catch (Exception original)
        {
            throw new AggregateException(original);
        }
    }

    private async Task ReleaseOwnerAsync()
    {
        await owner!.DisposeAsync();
        if (result is not null)
        {
            result = result with { OuterOwnerReleased = ownerHandle!.IsClosed };
        }
    }

    private static void AssertOuterOwnerHeld(string path)
    {
        try
        {
            using var probe = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException)
        {
            return;
        }
        throw new IOException(OuterOwnerLostMessage);
    }

    private static FileStream OpenOuterOwner(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(MissingOuterOwnerMessage, path);
        }
        return new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    private static async Task CloseAssemblyAsync(FileStream assembly) => await assembly.DisposeAsync();

    private static async Task<byte[]> ReadHashAsync(FileStream assembly)
        => await System.Security.Cryptography.SHA256.HashDataAsync(assembly);

    private static async Task<string> HashAssemblyAsync(string path)
    {
        var assembly = File.OpenRead(path);
        var failures = new List<Exception>();
        Task<byte[]>? hash = null;
        ExistingStoreInspectorFailureJoin.Capture(() => hash = ReadHashAsync(assembly), failures);
        if (hash is not null)
        {
            await ExistingStoreInspectorFailureJoin.ObserveAsync(hash, failures);
        }
        await ExistingStoreInspectorFailureJoin.ObserveAsync(CloseAssemblyAsync(assembly), failures);
        ExistingStoreInspectorFailureJoin.Throw(failures);
        return Convert.ToHexStringLower(await hash!);
    }
}
