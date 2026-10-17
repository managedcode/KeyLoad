using MongoDB.Bson;
using MongoDB.Driver;

namespace KeyLoad.Comparisons.Targets;

/// <summary>Verifies the original native MongoDB server version and strict single-node topology.</summary>
internal static class MongoServerVerification
{
    internal static async Task VerifySingleNodeAsync(MongoClient? primaryClient, CancellationToken cancellationToken)
    {
        const int FirstElementIndex = 0;

        var admin = primaryClient!.GetDatabase(MongoSchema.AdminDatabase);
        var hello = await admin.RunCommandAsync<BsonDocument>(
            new BsonDocument(MongoSchema.HelloCommand, MongoSchema.CommandEnabledValue),
            ReadPreference.Primary, cancellationToken);
        if (!hello.GetValue(MongoSchema.WritablePrimaryField, false).ToBoolean()
            || hello.GetValue(MongoSchema.RouterMessageField, BsonNull.Value) == MongoSchema.RouterMessage)
        {
            throw new ComparisonFailureException(MongoSchema.FailureUnsupportedTopology);
        }
        if (!hello.Contains(MongoSchema.ReplicaSetNameField))
        {
            return;
        }
        var status = await admin.RunCommandAsync<BsonDocument>(
            new BsonDocument(MongoSchema.ReplicaSetStatusCommand, MongoSchema.CommandEnabledValue),
            ReadPreference.Primary, cancellationToken);
        var members = status.GetValue(MongoSchema.MembersField).AsBsonArray;
        if (members.Count != MongoSchema.SingleNodeCount
            || members[FirstElementIndex].AsBsonDocument.GetValue(MongoSchema.MemberStateField).AsString != MongoSchema.PrimaryState
            || members[FirstElementIndex].AsBsonDocument.GetValue(MongoSchema.MemberHealthField).ToInt32() != MongoSchema.HealthyMemberValue)
        {
            throw new ComparisonFailureException(MongoSchema.FailureUnsupportedTopology);
        }
    }

    internal static async Task<string> ReadPrimaryVersionAsync(IMongoDatabase? database, CancellationToken cancellationToken)
    {
        var result = await (database ?? throw new ComparisonFailureException(MongoSchema.FailureNotInitialized)).RunCommandAsync<BsonDocument>(
            new BsonDocument(MongoSchema.BuildInfoCommand, MongoSchema.CommandEnabledValue),
            ReadPreference.Primary, cancellationToken);
        if (!result.TryGetValue(MongoSchema.VersionField, out var version) || !version.IsString)
        {
            throw new ComparisonFailureException(MongoSchema.FailureVersionUnavailable);
        }
        return version.AsString;
    }
}
