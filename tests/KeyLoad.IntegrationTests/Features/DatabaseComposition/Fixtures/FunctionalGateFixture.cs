using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using KeyLoad.AppHost.Features.ClusterReplication;
using KeyLoad.AppHost.Features.TestInfrastructure;
using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.Server;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

/// <summary>Owns one real development voter or the same real three-voter functional scenario through Aspire.</summary>
internal sealed partial class FunctionalGateFixture : IAsyncDisposable
{
    private const int SingleVoter = 1;
    private const int ThreeVoters = 3;
    private readonly Lock disposalGate = new();
    private readonly bool coldReopen;
    private IDistributedApplicationTestingBuilder? builder;
    private LocalRf3ImageTestSession? imageSession;
    private Task? disposal;
    private ClusterFixtureDiagnostics? diagnostics;
    private string[] diagnosticSecrets = [];
    private Action<string>? saveStartupEvidence;
    private bool started;
    internal ContainerRuntimeControl? ColdRuntime
    {
        get; private set;
    }
    internal DistributedApplication App { get; private set; } = null!;
    internal string AdminKey { get; private set; } = string.Empty;
    internal string[] NodeNames
    {
        get;
    }
    internal string Root { get; } = Path.Combine(Path.GetTempPath(), "keyload-functional-" + Guid.NewGuid().ToString("N"));
    internal bool PreserveEvidence
    {
        get; set;
    }

    private FunctionalGateFixture(int nodes, bool coldReopen, bool exclusiveOwnerContention)
    {
        this.coldReopen = coldReopen;
        fileContenders = new(exclusiveOwnerContention);
        NodeNames = Enumerable.Range(SingleVoter, nodes).Select(ClusterFixtureProtocol.NodeName).ToArray();
    }

    internal static Task<FunctionalGateFixture> StartAsync(int nodes, CancellationToken token)
        => StartCoreAsync(nodes, false, false, token);

    internal static Task<FunctionalGateFixture> StartColdAsync(int nodes, CancellationToken token)
        => StartCoreAsync(nodes, true, false, token);

    private static async Task<FunctionalGateFixture> StartCoreAsync(int nodes, bool coldReopen, bool exclusiveOwnerContention, CancellationToken token)
    {
        if (nodes is not (SingleVoter or ThreeVoters))
        {
            throw new ArgumentOutOfRangeException(nameof(nodes));
        }
        token.ThrowIfCancellationRequested();
        var owner = new FunctionalGateFixture(nodes, coldReopen, exclusiveOwnerContention);
        return await FunctionalGateFixtureStartup.InitializeAsync(owner,
            () => owner.InitializeAsync(nodes, token)).ConfigureAwait(false);
    }

    private async Task InitializeAsync(int nodes, CancellationToken token)
    {
        using var preparationBound = new CancellationTokenSource(TimeSpan.FromMinutes(15), TimeProvider.System);
        using var preparation = CancellationTokenSource.CreateLinkedTokenSource(token, preparationBound.Token);
        imageSession = await LocalRf3ImageTestSession.StartIfSelectedAsync(preparation.Token).ConfigureAwait(false);
        var selection = imageSession?.Selection;
        if (selection is null && LocalRf3ImageSelection.Read() is not null)
        {
            throw new InvalidOperationException("The functional fixture requires its own selected local image preparation session.");
        }
        var arguments = FunctionalGateFixtureStartup.Arguments(Root, nodes, coldReopen,
            fileContenders.Enabled, selection);
        using var startupBound = new CancellationTokenSource(TimeSpan.FromMinutes(2), TimeProvider.System);
        using var startup = CancellationTokenSource.CreateLinkedTokenSource(token, startupBound.Token);
        builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.KeyLoad_AppHost>(arguments.ToArray(), startup.Token)
            .ConfigureAwait(false);
        ClusterFixtureComposition.ConfigureLogging(builder);
        var containers = FunctionalGateImageModel.ReadContainerNames(builder, NodeNames);
        App = await builder.BuildAsync(startup.Token).ConfigureAwait(false);
        var profile = ClusterFixturePhysicalShardIdentity.ReadProfile(Root);
        AdminKey = profile.AdminKey;
        diagnosticSecrets = [profile.SigningKey, profile.PeerSecret, profile.AdminKey];
        saveStartupEvidence = App.Services.GetRequiredService<ClusterFixtureStartupEvidence>().Save;
        diagnostics = new(App);
        diagnostics.Start(App.Services.GetRequiredService<ResourceLoggerService>());
        var repository = ClusterFixtureDiagnostics.FindRepositoryRoot().FullName;
        ColdRuntime = new(App, containers, repository, selection);
        var identity = selection is null ? null
            : await LocalRf3ImageIdentity.VerifyBeforeStartAsync(App, repository, selection, NodeNames, startup.Token)
                .ConfigureAwait(false);
        if (identity is null)
        {
            await FunctionalGateImageModel.VerifyPublishedImageAsync(App, NodeNames, startup.Token).ConfigureAwait(false);
        }
        await App.StartAsync(startup.Token).ConfigureAwait(false);
        await AspireStartupReadiness.WaitForHealthyAsync(App, NodeNames, startup.Token).ConfigureAwait(false);
        if (identity is not null)
        {
            await LocalRf3ImageIdentity.VerifyStartedContainersAsync(identity, containers, NodeNames, startup.Token)
                .ConfigureAwait(false);
        }
        started = true;
        PhysicalFiles = await FunctionalGatePhysicalFileAssertions.CaptureLiveAsync(this, startup.Token).ConfigureAwait(false);
    }
}
