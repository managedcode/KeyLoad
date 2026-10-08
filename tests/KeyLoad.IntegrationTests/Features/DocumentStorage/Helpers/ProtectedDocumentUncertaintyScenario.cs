using System.Security.Cryptography;
using System.Text;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Orleans;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.DocumentStorage;

/// <summary>Owns genuine SDK producers and the existing bounded private probe session.</summary>
internal sealed class ProtectedDocumentUncertaintyScenario(TwoRf3MembershipWave wave) : IAsyncDisposable
{
    private readonly List<HttpClient> connections = [];
    private readonly List<Task> producers = [];
    private readonly List<Guid> arms = [];
    private ReplicaSiloDiscovery[] discovery = [];
    private CancellationTokenSource? caller;
    private McpOfficialClient? official;

    internal KeyLoadClient Sdk(string node, string secret)
    {
        var http = McpCallerHttp.Create(wave.Application, node);
        connections.Add(http);
        return new(http, secret, IntegrationClientOptions.Execution());
    }

    internal Task<T> Own<T>(Task<T> task) { producers.Add(task); return task; }
    internal Guid Arm(string principal, Guid command, RequestCqrsProbePhase phase, RequestCqrsProbeAction action)
    {
        var arm = wave.QueryControls.WriteArm(principal, command, null, phase, action);
        arms.Add(arm);
        return arm;
    }
    internal IReadOnlyList<ReplicaSiloDiscovery> Discovery => discovery;

    internal async Task RunAsync(CancellationToken token)
    {
        caller = CancellationTokenSource.CreateLinkedTokenSource(token);
        await PhysicalOwnerRegistrationRf3Observation.WaitAsync(wave.Application, caller.Token).ConfigureAwait(false);
        discovery = new ReplicaSiloDiscovery[3];
        for (var index = 0; index < discovery.Length; index++)
        {
            discovery[index] = await RequestCqrsRf3SignedDiscovery.ReadForProfileAsync(wave.Application,
            RequestCqrsRf3Protocol.NodeName(index), wave.Profile, caller.Token).ConfigureAwait(false);
        }
        var administrator = Sdk(TwoRf3MembershipProtocol.Node1, wave.Profile.AdminKey);
        var target = Sdk(TwoRf3MembershipProtocol.Node4, wave.Profile.AdminKey);
        var identity = await PersistAsync(administrator, caller.Token).ConfigureAwait(false);
        var source = Sdk(TwoRf3MembershipProtocol.Node1, identity.Secret);
        var seed = await ProtectedDocumentRf3Seed.CreateAsync(wave, administrator, caller.Token).ConfigureAwait(false);
        var denied = Sdk(TwoRf3MembershipProtocol.Node3, seed.DeniedSecret);
        var command = new CommandRequest(new("bbdf73bb-5d26-43a0-b593-19fbef11bc91"), seed.Partition,
            [new PutDocument("protected-documents", "one",
                "{\"title\":\"uncertain Київ\",\"secret\":\"uncertain-private-canary\"}", 1, ExplicitReplacement: true)]);
        await ProtectedDocumentUncertaintyOracle.IssuedAsync(command, seed.Partition).ConfigureAwait(false);
        var before = await McpCallerAssertions.SdkSuccessAsync(await target.StatusAsync(caller.Token));
        await Assert.That(before.Incarnation).IsEqualTo(seed.Peer.Target.Incarnation);
        var expected = await ProtectedDocumentUncertaintyFlow.RunAsync(this, wave, source, target, denied, identity.Id,
            seed, command, before.Applied, caller.Token).ConfigureAwait(false);
        await ProtectedDocumentRf3CommandOutcome.ExecuteAsync(wave, source, target, seed, command,
            expected, caller.Token, identity.Id).ConfigureAwait(false);
        official = await McpOfficialClient.ConnectAsync(wave.Application, TwoRf3MembershipProtocol.Node2,
            identity.Secret, caller.Token).ConfigureAwait(false);
        await ProtectedDocumentUncertaintyOracle.ReplayAsync(source, target, official, command, expected, caller.Token);
        await ProtectedDocumentUncertaintyOracle.DocumentAsync(source, seed, caller.Token, expected.Token);
        await ProtectedDocumentUncertaintyOracle.DocumentMcpAsync(official, seed, expected.Token, caller.Token);
        foreach (var node in TwoRf3MembershipProtocol.Nodes)
        {
            await wave.RemoteRuntime.KillAsync(node, "uncertain-doc-all-voter-reopen", caller.Token).ConfigureAwait(false);
            await wave.RemoteRuntime.RestartAsync(node, caller.Token).ConfigureAwait(false);
            await wave.Application.ResourceNotifications.WaitForResourceHealthyAsync(node, caller.Token).ConfigureAwait(false);
        }
        await ProtectedDocumentUncertaintyOracle.ReplayAsync(source, target, official, command, expected, caller.Token);
        await ProtectedDocumentUncertaintyOracle.DocumentAsync(source, seed, caller.Token, expected.Token);
        await ProtectedDocumentUncertaintyOracle.DocumentMcpAsync(official, seed, expected.Token, caller.Token);
    }

    private static async Task<(string Id, string Secret)> PersistAsync(KeyLoadClient administrator, CancellationToken token)
    {
        const string id = "c1-probe-protected-uncertain-admin";
        var key = "uncertain-key-" + Guid.NewGuid().ToString("N");
        var secret = key + "." + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        _ = await McpCallerAssertions.SdkSuccessAsync(await administrator.ConfigurePrincipalAsync(Guid.NewGuid(),
            new(id, "protected-tenant", [], []) { ClusterAdministrator = true }, token));
        _ = await McpCallerAssertions.SdkSuccessAsync(await administrator.ConfigureApiKeyAsync(Guid.NewGuid(),
            new(key, id, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret)))), token));
        return (id, secret);
    }

    public async ValueTask DisposeAsync()
    {
        var failures = new List<Exception>();
        using var cleanup = new CancellationTokenSource(TwoRf3MembershipProtocol.CleanupDeadline, TimeProvider.System);
        if (caller is not null)
        { await ServerFailureObserver.ObserveAsync(caller.CancelAsync, failures).ConfigureAwait(false); }
        wave.QueryControls.StopAdmission();
        if (discovery.Length == 3)
        { await ServerFailureObserver.ObserveAsync(() => wave.QueryControls.ReleaseOpenArmsAsync(discovery, cleanup.Token), failures); }
        foreach (var producer in producers)
        { await ServerFailureObserver.ObserveAsync(() => producer, failures).ConfigureAwait(false); }
        foreach (var arm in arms)
        { await JoinArmAsync(arm, failures, cleanup.Token).ConfigureAwait(false); }
        if (official is not null)
        { await ServerFailureObserver.ObserveAsync(() => official.DisposeAsync().AsTask(), failures).ConfigureAwait(false); }
        foreach (var connection in connections)
        { ServerFailureObserver.Observe(connection.Dispose, failures); }
        caller?.Dispose();
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private async Task JoinArmAsync(Guid arm, List<Exception> failures, CancellationToken token)
    {
        var state = wave.QueryControls.ArmFor(arm);
        if (state.RequestId is null || state.ProducerDisposedSeen)
        { return; }
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            _ = await wave.QueryControls.WaitForMarkerAsync(arm, RequestCqrsProbePhase.ProducerDisposed,
                RequestCqrsProbeOutcome.Observed, discovery, token).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
    }
}
