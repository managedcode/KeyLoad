using MongoDB.Driver;

namespace KeyLoad.Comparisons.Targets;

internal static class MongoProfileFactory
{
    public static TargetProfile Create(string connectionString, string image, ComparisonTopology topology)
    {
        var url = new MongoUrl(connectionString);
        var nodes = ComparisonTopologies.NodeCount(topology);
        return new TargetProfile(MongoSchema.TargetName, MongoSchema.ProfileVersion,
            nodes == 1 ? MongoSchema.SingleTopology : nodes == 2 ? MongoSchema.TwoNodeTopology : MongoSchema.ReplicatedTopology,
            MongoSchema.WriteAcknowledgement, MongoSchema.ReadContract,
            !url.UseTls ? MongoSchema.TlsAbsent : !url.AllowInsecureTls ? MongoSchema.TlsVerified : MongoSchema.TlsUnverified,
            !string.IsNullOrEmpty(url.Username) ? MongoSchema.AuthenticatedNoClientCertificate : MongoSchema.UnauthenticatedNoClientCertificate, image);
    }
}
