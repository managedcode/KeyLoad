using KeyLoad.Clustering;

namespace KeyLoad.FunctionalTests.OrleansExecution;

internal sealed class ConnectionScenario : IAsyncDisposable
{
    private readonly ConnectionCalls calls = new();
    private readonly Guid collectionCreation = Guid.NewGuid();
    private bool closed;
    private bool initialized;
    private Guid lastClosedOwnerId;
    private const string ActivationWitness = "ConnectionActivationCount";
    internal ConnectionServerRuntime Fixture
    {
        get;
    }
    internal ConnectionOperationObservation Observation { get; } = new();
    internal RuntimeTestOptions Timing { get; } = new();
    internal IConnectionGrain Connection => Fixture.Cluster.Client.GetGrain<IConnectionGrain>(Fixture.ConnectionId);

    internal ConnectionScenario(GrainRoutingOptions? routing = null,
        IGrainRequestPurposeObserver? purposeObserver = null,
        Func<ConnectionOperationObservation, IGrainRequestPhaseObserver>? phaseObserver = null)
        => Fixture = new(routing, phaseObserver?.Invoke(Observation) ?? Observation, purposeObserver, Observation);

    internal static async Task RunAsync(Func<ConnectionScenario, Task> execute,
        GrainRoutingOptions? routing = null, IGrainRequestPurposeObserver? purposeObserver = null,
        Func<ConnectionOperationObservation, IGrainRequestPhaseObserver>? phaseObserver = null)
    {
        var failures = new List<Exception>();
        ConnectionServerRuntime? owner = null;
        try
        {
            await using var scenario = new ConnectionScenario(routing, purposeObserver, phaseObserver);
            owner = scenario.Fixture;
            await KeyLoad.Server.ServerFailureObserver.ObserveAsync(() => execute(scenario), failures);
            if (failures.Count > ConnectionProtocol.EmptyFailures)
            {
                KeyLoad.Server.ServerFailureObserver.Observe(
                    () => scenario.WriteTrace(ConnectionProtocol.BodyFailure), failures);
                owner.RetainRoots();
            }
        }
        catch (Exception failure) when (CqrsBoundaryErrors.IsNonFatal(failure))
        {
            failures.Add(failure);
        }
        catch (Exception failure) when (!CqrsBoundaryErrors.IsNonFatal(failure))
        {
            failures.Add(failure);
        }
        if (owner is not null && failures.Count > ConnectionProtocol.EmptyFailures)
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
        Fixture.Database.Configure(ConnectionProtocol.Collection, ResourceKind.Collection, id: collectionCreation);
        await AssertActivationCountAsync(ConnectionProtocol.AbsentActivations, cancellationToken);
    }

    internal PrincipalRecord Principal()
    {
        var record = new PrincipalRecord(ConnectionProtocol.SubjectPrefix + Guid.NewGuid().ToString("N"),
            Fixture.Database.Partition.TenantId,
            [new ScopeGrant(Fixture.Database.Partition.DatabaseId, ConnectionProtocol.Collection,
                Capability.DocumentsRead | Capability.DocumentsWrite)], []);
        return Fixture.Database.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(record))
            .Get<PrincipalRecord>();
    }

    internal CommandRequest Command(string documentId, string json = ConnectionProtocol.FirstJson,
        long? expectedRevision = null, Guid? commandId = null)
        => new(commandId ?? Guid.NewGuid(), Fixture.Database.Partition,
            [new PutDocument(ConnectionProtocol.Collection, documentId, json, expectedRevision)]);

    internal Task<GrainOperationReply> CommandAsync(PrincipalRecord principal, Guid requestId,
        CommandRequest command, CancellationToken cancellationToken)
    {
        var envelope = Fixture.Codec.CreateCommand(requestId, principal.Id, OperationKind.Batch,
            command.CommandId, OrleansSerialization.Serialize(command));
        return InvokeAsync(principal, requestId, command.CommandId, envelope, cancellationToken);
    }

    internal Task<GrainOperationReply> ReadAsync(PrincipalRecord principal, Guid requestId,
        string documentId, CancellationToken cancellationToken)
    {
        var envelope = Fixture.Codec.CreateRead(requestId, principal.Id, GrainReadKind.Document,
            OrleansSerialization.Serialize(new GetDocumentRequest(Reference(documentId))));
        return InvokeAsync(principal, requestId, Guid.Empty, envelope, cancellationToken);
    }

    internal Task<GrainOperationReply> InvokeAsync(PrincipalRecord principal, Guid requestId,
        Guid commandId, GrainRequestEnvelope envelope, CancellationToken cancellationToken)
        => calls.Retain(requestId, () => InvokeCoreAsync(principal, requestId, commandId, envelope, cancellationToken));

    private async Task<GrainOperationReply> InvokeCoreAsync(PrincipalRecord principal, Guid requestId,
        Guid commandId, GrainRequestEnvelope envelope, CancellationToken cancellationToken)
    {
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, calls.Token);
        using var identity = new GrainRequestIdentityScope(Fixture.Cluster.ServiceProvider, principal,
            requestId, commandId, bounded.Token, connectionId: Fixture.ConnectionId);
        return await Connection.ExecuteAsync(envelope, bounded.Token);
    }

    internal EntityRef Reference(string documentId)
        => Fixture.Database.Reference(collectionCreation, ConnectionProtocol.Collection, documentId);

    internal DocumentResult? Stored(string documentId)
        => Fixture.Database.Database.GetDocument(ConnectionProtocol.Root, Reference(documentId));

    internal async Task AssertActivationCountAsync(int expected, CancellationToken cancellationToken)
    {
        var actual = await Fixture.ActiveConsumersAsync(cancellationToken);
        var failures = new List<Exception>();
        await KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            await Assert.That(actual.Count).IsEqualTo(expected);
            if (expected == ConnectionProtocol.OneConnection)
            {
                await Assert.That(actual.Contains(((GrainReference)Connection).GrainId)).IsTrue();
            }
        }, failures);
        if (failures.Count > ConnectionProtocol.EmptyFailures)
        {
            KeyLoad.Server.ServerFailureObserver.Observe(() => ConnectionActivationCountWitness.Write(
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
        while ((await Fixture.ActiveConsumersAsync(cancellationToken)).Count != ConnectionProtocol.AbsentActivations)
        {
            await Task.Delay(Timing.PollInterval, cancellationToken);
        }
        await AssertActivationCountAsync(ConnectionProtocol.AbsentActivations, cancellationToken);
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
        if (failures.Count > ConnectionProtocol.EmptyFailures)
        {
            Fixture.RetainRoots();
        }
        await KeyLoad.Server.ServerFailureObserver.ObserveAsync(() => Fixture.DisposeAsync().AsTask(), failures);
        try
        {
            calls.Dispose();
        }
        catch (Exception failure) when (CqrsBoundaryErrors.IsNonFatal(failure))
        {
            failures.Add(failure);
        }
        catch (Exception failure) when (!CqrsBoundaryErrors.IsNonFatal(failure))
        {
            failures.Add(failure);
        }
        if (failures.Count > ConnectionProtocol.EmptyFailures)
        {
            KeyLoad.Server.ServerFailureObserver.Observe(
                () => WriteTrace(ConnectionProtocol.CleanupFailure), failures);
        }
        KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }

    private void WriteTrace(string phase)
    {
        calls.WriteTrace(phase);
        Observation.WriteTrace(phase);
    }
}
