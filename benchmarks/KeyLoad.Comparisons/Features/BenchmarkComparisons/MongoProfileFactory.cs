using MongoDB.Driver;

namespace KeyLoad.Comparisons.Targets;

internal static class MongoProfileFactory
{
    public static TargetProfile Create(string connectionString, string image, ComparisonTopology topology)
    {
        var url = new MongoUrl(connectionString);
        return new TargetProfile(MongoSchema.TargetName, MongoSchema.ProfileVersion,
            topology == ComparisonTopology.Replicated ? MongoSchema.ReplicatedTopology : MongoSchema.SingleTopology,
            MongoSchema.WriteAcknowledgement, MongoSchema.ReadContract,
            !url.UseTls ? MongoSchema.TlsAbsent : !url.AllowInsecureTls ? MongoSchema.TlsVerified : MongoSchema.TlsUnverified,
            !string.IsNullOrEmpty(url.Username) ? MongoSchema.AuthenticatedNoClientCertificate : MongoSchema.UnauthenticatedNoClientCertificate, image);
    }
}
