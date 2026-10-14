using System.Security.Cryptography;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.Clustering;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed class RemoteTransferNativeMembership : IDisposable
{
    private readonly byte[] sourceSecret;
    private readonly byte[] destinationSecret;
    private RemoteTransferNativeMembership(byte[] sourceSecret, byte[] destinationSecret,
        IReadOnlyDictionary<string, ReplicaSiloDiscovery> owners,
        IReadOnlyDictionary<string, ReadOnlyMemory<byte>> secrets)
    {
        this.sourceSecret = sourceSecret;
        this.destinationSecret = destinationSecret;
        Owners = owners;
        Secrets = secrets;
    }

    internal IReadOnlyDictionary<string, ReplicaSiloDiscovery> Owners
    {
        get;
    }
    internal IReadOnlyDictionary<string, ReadOnlyMemory<byte>> Secrets
    {
        get;
    }
    internal IReadOnlyList<ReplicaSiloDiscovery> Source => TwoRf3MembershipProtocol.Nodes[..3].Select(node => Owners[node]).ToArray();
    internal IReadOnlyList<ReplicaSiloDiscovery> Destination => TwoRf3MembershipProtocol.Nodes[3..].Select(node => Owners[node]).ToArray();
    internal string SourceSecret => Convert.ToBase64String(sourceSecret);
    internal string DestinationSecret => Convert.ToBase64String(destinationSecret);

    internal static async Task<RemoteTransferNativeMembership> ReadAsync(TwoRf3MembershipWave wave,
        CancellationToken token)
    {
        var model = wave.Application.Services.GetRequiredService<DistributedApplicationModel>();
        var incarnation = Guid.ParseExact(await ParameterAsync(model, "membership-incarnation-b", token), "D");
        var source = Convert.FromBase64String(wave.Profile.PeerSecret);
        byte[] target = [];
        try
        {
            target = Convert.FromBase64String(await ParameterAsync(model, "membership-peer-b", token));
            if (source.Length != 32 || target.Length != 32)
            {
                throw new InvalidOperationException(RemoteTransferDistinctProtocol.Missing);
            }
            var owners = new Dictionary<string, ReplicaSiloDiscovery>(StringComparer.Ordinal);
            var secrets = new Dictionary<string, ReadOnlyMemory<byte>>(StringComparer.Ordinal);
            var cluster = "keyload-" + wave.Profile.Incarnation.ToString("N");
            foreach (var node in TwoRf3MembershipProtocol.Nodes)
            {
                var control = Array.IndexOf(TwoRf3MembershipProtocol.Nodes, node) < 3;
                var key = control ? source : target;
                owners.Add(node, await TwoRf3MembershipSignedDiscovery.ReadAsync(wave.Application, node, cluster,
                    control ? wave.Profile.Incarnation : incarnation, key, wave.Profile.AdminKey, token));
                secrets.Add(node, key);
            }
            if (owners.Values.Select(value => value.VoterId).Distinct(StringComparer.Ordinal).Count() != 6
                || owners.Values.Select(value => value.SiloAddress).Distinct(StringComparer.Ordinal).Count() != 6)
            {
                throw new InvalidOperationException(RemoteTransferDistinctProtocol.Missing);
            }
            return new(source, target, owners, secrets);
        }
        catch (Exception)
        {
            CryptographicOperations.ZeroMemory(source);
            CryptographicOperations.ZeroMemory(target);
            throw;
        }
    }

    private static async Task<string> ParameterAsync(DistributedApplicationModel model, string name, CancellationToken token)
        => await model.Resources.OfType<ParameterResource>().Single(value => value.Name == name).GetValueAsync(token)
            ?? throw new InvalidOperationException(RemoteTransferDistinctProtocol.Missing);

    public void Dispose()
    {
        CryptographicOperations.ZeroMemory(sourceSecret);
        CryptographicOperations.ZeroMemory(destinationSecret);
    }
}
