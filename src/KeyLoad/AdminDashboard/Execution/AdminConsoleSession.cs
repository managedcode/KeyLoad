using KeyLoad.Client;
using ManagedCode.Communication;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

/// <summary>Owns cancellable, freshly authorized reads and state for one administration circuit.</summary>
/// <param name="connection">The circuit-owned local authenticated connection.</param>
/// <param name="options">Validated circuit resource bounds.</param>
internal sealed class AdminConsoleSession(AdminConsoleConnection connection, IOptions<AdminConsoleOptions> options) : IAsyncDisposable
{
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? operation;
    private Task? pending;
    private long epoch;
    internal AdminNodeSnapshot? Snapshot { get; private set; }
    internal AdminMeasurements Measurements { get; } = new(options);
    internal AdminConsoleData Data { get; } = new(options);
    internal string Status { get; private set; } = AdminConsoleText.Connect;
    internal bool Connected => Snapshot is not null && connection.Client is not null;
    internal TimeSpan PollInterval => options.Value.PollInterval;
    internal bool Busy => pending is { IsCompleted: false };
    internal string Tenant { get; set; } = AdminConsoleText.DefaultScope;
    internal string Database { get; set; } = AdminConsoleText.DefaultScope;
    internal string PartitionKey { get; set; } = AdminConsoleText.DefaultPartition;

    internal async Task ConnectAsync(string credential)
    {
        await DisconnectAsync();
        connection.Connect(credential.Trim());
        await RefreshAsync();
    }

    internal Task RefreshAsync() => RunAsync(async (client, token) =>
    {
        var snapshot = Value(await client.DashboardAsync(token));
        token.ThrowIfCancellationRequested();
        Measurements.Observe(snapshot);
        Snapshot = snapshot;
        Status = AdminConsoleText.Updated;
    });

    internal async Task LoadResourcesAsync(string view, bool next = false)
    {
        var after = next ? Data.ResourceAfter : null;
        var cancellation = CancelAsync();
        Data.Clear();
        Status = AdminConsoleText.LoadingResources;
        await cancellation;
        await RunAsync(async (client, token) =>
        {
            var resources = Value(await client.ListResourcesAsync(new(Tenant, Database, after), token));
            token.ThrowIfCancellationRequested();
            Data.SetResources(resources, view);
            Status = AdminConsoleText.ResourcesReady;
        });
    }

    internal async Task LoadDataAsync(AdminResourceInfo selected, bool next = false)
    {
        var after = next ? Data.After : null;
        var cancellation = CancelAsync();
        Data.ClearRows();
        Data.Selected = selected;
        Status = AdminConsoleText.LoadingRecords;
        await cancellation;
        await RunAsync(async (client, token) =>
        {
            await Data.LoadAsync(client, connection.Http, new(Tenant, Database, selected.TransactionDomainId, PartitionKey), after, token);
            Status = AdminConsoleText.RecordsReady;
        });
    }

    private Task RunAsync(Func<KeyLoadClient, CancellationToken, Task> action)
    {
        if (Busy || connection.Client is not { } client)
        {
            return Task.CompletedTask;
        }
        operation?.Dispose();
        operation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        operation.CancelAfter(options.Value.RequestTimeout);
        pending = ExecuteAsync(client, action, epoch, operation.Token);
        return pending;
    }

    private async Task ExecuteAsync(KeyLoadClient client, Func<KeyLoadClient, CancellationToken, Task> action,
        long started, CancellationToken token)
    {
        try
        {
            await action(client, token);
        }
        catch (OperationCanceledException) when (started != epoch || lifetime.IsCancellationRequested)
        {
            // The caller joins this operation before clearing state or starting the next one.
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or InvalidOperationException or UnauthorizedAccessException)
        {
            if (started != epoch || lifetime.IsCancellationRequested)
            {
                return;
            }
            Snapshot = null;
            Measurements.Clear();
            Data.Clear();
            connection.Disconnect();
            Status = error is UnauthorizedAccessException ? AdminConsoleText.Unauthorized : AdminConsoleText.Unavailable;
        }
    }

    internal static T Value<T>(Result<T> result)
    {
        if (result.IsSuccess)
        {
            return result.Value;
        }
        if (result.Problem.ErrorCode is nameof(ErrorCode.Unauthenticated) or nameof(ErrorCode.PermissionDenied))
        {
            throw new UnauthorizedAccessException(AdminConsoleText.ReadFailed);
        }
        throw new InvalidOperationException(AdminConsoleText.ReadFailed);
    }

    private async Task CancelAsync()
    {
        epoch++;
        if (operation is not null)
        {
            await operation.CancelAsync();
        }
        if (pending is not null)
        {
            await pending;
        }
        operation?.Dispose();
        operation = null;
        pending = null;
    }

    internal async Task DisconnectAsync()
    {
        var cancellation = CancelAsync();
        connection.Disconnect();
        Snapshot = null;
        Measurements.Clear();
        Data.Clear();
        Status = AdminConsoleText.Disconnected;
        await cancellation;
    }

    public async ValueTask DisposeAsync()
    {
        await lifetime.CancelAsync();
        await DisconnectAsync();
        lifetime.Dispose();
    }
}
