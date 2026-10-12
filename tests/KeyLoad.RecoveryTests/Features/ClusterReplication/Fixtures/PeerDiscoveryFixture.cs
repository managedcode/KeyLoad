using KeyLoad.Orleans;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;

namespace KeyLoad.RecoveryTests;

/// <summary>Mutations of genuine discovery HTTP requests, without transport doubles.</summary>

internal sealed class PeerDiscoveryFixture : IAsyncDisposable
{
    internal const int SingleCapacity = 1;
    private readonly PeerDiscoveryNativeCluster cluster;
    internal byte[] Secret { get; }
    internal ReplicaEnvelopeAuthenticator Sender { get; }
    internal ReplicaEnvelopeAuthenticator Receiver { get; }
    internal ReplicaNativeDiscoveryRuntime Runtime { get; }
    internal OrleansMembershipOptions Settings { get; }
    internal static CancellationToken Cancellation => TestContext.Current!.Execution.CancellationToken;

    internal PeerDiscoveryFixture(PeerDiscoveryNativeCluster cluster, int capacity = SingleCapacity)
    {
        this.cluster = cluster;
        Secret = cluster.Sender.Peers.Secret.ToArray();
        Settings = new() { ReplayNonceCapacity = capacity };
        ReplicaEnvelopeAuthenticator? sender = null;
        ReplicaEnvelopeAuthenticator? receiver = null;
        try
        {
            Sender = sender = CreateAuthentication(cluster.Sender, Secret);
            Receiver = receiver = CreateAuthentication(cluster.Receiver, Secret);
            Runtime = new(RecoveryExecutionOptions.Configuration(cluster.Receiver.Configuration),
                cluster.Receiver.Discovery, Receiver, cluster.ReceiverServices.GetRequiredService<ISiloStatusOracle>(),
                Options.Create(Settings), TimeProvider.System);
        }
        catch (Exception primary)
        {
            List<Exception> failures = [primary];
            if (receiver is not null)
            { ReplicaMaterializerLifecycleErrors.Attempt(receiver.Dispose, failures); }
            if (sender is not null)
            { ReplicaMaterializerLifecycleErrors.Attempt(sender.Dispose, failures); }
            CryptographicOperations.ZeroMemory(Secret);
            ReplicaMaterializerLifecycleErrors.Throw(failures);
            throw;
        }
    }

    internal static async Task RunAsync(PeerDiscoveryNativeCluster cluster, Func<PeerDiscoveryFixture, Task> scenario, int capacity = SingleCapacity)
    {
        List<Exception> failures = [];
        try
        {
            await using var fixture = new PeerDiscoveryFixture(cluster, capacity);
            await ReplicaMaterializerLifecycleErrors.AttemptAsync(() => scenario(fixture), failures);
        }
        catch (Exception failure) when (NativeCqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
        catch (Exception failure) when (!NativeCqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
        ReplicaMaterializerLifecycleErrors.Throw(failures);
    }

    internal ReplicaNativeDiscoveryRequest Sign() => Sender.CreateNativeDiscoveryRequest(
        PeerDiscoveryNativeCluster.ReceiverVoter, cluster.Receiver.Discovery.RuntimeAddress);

    internal static ReplicaEnvelopeAuthenticator CreateAuthentication(PeerDiscoveryNativeNode node, ReadOnlyMemory<byte> secret)
        => new(RecoveryExecutionOptions.Configuration(node.Configuration), Options.Create(node.Peers with { Secret = secret }),
            node.Discovery, TimeProvider.System, Options.Create(new ReplicaTransportOptions()), Options.Create(node.Peers.ReplayLimits));

    internal async Task<ReplicaNativeDiscoveryReply> ReceiveAsync(ReplicaNativeDiscoveryRequest request)
    {
        var reply = await Runtime.DiscoverAsync(request, Cancellation);
        return Sender.VerifyNativeDiscoveryReply(request, reply);
    }

    internal async Task<ReplicaNativeDiscoveryReply> ReceiveNativeAsync(ReplicaNativeDiscoveryRequest request)
    {
        var client = new ReplicaNativeDiscoveryClient(cluster.SenderServices);
        var reply = await client.DiscoverAsync(SiloAddress.FromParsableString(cluster.Receiver.Discovery.RuntimeAddress), request, Cancellation);
        return Sender.VerifyNativeDiscoveryReply(request, reply);
    }

    public async ValueTask DisposeAsync()
    {
        List<Exception> failures = [];
        await ReplicaMaterializerLifecycleErrors.AttemptAsync(() => Runtime.DisposeAsync().AsTask(), failures);
        ReplicaMaterializerLifecycleErrors.Attempt(Sender.Dispose, failures);
        ReplicaMaterializerLifecycleErrors.Attempt(Receiver.Dispose, failures);
        CryptographicOperations.ZeroMemory(Secret);
        ReplicaMaterializerLifecycleErrors.Throw(failures);
    }
}
