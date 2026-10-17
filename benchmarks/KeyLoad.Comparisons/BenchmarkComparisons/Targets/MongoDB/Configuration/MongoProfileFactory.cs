using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace KeyLoad.Comparisons.Targets;

internal static class MongoProfileFactory
{
    public static TargetProfile Create(string connectionString, string image, ComparisonTopology topology)
    {
        const int SingleNodeTopology = 1;

        var url = new MongoUrl(connectionString);
        var nodes = ComparisonTopologies.NodeCount(topology);
        return new TargetProfile(MongoSchema.TargetName, MongoSchema.ProfileVersion,
            nodes == SingleNodeTopology ? MongoSchema.SingleTopology : MongoSchema.ReplicatedTopology,
            MongoSchema.WriteAcknowledgement, MongoSchema.ReadContract,
            !url.UseTls ? MongoSchema.TlsAbsent : !url.AllowInsecureTls ? MongoSchema.TlsVerified : MongoSchema.TlsUnverified,
            !string.IsNullOrEmpty(url.Username) ? MongoSchema.AuthenticatedNoClientCertificate : MongoSchema.UnauthenticatedNoClientCertificate, image);
    }
    internal static MongoClientSettings CreateSettings(string connectionString, int concurrency,
        IOptions<NativeComparisonExecutionOptions> executionOptions)
    {
        var execution = NativeComparisonExecutionOptions.Require(executionOptions).Value;
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(concurrency);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(concurrency, int.MaxValue - execution.MongoPoolSessionMargin);
        var settings = MongoClientSettings.FromConnectionString(connectionString);
        settings.WriteConcern = MongoSchema.MajorityJournalWriteConcern;
        settings.ReadConcern = ReadConcern.Majority;
        settings.ReadPreference = ReadPreference.Primary;
        settings.RetryWrites = false;
        settings.RetryReads = false;
        settings.MaxConnectionPoolSize = Math.Max(concurrency + execution.MongoPoolSessionMargin, execution.MongoPoolMinimumSize);
        return settings;
    }

}
