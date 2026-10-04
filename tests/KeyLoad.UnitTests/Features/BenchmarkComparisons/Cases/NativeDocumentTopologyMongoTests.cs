using KeyLoad.Comparisons;
using KeyLoad.Comparisons.Targets;
using MongoDB.Bson;
using MongoDB.Driver;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

/// <summary>AC-ISO-003/005 checks real BSON receipts and native driver result objects.</summary>
internal sealed class NativeDocumentTopologyMongoTests
{
    private const string Connection = "mongodb://localhost:27017/?replicaSet=benchmark";
    private const string Set = "set";
    private const string SetName = "benchmark";
    private const string ForeignSet = "foreign";
    private const string Identity = "_id";
    private const string HostPrefix = "mongo";
    private const string HostSuffix = ":27017";
    private const string VotingMembers = "votingMembersCount";
    private const string WritableMembers = "writableVotingMembersCount";
    private const string VoteMajority = "majorityVoteCount";
    private const string WriteMajority = "writeMajorityCount";
    private const string Self = "self";

    [Test]
    public async Task AC_ISO_003_RequiresHealthyDistinctSameSetMembersForBothTwoAndThreeNodes()
    {
        var two = Status(2);
        var three = Status(3);
        await Assert.That(MongoReplicaVerifier.ValidateMembers(two, ComparisonTopology.TwoNode, SetName).Length).IsEqualTo(2);
        await Assert.That(MongoReplicaVerifier.ValidateMembers(three, ComparisonTopology.Replicated, SetName).Length).IsEqualTo(3);
        Assert.ThrowsExactly<ComparisonFailureException>(() => MongoReplicaVerifier.ValidateMembers(three, ComparisonTopology.TwoNode, SetName));
        two[Set] = ForeignSet;
        Assert.ThrowsExactly<ComparisonFailureException>(() => MongoReplicaVerifier.ValidateMembers(two, ComparisonTopology.TwoNode, SetName));
        two = Status(2);
        two[MongoSchema.MembersField][1][MongoSchema.MemberHostField] = two[MongoSchema.MembersField][0][MongoSchema.MemberHostField];
        Assert.ThrowsExactly<ComparisonFailureException>(() => MongoReplicaVerifier.ValidateMembers(two, ComparisonTopology.TwoNode, SetName));
        two = Status(2);
        two[WriteMajority] = 1;
        Assert.ThrowsExactly<ComparisonFailureException>(() => MongoReplicaVerifier.ValidateMembers(two, ComparisonTopology.TwoNode, SetName));
        two = Status(2);
        two[MongoSchema.MembersField][1][Identity] = two[MongoSchema.MembersField][0][Identity];
        Assert.ThrowsExactly<ComparisonFailureException>(() => MongoReplicaVerifier.ValidateMembers(two, ComparisonTopology.TwoNode, SetName));
        two = Status(2);
        two[MongoSchema.MembersField][1][MongoSchema.MemberHostField] = string.Empty;
        Assert.ThrowsExactly<ComparisonFailureException>(() => MongoReplicaVerifier.ValidateMembers(two, ComparisonTopology.TwoNode, SetName));
    }

    [Test]
    public void AC_ISO_003_DirectSecondaryReceiptMustIdentifyTheExpectedNativeSelf()
    {
        var status = Status(2);
        var expected = status[MongoSchema.MembersField][1].AsBsonDocument;
        expected[Self] = true;
        MongoReplicaMembers.ValidateDirectMember(status, expected);
        expected.Remove(Self);
        status[MongoSchema.MembersField][0][Self] = true;
        Assert.ThrowsExactly<ComparisonFailureException>(() => MongoReplicaMembers.ValidateDirectMember(status, expected));
        expected[Self] = true;
        Assert.ThrowsExactly<ComparisonFailureException>(() => MongoReplicaMembers.ValidateDirectMember(status, expected));
    }

    [Test]
    public async Task AC_ISO_005_MatchedButUnchangedUpsertAndExcessCountsAreFailures()
    {
        MongoSession.ValidateUpdateResult(new UpdateResult.Acknowledged(1, 1, null));
        var missing = Assert.ThrowsExactly<ComparisonFailureException>(() => MongoSession.ValidateUpdateResult(new UpdateResult.Acknowledged(0, 0, null)));
        var unchanged = Assert.ThrowsExactly<ComparisonFailureException>(() => MongoSession.ValidateUpdateResult(new UpdateResult.Acknowledged(1, 0, null)));
        var excess = Assert.ThrowsExactly<ComparisonFailureException>(() => MongoSession.ValidateUpdateResult(new UpdateResult.Acknowledged(2, 1, null)));
        await Assert.That(missing.Message).IsEqualTo(ComparisonMutationFailures.UpdateMissing);
        await Assert.That(unchanged.Message).IsEqualTo(ComparisonMutationFailures.CardinalityMismatch);
        await Assert.That(excess.Message).IsEqualTo(ComparisonMutationFailures.CardinalityMismatch);
        Assert.ThrowsExactly<ComparisonFailureException>(() => MongoSession.ValidateUpdateResult(new UpdateResult.Acknowledged(1, 1, new BsonString(SetName))));
        Assert.ThrowsExactly<ComparisonFailureException>(() => MongoSession.ValidateUpdateResult(new UpdateResult.Acknowledged(1, null, null)));
        Assert.ThrowsExactly<ComparisonFailureException>(() => MongoSession.ValidateUpdateResult(UpdateResult.Unacknowledged.Instance));
        MongoSession.ValidateDeleteResult(new DeleteResult.Acknowledged(1));
        var deletion = Assert.ThrowsExactly<ComparisonFailureException>(() => MongoSession.ValidateDeleteResult(new DeleteResult.Acknowledged(0)));
        await Assert.That(deletion.Message).IsEqualTo(ComparisonMutationFailures.DeleteMissing);
        Assert.ThrowsExactly<ComparisonFailureException>(() => MongoSession.ValidateDeleteResult(new DeleteResult.Acknowledged(2)));
        Assert.ThrowsExactly<ComparisonFailureException>(() => MongoSession.ValidateDeleteResult(DeleteResult.Unacknowledged.Instance));
    }

    [Test]
    public async Task AC_ISO_005_NativeMajorityJournalSettingsDisableHiddenRetries()
    {
        var settings = MongoTarget.CreateSettings(Connection, 16);
        await Assert.That(settings.RetryWrites).IsFalse();
        await Assert.That(settings.RetryReads).IsFalse();
        await Assert.That(settings.WriteConcern.Journal).IsTrue();
        await Assert.That(settings.WriteConcern.W.ToString()).IsEqualTo(MongoSchema.MajorityMode);
    }

    private static BsonDocument Status(int nodes)
        => new()
        {
            [Set] = SetName,
            [VotingMembers] = nodes,
            [WritableMembers] = nodes,
            [VoteMajority] = nodes / 2 + 1,
            [WriteMajority] = nodes / 2 + 1,
            [MongoSchema.MembersField] = new BsonArray(Enumerable.Range(0, nodes).Select(index => new BsonDocument
            {
                [Identity] = index,
                [MongoSchema.MemberHostField] = HostPrefix + index + HostSuffix,
                [MongoSchema.MemberStateField] = index == 0 ? MongoSchema.PrimaryState : MongoSchema.SecondaryState,
                [MongoSchema.MemberHealthField] = 1,
                [MongoSchema.MemberArbiterField] = false,
            })),
        };
}
