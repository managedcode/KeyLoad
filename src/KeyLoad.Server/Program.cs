using KeyLoad;
using KeyLoad.Server;
using Microsoft.Extensions.Options;

const string PhysicalOwnerUnjoined = "The original physical owner has not joined; retain its application and root.";

global::KeyLoad.SimdRuntime.RequireHardwareSupport();
_ = SerializationExecutionRegistration.Process.Value;
var app = await ServerConfiguration.BuildAsync(args).ConfigureAwait(false);
if (app.Services.GetService<CoordinationSiloOptions>() is not null)
{
    await using (app.ConfigureAwait(false))
    {
        await app.RunAsync().ConfigureAwait(false);
    }
    return;
}
var failures = new List<Exception>();
OrleansNode? silo = null;
async Task RunLifetimeAsync()
{
    try
    {
        silo = app.Services.GetRequiredService<OrleansNode>();
        await app.StartAsync(app.Lifetime.ApplicationStopping).ConfigureAwait(false);
        await silo.StartAsync(app.Lifetime.ApplicationStopping).ConfigureAwait(false);
        await silo.WaitForPhysicalOwnerRegistrationAsync(app.Lifetime.ApplicationStopping).ConfigureAwait(false);
        await WaitForStopAsync(app.Lifetime.ApplicationStopping).ConfigureAwait(false);
    }
    catch (OperationCanceledException) when (app.Lifetime.ApplicationStopping.IsCancellationRequested)
    {
    }
}
await ServerFailureObserver.ObserveAsync(RunLifetimeAsync, failures).ConfigureAwait(false);
await ShutdownAsync(app, silo, failures).ConfigureAwait(false);
ServerFailureObserver.ThrowIfAny(failures);

static async Task WaitForStopAsync(CancellationToken stopping)
{
    var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    using var registration = stopping.Register(static value => ((TaskCompletionSource)value!).TrySetResult(), signal);
    await signal.Task.ConfigureAwait(false);
}

static async Task ShutdownAsync(WebApplication app, OrleansNode? silo,
    List<Exception> failures)
{
    var settings = app.Services.GetRequiredService<IOptions<ServerExecutionOptions>>().Value;
    using var deadline = new CancellationTokenSource(settings.ShutdownTimeout, app.Services.GetRequiredService<TimeProvider>());
    var connections = app.Services.GetRequiredService<ServerConnectionRegistry>();
    await ServerFailureObserver.ObserveAsync(() => connections.ShutdownAsync(deadline.Token), failures).ConfigureAwait(false);
    await ServerFailureObserver.ObserveAsync(() => connections.ShutdownAsync(CancellationToken.None), failures).ConfigureAwait(false);
    if (silo is not null)
    {
        await ServerFailureObserver.ObserveAsync(() => silo.StopAsync(deadline.Token), failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(() => silo.StopAsync(CancellationToken.None), failures).ConfigureAwait(false);
        if (!silo.HasJoinedRequestWork)
        {
            return;
        }
    }
    await ServerFailureObserver.ObserveAsync(() => app.StopAsync(deadline.Token), failures).ConfigureAwait(false);
    var physical = app.Services.GetRequiredService<PartitionStorageLifecycle>();
    await ServerFailureObserver.ObserveAsync(() => physical.StopAsync(CancellationToken.None), failures).ConfigureAwait(false);
    var owner = app.Services.GetRequiredService<PartitionHostApplicationOwner>();
    if (owner.Closing is not { IsCompleted: true } || !owner.CanReleaseRoot)
    {
        failures.Add(new InvalidOperationException(PhysicalOwnerUnjoined));
        return;
    }
    await ServerFailureObserver.ObserveAsync(() => app.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
}
