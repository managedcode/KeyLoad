using KeyLoad.Clustering;
using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class ConnectionNativeScenario : IAsyncDisposable
{
    private readonly ConnectionNativeCalls calls = new();
    private readonly Guid collectionCreation = Guid.NewGuid();
    private bool closed;
    private bool initialized;
    private Guid lastClosedOwnerId;
    private const string ActivationWitness = "ConnectionNativeActivationCount";
    internal ConnectionNativeServerRuntime Fixture
    {
        get;
    }
    internal ConnectionOperationObservation Observation { get; } = new();
    internal NativeRuntimeTestOptions Timing { get; } = new();
    internal IConnectionGrain Connection => Fixture.Cluster.Client.GetGrain<IConnectionGrain>(Fixture.ConnectionId);

    internal ConnectionNativeScenario(GrainRoutingOptions? routing = null,
        IGrainRequestPurposeObserver? purposeObserver = null,
        Func<ConnectionOperationObservation, IGrainRequestPhaseObserver>? phaseObserver = null)
        => Fixture = new(routing, phaseObserver?.Invoke(Observation) ?? Observation, purposeObserver, Observation);

    internal static async Task RunAsync(Func<ConnectionNativeScenario, Task> execute,
        GrainRoutingOptions? routing = null, IGrainRequestPurposeObserver? purposeObserver = null,
        Func<ConnectionOperationObservation, IGrainRequestPhaseObserver>? phaseObserver = null)
    {
        var failures = new List<Exception>();
        ConnectionNativeServerRuntime? owner = null;
        try
        {
            await using var scenario = new ConnectionNativeScenario(routing, purposeObserver, phaseObserver);
            owner = scenario.Fixture;
            await KeyLoad.Server.ServerFailureObserver.ObserveAsync(() => execute(scenario), failures);
            if (failures.Count > ConnectionNativeProtocol.EmptyFailures)
            {
                KeyLoad.Server.ServerFailureObserver.Observe(
                    () => scenario.WriteTrace(ConnectionNativeProtocol.BodyFailure), failures);
                owner.RetainRoots();
            }
        }
        catch (Exception failure) when (NativeCqrsBoundaryErrors.IsNonFatal(failure))
        {
            failures.Add(failure);
        }
        catch (Exception failure) when (!NativeCqrsBoundaryErrors.IsNonFatal(failure))
        {
            failures.Add(failure);
        }
        if (owner is not null && failures.Count > ConnectionNativeProtocol.EmptyFailures)
        {
            KeyLoad.Server.ServerFailureObserver.Observe(
                () => owner.ReportFailure(failures), failures);
        }
        KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }

    internal async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await Fixture.InitializeAsync(cancellationToken);
        initialized = true;
        Fixture.Database.Configure(ConnectionNativeProtocol.Collection, ResourceKind.Collection, id: collectionCreation);
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
        => calls.Retain(requestId, () => InvokeCoreAsync(principal, requestId, commandId, signed, cancellationToken));

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
        => Fixture.Database.Reference(collectionCreation, ConnectionNativeProtocol.Collection, documentId);

    internal DocumentResult? Stored(string documentId)
        => Fixture.Database.Database.GetDocument(ConnectionNativeProtocol.Root, Reference(documentId));

    internal async Task AssertActivationCountAsync(int expected, CancellationToken cancellationToken)
    {
        var actual = await Fixture.ActiveConsumersAsync(cancellationToken);
        var failures = new List<Exception>();
        await KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            await Assert.That(actual.Count).IsEqualTo(expected);
            if (expected == ConnectionNativeProtocol.OneConnection)
            {
                await Assert.That(actual.Contains(((GrainReference)Connection).GrainId)).IsTrue();
            }
        }, failures);
        if (failures.Count > ConnectionNativeProtocol.EmptyFailures)
        {
            KeyLoad.Server.ServerFailureObserver.Observe(() => ConnectionNativeActivationCountWitness.Write(
                ActivationWitness, expected, actual.Count, actual.Contains(((GrainReference)Connection).GrainId),
                lastClosedOwnerId != Guid.Empty && actual.Contains(((GrainReference)
                    Fixture.Cluster.Client.GetGrain<IConnectionGrain>(lastClosedOwnerId)).GrainId),
                lastClosedOwnerId == Fixture.ConnectionId, closed), failures);
        }
        KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
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
        await CloseOwnerAsync(cancellationToken);
    }

    internal Task CloseOwnerAsync(CancellationToken cancellationToken)
        => CloseOwnerAsync(Connection, Fixture.ConnectionId, cancellationToken);

    internal async Task CloseOwnerAsync(IConnectionGrain owner, Guid ownerId, CancellationToken cancellationToken)
    {
        Fixture.RegisterConsumer(ownerId);
        await owner.CloseAsync(Fixture.Codec.CreateConnectionClose(ownerId), cancellationToken);
        lastClosedOwnerId = ownerId;
        if (ownerId == Fixture.ConnectionId)
        {
            closed = true;
        }
        while ((await Fixture.ActiveConsumersAsync(cancellationToken)).Count != ConnectionNativeProtocol.AbsentActivations)
        {
            await Task.Delay(Timing.PollInterval, cancellationToken);
        }
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
        {
            await KeyLoad.Server.ServerFailureObserver.ObserveAsync(() => CloseAsync(timeout.Token), failures);
        }
        if (failures.Count > ConnectionNativeProtocol.EmptyFailures)
        {
            Fixture.RetainRoots();
        }
        await KeyLoad.Server.ServerFailureObserver.ObserveAsync(() => Fixture.DisposeAsync().AsTask(), failures);
        try
        {
            calls.Dispose();
        }
        catch (Exception failure) when (NativeCqrsBoundaryErrors.IsNonFatal(failure))
        {
            failures.Add(failure);
        }
        catch (Exception failure) when (!NativeCqrsBoundaryErrors.IsNonFatal(failure))
        {
            failures.Add(failure);
        }
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
