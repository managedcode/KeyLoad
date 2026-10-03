using MongoDB.Bson;

namespace KeyLoad.Comparisons.Targets;

internal static class MongoReplicaMembers
{
    internal static BsonDocument[] Read(BsonDocument status)
        => status.GetValue(MongoSchema.MembersField).AsBsonArray.Select(value => value.AsBsonDocument).ToArray();

    internal static BsonDocument[] Validate(BsonDocument status, ComparisonTopology topology, string? expectedSet)
    {
        var members = Read(status);
        var nodes = ComparisonTopologies.NodeCount(topology);
        var set = status.GetValue(MongoSchema.ReplicaStatusSetField, BsonNull.Value);
        if (!set.IsString || string.IsNullOrWhiteSpace(set.AsString) || expectedSet is not null && set.AsString != expectedSet ||
            members.Length != nodes || members.Count(IsPrimary) != 1 || members.Count(IsSecondary) != nodes - 1 ||
            !NativeCountIs(status, MongoSchema.VotingMembersField, nodes) || !NativeCountIs(status, MongoSchema.WritableVotingMembersField, nodes) ||
            !NativeCountIs(status, MongoSchema.VoteMajorityField, nodes / 2 + 1) || !NativeCountIs(status, MongoSchema.WriteMajorityField, nodes / 2 + 1) ||
            members.Select(member => member[MongoSchema.MemberIdField].ToInt32()).Distinct().Count() != nodes ||
            members.Any(member => string.IsNullOrWhiteSpace(member[MongoSchema.MemberHostField].AsString)) ||
            members.Select(member => member[MongoSchema.MemberHostField].AsString).Distinct(StringComparer.OrdinalIgnoreCase).Count() != nodes)
        {
            throw new ComparisonFailureException(MongoSchema.FailureReplicaSetShape);
        }
        return members;
    }

    internal static void ValidateDirectMember(BsonDocument status, BsonDocument expected)
    {
        var self = Read(status).Where(member => member.GetValue(MongoSchema.MemberSelfField, false).ToBoolean()).ToArray();
        if (self.Length != 1 || !IsSecondary(self[0]) ||
            self[0][MongoSchema.MemberIdField] != expected[MongoSchema.MemberIdField] ||
            self[0][MongoSchema.MemberHostField] != expected[MongoSchema.MemberHostField])
        {
            throw new ComparisonFailureException(MongoSchema.FailureReplicaSetShape);
        }
    }

    internal static bool IsSecondary(BsonDocument member)
        => IsHealthyDataBearing(member) && member.GetValue(MongoSchema.MemberStateField).AsString == MongoSchema.SecondaryState;

    private static bool IsPrimary(BsonDocument member)
        => IsHealthyDataBearing(member) && member.GetValue(MongoSchema.MemberStateField).AsString == MongoSchema.PrimaryState;

    private static bool IsHealthyDataBearing(BsonDocument member)
        => member.GetValue(MongoSchema.MemberHealthField).ToInt32() == MongoSchema.HealthyMemberValue
           && !member.GetValue(MongoSchema.MemberArbiterField, false).ToBoolean();

    private static bool NativeCountIs(BsonDocument status, string field, int expected)
    {
        var value = status.GetValue(field, BsonNull.Value);
        return (value.IsInt32 || value.IsInt64) && value.ToInt64() == expected;
    }
}
