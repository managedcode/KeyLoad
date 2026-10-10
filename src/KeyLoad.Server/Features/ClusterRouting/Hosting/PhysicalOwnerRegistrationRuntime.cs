using KeyLoad.Orleans;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed class PhysicalOwnerRegistrationRuntime : IAsyncDisposable
{
    private readonly PhysicalOwnerProbeClient probes;
    private readonly PhysicalOwnerRegistrationController controller;
    private readonly PhysicalOwnerRegistrationWorker worker;
    private readonly Lock gate = new();
    private Task? disposal;
    private Task? registration;

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

    internal static void RequireProbeWork(NodeOptions options, PhysicalOwnerProbeWorkOwner? work)
    {
        if (options.MembershipAuthority.RegisterPhysicalOwners && work is null)
        { throw Errors.Fail(ErrorCode.OwnershipLost, PhysicalOwnerProbeProtocol.Unavailable); }
    }

    internal static void PublishAndAttachAuthority(ref PhysicalOwnerRegistrationRuntime? slot, OrleansNode node,
        PartitionHost partition, NodeOptions nodeOptions, ServerRuntimeOptions options,
        ReplicaMembershipAuthorityOwner authority, IServiceProvider services, TimeProvider clock)
    {
        if (nodeOptions.MembershipAuthority.Mode != MembershipAuthoritySettingsProtocol.Authority)
        { return; }
        authority.Publish(services.GetRequiredService<IMembershipTable>());
        Attach(ref slot, node, partition, options, services, clock);
    }

    internal static void Attach(ref PhysicalOwnerRegistrationRuntime? slot, OrleansNode node,
        PartitionHost partition, ServerRuntimeOptions options, IServiceProvider services, TimeProvider clock)
    {
        if (!options.Node.Value.MembershipAuthority.RegisterPhysicalOwners
            || options.Node.Value.MembershipAuthority.Mode != MembershipAuthoritySettingsProtocol.Authority)
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

    internal void Start()
    {
        lock (gate)
        { registration = worker.Start(); }
    }

    internal static Task WaitForRegistrationAsync(PhysicalOwnerRegistrationRuntime? registration,
        NodeOptions options, CancellationToken cancellationToken)
    {
        if (!options.MembershipAuthority.RegisterPhysicalOwners
            || options.MembershipAuthority.Mode != MembershipAuthoritySettingsProtocol.Authority)
        { return Task.CompletedTask; }
        return (registration ?? throw Errors.Fail(ErrorCode.OwnershipLost, PhysicalOwnerProbeProtocol.Unavailable))
            .WaitForRegistrationAsync(cancellationToken);
    }

    internal Task WaitForRegistrationAsync(CancellationToken cancellationToken)
    {
        Task original;
        lock (gate)
        {
            original = registration
                ?? throw Errors.Fail(ErrorCode.OwnershipLost, PhysicalOwnerProbeProtocol.Unavailable);
        }
        return original.WaitAsync(cancellationToken);
    }
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
