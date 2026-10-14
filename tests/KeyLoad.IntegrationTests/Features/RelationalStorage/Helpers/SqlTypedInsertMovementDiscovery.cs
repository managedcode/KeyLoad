using KeyLoad.Clustering;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using System.Security.Cryptography;
namespace KeyLoad.IntegrationTests.Features.RelationalStorage;
internal static class SqlTypedInsertMovementDiscovery
{
    internal static async Task<ReplicaSiloDiscovery[]> ReadAsync(TwoRf3MembershipWave wave,
        PartitionMovementPublicParentRf3Seed seed,SqlTypedInsertMovementState state,CancellationToken token)
    {
        var owner=state.PublishedOwner ?? throw new InvalidOperationException(SqlTypedInsertMovementProtocol.Missing);
        var first=Convert.FromBase64String(wave.Profile.PeerSecret);
        byte[] second=[];
        try
        {
            second=Convert.FromBase64String(wave.MovementTargetPeerKey
                ?? throw new InvalidOperationException(SqlTypedInsertMovementProtocol.Missing));
            var actual=new ReplicaSiloDiscovery[TwoRf3MembershipProtocol.NodeCount];
            for(var index=First;index<actual.Length;index++)
            {
                var source=index<TwoRf3MembershipProtocol.MembersPerGroup;
                actual[index]=await TwoRf3MembershipSignedDiscovery.ReadAsync(wave.Application,
                    TwoRf3MembershipProtocol.Nodes[index],ClusterPrefix+wave.Profile.Incarnation.ToString("N"),
                    source?wave.Profile.Incarnation:owner.Incarnation,source?first:second,seed.Credential,token);
            }
            return actual;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(first);
            CryptographicOperations.ZeroMemory(second);
        }
    }
    private const string ClusterPrefix="keyload-";
    private const int First=0;
}

