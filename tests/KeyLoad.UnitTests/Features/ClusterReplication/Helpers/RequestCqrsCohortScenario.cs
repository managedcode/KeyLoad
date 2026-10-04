using System.Collections.Immutable;
using System.Security.Cryptography;
using KeyLoad.Orleans;
using KeyLoad.Replication;

namespace KeyLoad.UnitTests.Features.ClusterReplication;

internal sealed class RequestCqrsCohortScenario : IAsyncDisposable
{
    internal const string LocalVoter = "http://cohort-local:8080";
    internal const string FirstRemote = "http://cohort-remote-one:8080";
    internal const string SecondRemote = "http://cohort-remote-two:8080";
    internal const string ClusterId = "request-cqrs-cohort-unit";
    internal static readonly TimeSpan RpcTimeout = TimeSpan.FromSeconds(1);
    internal static readonly TimeSpan ConnectTimeout = TimeSpan.FromMilliseconds(200);
    internal static readonly TimeSpan LowerElectionTimeout = TimeSpan.FromMilliseconds(1_500);
    internal static readonly TimeSpan UpperElectionTimeout = TimeSpan.FromSeconds(2);
    private readonly RequestCqrsCohortRuntimeFixture runtime;
    private readonly byte[] secret = RandomNumberGenerator.GetBytes(ReplicaTransportProtocol.SecretBytes);
    private readonly List<RequestCqrsCohortEndpoint> endpoints = [];
    private readonly List<ReplicaEnvelopeAuthenticator> authenticators = [];
    private readonly Dictionary<string, ReplicaEnvelopeAuthenticator> serverAuthenticators = new(StringComparer.Ordinal);
    private ReplicaSiloDiscoveryClient? client;

    private RequestCqrsCohortScenario(RequestCqrsCohortRuntimeFixture runtime)
    {
        this.runtime = runtime;
    }

    internal ReplicaConfiguration Configuration { get; private set; } = null!;
    internal ReplicaPeerOptions Options { get; private set; } = null!;
    internal ReplicaSiloDiscoveryClient Client => client ?? throw new InvalidOperationException("The discovery client is not initialized.");
    internal RequestCqrsCohortEndpoint RemoteOne => Find(FirstRemote);
    internal RequestCqrsCohortEndpoint RemoteTwo => Find(SecondRemote);

    internal static async Task<RequestCqrsCohortScenario> StartAsync(
        RequestCqrsCohortRuntimeFixture runtime, CancellationToken token)
    {
        var scenario = new RequestCqrsCohortScenario(runtime);
        try
        {
            await scenario.StartCoreAsync(token);
            return scenario;
        }
        catch (Exception startupFailure)
        {
            try
            {
                await scenario.DisposeAsync();
            }
            catch (Exception cleanupFailure)
            {
                throw new AggregateException(startupFailure, cleanupFailure);
            }

            throw;
        }
    }

    internal ReplicaSiloDiscovery Discovery(string voter, int addressIndex,
        int requestVersion = GrainRoutingProtocol.RequestInterfaceVersion,
        int envelopeVersion = ReplicaTransportProtocol.Version)
        => new(voter, Options.ClusterId, Configuration.Incarnation, runtime.RuntimeAddress(addressIndex), true,
            requestVersion, envelopeVersion);

    internal void PublishCompatibleRemoteOne(int addressIndex)
        => RemoteOne.SetDiscovery(Authenticator(FirstRemote),
            Discovery(FirstRemote, addressIndex));

    internal void PublishRemoteOne(int addressIndex, int requestVersion, int envelopeVersion)
        => RemoteOne.SetDiscovery(Authenticator(FirstRemote),
            Discovery(FirstRemote, addressIndex, requestVersion, envelopeVersion));

    internal void PublishRemoteTwo(int addressIndex, int requestVersion, int envelopeVersion)
        => RemoteTwo.SetDiscovery(Authenticator(SecondRemote),
            Discovery(SecondRemote, addressIndex, requestVersion, envelopeVersion));

    internal void PublishRemoteOneRecord(ReplicaSiloDiscovery discovery)
        => RemoteOne.SetDiscovery(Authenticator(FirstRemote), discovery);

    internal void TamperRemoteOnePayload() => RemoteOne.SetTamperedPayload();

    private async Task StartCoreAsync(CancellationToken token)
    {
        var local = await RequestCqrsCohortEndpoint.StartAsync(LocalVoter, token);
        endpoints.Add(local);
        var remoteOne = await RequestCqrsCohortEndpoint.StartAsync(FirstRemote, token);
        endpoints.Add(remoteOne);
        var remoteTwo = await RequestCqrsCohortEndpoint.StartAsync(SecondRemote, token);
        endpoints.Add(remoteTwo);
        Configuration = new(LocalVoter, ImmutableArray.Create(LocalVoter, FirstRemote, SecondRemote),
            Path.Combine(Path.GetTempPath(), "keyload-request-cqrs-cohort", Guid.NewGuid().ToString("N")),
            Guid.NewGuid())
        {
            RpcTimeout = RpcTimeout,
            HeartbeatInterval = TimeSpan.FromMilliseconds(100),
            LowerElectionTimeout = LowerElectionTimeout,
            UpperElectionTimeout = UpperElectionTimeout
        };
        Options = new(new(StringComparer.Ordinal)
        {
            [LocalVoter] = local.Origin,
            [FirstRemote] = remoteOne.Origin,
            [SecondRemote] = remoteTwo.Origin
        }, secret, ClusterId)
        { ConnectTimeout = ConnectTimeout };
        Configuration.Validate();
        Options.Validate(Configuration);
        ConfigureSigner(local);
        ConfigureSigner(remoteOne);
        ConfigureSigner(remoteTwo);
        PublishCompatibleRemoteOne(1);
        remoteTwo.SetDiscovery(Authenticator(SecondRemote), Discovery(SecondRemote, 2));
        var localState = new ReplicaSiloDiscoveryState(Configuration, Options, runtime.LocalSilo);
        localState.MarkTransportReady();
        var localAuthenticator = new ReplicaEnvelopeAuthenticator(Configuration, Options, localState, TimeProvider.System);
        authenticators.Add(localAuthenticator);
        client = new ReplicaSiloDiscoveryClient(Configuration, Options, localState, localAuthenticator, TimeProvider.System);
    }

    private void ConfigureSigner(RequestCqrsCohortEndpoint endpoint)
    {
        var remoteConfiguration = Configuration with { LocalId = endpoint.VoterId };
        var remoteState = new ReplicaSiloDiscoveryState(remoteConfiguration, Options, runtime.LocalSilo);
        var authentication = new ReplicaEnvelopeAuthenticator(remoteConfiguration, Options, remoteState, TimeProvider.System);
        authenticators.Add(authentication);
        serverAuthenticators.Add(endpoint.VoterId, authentication);
        endpoint.SetDiscovery(authentication,
            Discovery(endpoint.VoterId, endpoint.VoterId == LocalVoter ? 0 : endpoint.VoterId == FirstRemote ? 1 : 2));
    }

    private ReplicaEnvelopeAuthenticator Authenticator(string voter)
        => serverAuthenticators[voter];

    private RequestCqrsCohortEndpoint Find(string voter)
        => endpoints.Single(endpoint => endpoint.VoterId == voter);

    public async ValueTask DisposeAsync()
    {
        var failures = new List<Exception>();
        if (client is not null)
        {
            try
            {
                await client.DisposeAsync();
            }
            catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
            {
                failures.Add(error);
            }
            catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
            {
                failures.Add(error);
            }
            finally
            {
                client = null;
            }
        }

        foreach (var authentication in authenticators)
        {
            RequestCqrsCohortCleanup.CaptureSync(authentication.Dispose, failures);
        }
        authenticators.Clear();
        serverAuthenticators.Clear();
        foreach (var endpoint in endpoints)
        {
            await RequestCqrsCohortCleanup.CaptureAsync(() => endpoint.DisposeAsync().AsTask(), failures);
        }
        endpoints.Clear();
        CryptographicOperations.ZeroMemory(secret);
        RequestCqrsCohortCleanup.ThrowIfAny(failures);
    }
}
