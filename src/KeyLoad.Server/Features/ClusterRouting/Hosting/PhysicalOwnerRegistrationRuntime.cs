using KeyLoad.Orleans;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed class PhysicalOwnerRegistrationRuntime : IAsyncDisposable
{
    private readonly PhysicalOwnerProbeClient probes;
    private readonly PhysicalOwnerRegistrationController controller;
    private readonly PhysicalOwnerRegistrationWorker worker;
    private readonly Lock gate = new();
    private Task? disposal;

    internal PhysicalOwnerRegistrationRuntime(OrleansNode node, PartitionHost partition,
        ServerRuntimeOptions options, ISiloStatusOracle oracle, TimeProvider clock, ILogger logger)
    {
        probes = new(options.Node, node, partition, options.Membership, clock);
        try
        {
            var requests = new PhysicalOwnerStartupRequests(node, options.Node, options.McpExecution);
            controller = new(node, partition, probes, requests, options.Node);
            worker = new(oracle, node, controller, options.GrainRouting, clock, logger);
        }
        catch (Exception exception) when (NativeCqrsBoundaryErrors.IsNonFatal(exception))
        {
            var failures = new List<Exception> { exception };
            try
            { probes.Dispose(); }
            catch (Exception cleanup) when (NativeCqrsBoundaryErrors.IsNonFatal(cleanup)) { failures.Add(cleanup); }
            catch (Exception cleanup) when (!NativeCqrsBoundaryErrors.IsNonFatal(cleanup)) { failures.Add(cleanup); }
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }

    internal static void Attach(ref PhysicalOwnerRegistrationRuntime? slot, OrleansNode node,
        PartitionHost partition, ServerRuntimeOptions options, IServiceProvider services, TimeProvider clock)
    {
        if (!options.Node.Value.MembershipAuthority.RegisterPhysicalOwners)
        { return; }
        slot = new PhysicalOwnerRegistrationRuntime(node, partition, options,
            services.GetRequiredService<ISiloStatusOracle>(), clock,
            services.GetRequiredService<ILoggerFactory>().CreateLogger<PhysicalOwnerRegistrationWorker>());
        slot.Start();
    }

    internal static async Task StopAndJoinAsync(PhysicalOwnerRegistrationRuntime? registration,
        PhysicalOwnerProbeWorkOwner? probes, List<Exception> failures)
    {
        if (registration is not null)
        { await ServerFailureObserver.ObserveAsync(() => registration.DisposeAsync().AsTask(), failures).ConfigureAwait(false); }
        if (probes is not null)
        { await ServerFailureObserver.ObserveAsync(probes.StopAsync, failures).ConfigureAwait(false); }
    }

    internal void Start() => worker.Start();
    internal PhysicalOwnerRegistrationObservation Observe() => controller.Observe();

    public ValueTask DisposeAsync()
    {
        lock (gate)
        { disposal ??= DisposeCoreAsync(); return new ValueTask(disposal); }
    }

    private async Task DisposeCoreAsync()
    {
        var failures = new List<Exception>();
        try
        { await worker.DisposeAsync().ConfigureAwait(false); }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        try
        { probes.Dispose(); }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
