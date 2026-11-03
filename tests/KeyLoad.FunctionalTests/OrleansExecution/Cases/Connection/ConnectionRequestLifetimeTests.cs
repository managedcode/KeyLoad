using KeyLoad.Clustering;
using KeyLoad.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.Configuration;

namespace KeyLoad.FunctionalTests.OrleansExecution;

internal sealed class ConnectionRequestLifetimeTests
{
    [Test]
    public Task AcCrs051ActiveRequestOutlivesConnectionIdleTimeoutUntilCallerCancels()
        => ConnectionScenario.RunAsync(async scenario =>
        {
            using var lifetime = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            var token = lifetime.Token;
            await scenario.InitializeAsync(token);
            var principal = scenario.Principal();
            var firstId = Guid.NewGuid();
            var held = scenario.Observation.Hold(firstId);
            var command = scenario.Command("caller-owned-lifetime");
            using var caller = CancellationTokenSource.CreateLinkedTokenSource(token);
            var first = scenario.CommandAsync(principal, firstId, command, caller.Token);
            try
            {
                await held.Arrived.Task.WaitAsync(token);
                await Task.Delay(TimeSpan.FromSeconds(125), token);
                await Assert.That(first.IsCompleted).IsFalse();
                await ConnectionAssertions.MissingOutcomeAsync(scenario, principal, command,
                    "caller-owned-lifetime");
                var followingId = Guid.NewGuid();
                var following = scenario.Command("healthy-while-active");
                var reply = await scenario.CommandAsync(principal, followingId, following, token);
                await Assert.That(reply.Error).IsNull();
                await ConnectionAssertions.StoredAsync(scenario, "healthy-while-active", 1, ConnectionProtocol.FirstJson);
                await caller.CancelAsync();
                await ConnectionAssertions.CancelledAsync(first);
                await ConnectionAssertions.MissingOutcomeAsync(scenario, principal, command,
                    "caller-owned-lifetime");
                await ConnectionAssertions.SameOwnerAsync(scenario, firstId, followingId, token);
                await scenario.CloseAsync(token);
            }
            finally
            {
                await caller.CancelAsync();
                held.Release.TrySetResult();
            }
        });

    [Test]
    public async Task AcCrs051RemoteOwnerOutlivesSiloResponseTimeoutAndDeactivates()
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current!.Execution.CancellationToken);
        lifetime.CancelAfter(TimeSpan.FromMinutes(2));
        var token = lifetime.Token;
        var responseTimeout = TimeSpan.FromSeconds(2);
        var requestId = Guid.NewGuid();
        var gate = new RemoteOwnerResponseGate(requestId);
        await using var cluster = await RequestCqrsHostedCluster.CreateAsync(token, observer: gate,
            configureServices: (index, services, _) =>
            {
                if (index == 0)
                {
                    services.Configure<SiloMessagingOptions>(options => options.ResponseTimeout = responseTimeout);
                }
            });
        var source = cluster.Nodes[0];
        var owner = cluster.Nodes[1].Services.GetRequiredService<ILocalSiloDetails>().SiloAddress;
        var client = source.Services.GetRequiredService<SiloDatabaseClient>();
        var configured = source.Services.GetRequiredService<IOptions<SiloMessagingOptions>>().Value.ResponseTimeout;
        await Assert.That(configured).IsEqualTo(responseTimeout);
        await Assert.That(client.LocalSiloAddress).IsNotEqualTo(owner);
        var principal = source.Database.Store.Read(view => view.GetRecord<PrincipalRecord>(KeySpace.Principal(RequestCqrsHostedProfile.Root)))
            ?? throw new InvalidOperationException("The persisted root principal is missing.");
        await AssertPrincipalAsync(await ReadRemotePrincipalAsync(source, client, owner, principal, Guid.NewGuid(), token), principal);

        var original = ReadRemotePrincipalAsync(source, client, owner, principal, requestId, token);
        try
        {
            var admitted = await gate.Admitted.Task.WaitAsync(token);
            await Assert.That(admitted.Silo).IsEqualTo(owner);
            await Task.Delay(responseTimeout * 2, token);
            await Assert.That(original.IsCompleted).IsFalse();
            await Assert.That(admitted.Deactivated.IsCompleted).IsFalse();
        }
        finally
        {
            gate.Release.TrySetResult();
            await ((Task)original).WaitAsync(token).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            if (gate.Admitted.Task.IsCompletedSuccessfully)
            {
                var admitted = await gate.Admitted.Task;
                await admitted.Deactivated.WaitAsync(token);
            }
        }

        await AssertPrincipalAsync(await original, principal);
        await AssertPrincipalAsync(await ReadRemotePrincipalAsync(source, client, owner, principal, Guid.NewGuid(), token), principal);
    }

    private static async Task<GrainOperationReply> ReadRemotePrincipalAsync(RequestCqrsHostedRuntimeFixture source,
        SiloDatabaseClient client, SiloAddress owner, PrincipalRecord principal, Guid requestId, CancellationToken token)
    {
        var envelope = source.Codec.CreateRead(requestId, principal.Id, GrainReadKind.CurrentPrincipal, GrainContracts.NoDtoMarker);
        using var identity = new GrainRequestIdentityScope(source.Services, principal, requestId, Guid.Empty, token,
            connectionId: source.ConnectionId);
        return await client.ExecuteAsync(owner, envelope, token);
    }

    private static async Task AssertPrincipalAsync(GrainOperationReply reply, PrincipalRecord principal)
    {
        await Assert.That(reply.Error).IsNull();
        await Assert.That(reply.Result?.Value is PrincipalRecord observed && observed.Id == principal.Id).IsTrue();
    }

    private sealed class RemoteOwnerResponseGate(Guid requestId) : IGrainRequestPhaseObserver
    {
        internal TaskCompletionSource<(SiloAddress? Silo, Task Deactivated)> Admitted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask ObserveAsync(GrainRequestProbeIdentity identity, GrainRequestPhase phase,
            IGrainContext context, CancellationToken cancellationToken)
        {
            if (identity.RequestId != requestId || phase != GrainRequestPhase.AuthorizationReload)
            {
                return;
            }
            if (context.GrainInstance is not RequestExecutionGrain)
            {
                throw new InvalidOperationException("The operation did not enter the ordinary remote request grain.");
            }
            Admitted.TrySetResult((context.Address.SiloAddress, context.Deactivated));
            await Release.Task.WaitAsync(cancellationToken);
        }

        public void ProducerDisposed(GrainRequestProbeIdentity identity, IGrainContext context)
        {
        }
    }
}
