using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Orleans;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal sealed class PartitionMovementBlobWireTrial : IAsyncDisposable
{
    private const int NoFailures = 0;
    private readonly List<Exception> failures;
    private readonly PartitionMovementBlobWireBorrow observer = new();
    private readonly PartitionMovementLateNativeOwners owners;
    private PartitionMovementBlobWireCallers? callers;
    private Task? producer;
    private Task? disposal;

    private PartitionMovementBlobWireTrial(List<Exception> failures)
    { this.failures = failures; owners = new(observer); }

    internal static async Task RunAsync(CancellationToken cancellationToken)
    {
        using var deadline = new CancellationTokenSource(RequestCqrsRf3Protocol.ParentDeadline, TimeProvider.System);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        var failures = new List<Exception>();
        await using (var trial = new PartitionMovementBlobWireTrial(failures))
        {
            try
            {
                trial.producer = trial.ExecuteAsync(lifetime.Token);
                await ServerFailureObserver.ObserveAsync(() => trial.producer ?? throw new InvalidOperationException("The actual native producer was not retained."), failures);
            }
            finally { ServerFailureObserver.Observe(lifetime.Cancel, failures); }
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private async Task ExecuteAsync(CancellationToken token)
    {
        observer.Attach(owners);
        await owners.StartAsync(token);
        callers = new(owners.Settings);
        await callers.InitializeAsync(owners.Settings, token);
        var seed = new PartitionMovementBlobWireSeed(callers);
        await seed.CreateAsync(owners.Settings, token);
        var terminal = await McpCallerAssertions.SdkSuccessAsync(await callers.Source.MovePartitionAsync(seed.Request, token));
        await Assert.That(terminal.Phase).IsEqualTo(PartitionMovePhase.Retired);
        await seed.RequireAsync(token);
        await PartitionMovementBlobWireEffects.RequireAsync(callers, seed, token);
        await observer.RequireCompleteAsync();
        await owners.StopAsync();
        var before = await PartitionMovementBlobWireCold.RequireAsync(owners, token);
        await owners.StartAsync(token);
        await callers.RequireAsync(seed.Partition, McpToolNames.AdminPartitionMove, seed.Request,
            ct => callers.Source.MovePartitionAsync(seed.Request, ct), terminal, token);
        await seed.RequireAsync(token);
        await owners.StopAsync();
        var after = await PartitionMovementBlobWireCold.RequireAsync(owners, token);
        await PartitionMovementBlobWireCold.RequireUnchangedAsync(before, after);
        await owners.StartAsync(token);
        await seed.RequireAsync(token);
        await SqlRf3Protocol.EqualAsync(terminal,
            await McpCallerAssertions.SdkSuccessAsync(await callers.Source.MovePartitionAsync(seed.Request, token)));
    }

    public ValueTask DisposeAsync() => new(disposal ??= DisposeCoreAsync());

    private async Task DisposeCoreAsync()
    {
        var cleanup = new List<Exception>();
        if (producer is { } tracked)
        { owners.TrackProducer(tracked); }
        await ServerFailureObserver.ObserveAsync(owners.StopAsync, cleanup);
        if (producer is { } original)
        { await ServerFailureObserver.ObserveAsync(() => owners.JoinProducerAsync(original), cleanup); }
        if (producer is { IsCompleted: false })
        { cleanup.Add(new InvalidOperationException("The original native blob producer has not joined; retain its callers and roots.")); }
        else
        {
            if (callers is not null)
            {
                try
                { await callers.DisposeAsync(); }
                catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { cleanup.Add(error); }
                catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error)) { cleanup.Add(error); }
            }
            try
            { observer.Dispose(); }
            catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { cleanup.Add(error); }
            catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error)) { cleanup.Add(error); }
        }
        if (failures.Count != NoFailures || cleanup.Count != NoFailures)
        { owners.RetainRoots(); }
        try
        { await owners.DisposeAsync(); }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { cleanup.Add(error); }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error)) { cleanup.Add(error); }
        failures.AddRange(cleanup);
    }
}
