using KeyLoad.Orleans;
using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class ConnectionNativeScenario : IAsyncDisposable
{
    private readonly ConnectionNativeCalls calls = new();
    private bool closed;
    private bool initialized;
    internal RequestCqrsClusterFixture Fixture { get; }
    internal ConnectionOperationObservation Observation { get; } = new();
    internal NativeRuntimeTestOptions Timing { get; } = new();
    internal IConnectionGrain Connection => Fixture.Cluster.Client.GetGrain<IConnectionGrain>(Fixture.ConnectionId);

    internal ConnectionNativeScenario(GrainRoutingOptions? routing = null)
        => Fixture = new(routing: routing, observer: Observation);

    internal static async Task RunAsync(Func<ConnectionNativeScenario, Task> execute,
        GrainRoutingOptions? routing = null)
    {
        var failures = new List<Exception>();
        try
        {
            await using var scenario = new ConnectionNativeScenario(routing);
            await KeyLoad.Server.ServerFailureObserver.ObserveAsync(() => execute(scenario), failures);
            if (failures.Count > ConnectionNativeProtocol.EmptyFailures)
            {
                KeyLoad.Server.ServerFailureObserver.Observe(
                    () => scenario.WriteTrace(ConnectionNativeProtocol.BodyFailure), failures);
            }
        }
        catch (Exception failure) when (NativeCqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
        catch (Exception failure) when (!NativeCqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
        KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }

    internal async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await Fixture.InitializeAsync().WaitAsync(cancellationToken);
        initialized = true;
        Fixture.Database.Configure(ConnectionNativeProtocol.Collection, ResourceKind.Collection);
        await AssertActivationCountAsync(ConnectionNativeProtocol.AbsentActivations, cancellationToken);
    }

    internal PrincipalRecord Principal()
    {
        var record = new PrincipalRecord(ConnectionNativeProtocol.SubjectPrefix + Guid.NewGuid().ToString("N"),
            Fixture.Database.Partition.TenantId,
            [new ScopeGrant(Fixture.Database.Partition.DatabaseId, ConnectionNativeProtocol.Collection,
                Capability.DocumentsRead | Capability.DocumentsWrite)], []);
        return Fixture.Database.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(record))
            .Get<PrincipalRecord>();
    }

    internal CommandRequest Command(string documentId, string json = ConnectionNativeProtocol.FirstJson,
        long? expectedRevision = null, Guid? commandId = null)
        => new(commandId ?? Guid.NewGuid(), Fixture.Database.Partition,
            [new PutDocument(ConnectionNativeProtocol.Collection, documentId, json, expectedRevision)]);

    internal Task<GrainOperationReply> CommandAsync(PrincipalRecord principal, Guid requestId,
        CommandRequest command, CancellationToken cancellationToken)
    {
        var signed = Fixture.Codec.CreateCommand(requestId, principal.Id, OperationKind.Batch,
            command.CommandId, NativeSerialization.Serialize(command));
        return InvokeAsync(principal, requestId, command.CommandId, signed, cancellationToken);
    }

    internal Task<GrainOperationReply> ReadAsync(PrincipalRecord principal, Guid requestId,
        string documentId, CancellationToken cancellationToken)
    {
        var signed = Fixture.Codec.CreateRead(requestId, principal.Id, GrainReadKind.Document,
            NativeSerialization.Serialize(new GetDocumentRequest(Reference(documentId))));
        return InvokeAsync(principal, requestId, Guid.Empty, signed, cancellationToken);
    }

    internal Task<GrainOperationReply> InvokeAsync(PrincipalRecord principal, Guid requestId,
        Guid commandId, string signed, CancellationToken cancellationToken)
        => calls.Retain(requestId, InvokeCoreAsync(principal, requestId, commandId, signed, cancellationToken));

    private async Task<GrainOperationReply> InvokeCoreAsync(PrincipalRecord principal, Guid requestId,
        Guid commandId, string signed, CancellationToken cancellationToken)
    {
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, calls.Token);
        using var identity = new GrainRequestIdentityScope(Fixture.Cluster.ServiceProvider, principal,
            requestId, commandId, bounded.Token, connectionId: Fixture.ConnectionId);
        return await GrainRequestStreamConsumer.DrainAsync(
            token => Connection.ExecuteStreamAsync(signed, token),
            Fixture.Cluster.ServiceProvider.GetRequiredService<
                Serializer<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>>(),
            requestId, Fixture.Clock, Fixture.RoutingOptions, bounded.Token);
    }

    internal EntityRef Reference(string documentId)
        => new(Fixture.Database.Partition, ConnectionNativeProtocol.Collection, documentId);

    internal DocumentResult? Stored(string documentId)
        => Fixture.Database.Database.GetDocument(ConnectionNativeProtocol.Root, Reference(documentId));

    internal async Task AssertActivationCountAsync(int expected, CancellationToken cancellationToken)
    {
        var management = Fixture.Cluster.Client.GetGrain<IManagementGrain>(0);
        var actual = await management.GetActiveGrains(GrainType.Create(GrainRoutingProtocol.RequestAlias),
            cancellationToken);
        await Assert.That(actual.Count).IsEqualTo(expected);
        if (expected == ConnectionNativeProtocol.OneConnection)
        { await Assert.That(actual.Contains(((GrainReference)Connection).GrainId)).IsTrue(); }
    }

    internal async Task CloseAsync(CancellationToken cancellationToken)
    {
        var failures = new List<Exception>();
        await KeyLoad.Server.ServerFailureObserver.ObserveAsync(calls.StopAsync, failures);
        await KeyLoad.Server.ServerFailureObserver.ObserveAsync(
            () => calls.JoinAsync(failures, cancellationToken), failures);
        await KeyLoad.Server.ServerFailureObserver.ObserveAsync(
            () => Observation.JoinAsync(failures, cancellationToken), failures);
        KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
        await Connection.CloseAsync(Fixture.Codec.CreateConnectionClose(Fixture.ConnectionId), cancellationToken);
        closed = true;
        var management = Fixture.Cluster.Client.GetGrain<IManagementGrain>(0);
        while ((await management.GetActiveGrains(GrainType.Create(GrainRoutingProtocol.RequestAlias),
            cancellationToken)).Count != ConnectionNativeProtocol.AbsentActivations)
        { await Task.Delay(Timing.PollInterval, cancellationToken); }
        await AssertActivationCountAsync(ConnectionNativeProtocol.AbsentActivations, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        var failures = new List<Exception>();
        using var timeout = new CancellationTokenSource(Timing.ShutdownTimeout, TimeProvider.System);
        await KeyLoad.Server.ServerFailureObserver.ObserveAsync(calls.StopAsync, failures);
        Observation.ReleaseAll();
        await KeyLoad.Server.ServerFailureObserver.ObserveAsync(() => calls.JoinAsync(failures, timeout.Token), failures);
        await KeyLoad.Server.ServerFailureObserver.ObserveAsync(
            () => Observation.JoinAsync(failures, timeout.Token), failures);
        if (!closed && initialized)
        { await KeyLoad.Server.ServerFailureObserver.ObserveAsync(() => CloseAsync(timeout.Token), failures); }
        await KeyLoad.Server.ServerFailureObserver.ObserveAsync(() => Fixture.DisposeAsync().AsTask(), failures);
        try
        { calls.Dispose(); }
        catch (Exception failure) when (NativeCqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
        catch (Exception failure) when (!NativeCqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
        if (failures.Count > ConnectionNativeProtocol.EmptyFailures)
        {
            KeyLoad.Server.ServerFailureObserver.Observe(
                () => WriteTrace(ConnectionNativeProtocol.CleanupFailure), failures);
        }
        KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }

    private void WriteTrace(string phase)
    {
        calls.WriteTrace(phase);
        Observation.WriteTrace(phase);
    }
}
