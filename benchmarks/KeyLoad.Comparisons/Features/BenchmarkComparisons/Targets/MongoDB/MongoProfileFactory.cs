using MongoDB.Driver;

namespace KeyLoad.Comparisons.Targets;

internal static class MongoProfileFactory
{
    public static TargetProfile Create(string connectionString, string image, ComparisonTopology topology)
    {
        const int SingleNodeTopology = 1;
        const int TwoNodeReplicaCount = 2;

        var url = new MongoUrl(connectionString);
        var nodes = ComparisonTopologies.NodeCount(topology);
        return new TargetProfile(MongoSchema.TargetName, MongoSchema.ProfileVersion,
            nodes == SingleNodeTopology ? MongoSchema.SingleTopology : nodes == TwoNodeReplicaCount ? MongoSchema.TwoNodeTopology : MongoSchema.ReplicatedTopology,
            MongoSchema.WriteAcknowledgement, MongoSchema.ReadContract,
            !url.UseTls ? MongoSchema.TlsAbsent : !url.AllowInsecureTls ? MongoSchema.TlsVerified : MongoSchema.TlsUnverified,
            !string.IsNullOrEmpty(url.Username) ? MongoSchema.AuthenticatedNoClientCertificate : MongoSchema.UnauthenticatedNoClientCertificate, image);
    }
}
