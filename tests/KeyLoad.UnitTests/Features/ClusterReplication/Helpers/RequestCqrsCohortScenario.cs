using KeyLoad.Orleans;
using KeyLoad.Replication;
using KeyLoad.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.UnitTests.Features.ClusterReplication;

internal sealed class RequestCqrsCohortScenario : IAsyncDisposable
{
    internal const string LocalVoter = "Primary";
    internal const string FirstRemote = "Secondary_1";
    internal const string SecondRemote = "Secondary_2";
    internal const string ClusterId = "request-cqrs-cohort-unit";
    internal static readonly TimeSpan RpcTimeout = TimeSpan.FromSeconds(1);
    internal static readonly TimeSpan ConnectTimeout = TimeSpan.FromMilliseconds(200);
    internal static readonly TimeSpan LowerElectionTimeout = TimeSpan.FromMilliseconds(1_500);
    internal static readonly TimeSpan UpperElectionTimeout = TimeSpan.FromSeconds(2);
    private readonly RequestCqrsCohortRuntimeFixture runtime;
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
    internal string RuntimeAddress(int index) => runtime.RuntimeAddress(index);

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
        int envelopeVersion = ReplicaTransportProtocol.Version,
        int runtimeJournalReaderContract = StoreReaderContract.RuntimeJournal)
        => new(voter, Options.ClusterId, Configuration.Incarnation, runtime.RuntimeAddress(addressIndex), true,
            requestVersion, envelopeVersion, runtimeJournalReaderContract);

    internal void PublishCompatibleRemoteOne(int addressIndex)
        => RemoteOne.SetDiscovery(Authenticator(FirstRemote),
            Discovery(FirstRemote, addressIndex, runtimeJournalReaderContract: StoreReaderContract.RuntimeJournal));

    internal void PublishRemoteOne(int addressIndex, int requestVersion, int envelopeVersion)
        => RemoteOne.SetDiscovery(Authenticator(FirstRemote),
            Discovery(FirstRemote, addressIndex, requestVersion, envelopeVersion, StoreReaderContract.RuntimeJournal));

    internal void PublishRemoteTwo(int addressIndex, int requestVersion, int envelopeVersion)
        => RemoteTwo.SetDiscovery(Authenticator(SecondRemote),
            Discovery(SecondRemote, addressIndex, requestVersion, envelopeVersion, StoreReaderContract.RuntimeJournal));

    internal void PublishRemoteOneRecord(ReplicaSiloDiscovery discovery)
        => RemoteOne.SetDiscovery(Authenticator(FirstRemote), discovery);

    internal void PublishRemoteTwoRecord(ReplicaSiloDiscovery discovery)
        => RemoteTwo.SetDiscovery(Authenticator(SecondRemote), discovery);

    internal void TamperRemoteOnePayload() => RemoteOne.SetTamperedPayload();

    private async Task StartCoreAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        await runtime.RestartIfStoppedAsync();
        var local = runtime.Node(LocalVoter);
        Configuration = local.Journal.Configuration;
        Options = local.Peers;
        runtime.Node(FirstRemote).Observation.Reset();
        runtime.Node(SecondRemote).Observation.Reset();
        PublishCompatibleRemoteOne(1);
        PublishRemoteTwoRecord(Discovery(SecondRemote, 2));
        var probe = new ReplicaDiscoveryProbe(
            (request, cancellation) => Find(request.DestinationVoter).BeforeSend(request, cancellation),
            static (_, _, _, _) => { },
            (request, reply, cancellation) => Find(request.DestinationVoter).Receive(reply, cancellation),
            (request, cancellation) => Find(request.DestinationVoter).BeforeSendAsync(cancellation));
        client = new(UnitExecutionOptions.ReplicaConfiguration(Configuration),
            UnitRoutingOptions.Peers(Configuration, Options), local.Discovery, local.Authentication,
            TimeProvider.System, runtime.LocalServices,
            runtime.LocalServices.GetRequiredService<ISiloStatusOracle>(), UnitRoutingOptions.Membership(), probe);
    }

    private ReplicaEnvelopeAuthenticator Authenticator(string voter)
        => runtime.Node(voter).Authentication;

    private RequestCqrsCohortEndpoint Find(string voter)
        => runtime.Node(voter).Observation;

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

        runtime.Node(FirstRemote).Observation.Reset();
        runtime.Node(SecondRemote).Observation.Reset();
        RequestCqrsCohortCleanup.ThrowIfAny(failures);
    }
}
