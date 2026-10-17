using System.Collections.Immutable;
using KeyLoad.Query.Features.Search;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class GlobalBranchWindowMergeLayoutTests
{
    [Test]
    public async Task AcRank001CentralAndTwoOrThreeWindowLayoutsHaveTheSameOrderedCandidates()
    {
        var scope = GlobalBranchTestSupport.Scope();
        var candidates = new[]
        {
            GlobalBranchTestSupport.Candidate("a", 0.91),
            GlobalBranchTestSupport.Candidate("b", 0.83),
            GlobalBranchTestSupport.Candidate("c", 0.72),
            GlobalBranchTestSupport.Candidate("d", 0.63),
            GlobalBranchTestSupport.Candidate("e", 0.51),
            GlobalBranchTestSupport.Candidate("f", 0.40)
        };
        var expected = GlobalBranchTestSupport.IndependentOrder(candidates);
        var central = GlobalBranchTestSupport.Merge(GlobalBranchTestSupport.Request(["all"], scope: scope),
            [GlobalBranchTestSupport.Window("all", expected, scope: scope)]);
        var two = GlobalBranchTestSupport.Merge(GlobalBranchTestSupport.Request(["left", "right"], scope: scope),
            [GlobalBranchTestSupport.Window("left", GlobalBranchTestSupport.IndependentOrder(
                    [candidates[0], candidates[2], candidates[4]]), scope: scope),
                GlobalBranchTestSupport.Window("right", GlobalBranchTestSupport.IndependentOrder(
                    [candidates[1], candidates[3], candidates[5], candidates[0]]), scope: scope)]);
        var three = GlobalBranchTestSupport.Merge(GlobalBranchTestSupport.Request(["a", "b", "c"], scope: scope),
            [GlobalBranchTestSupport.Window("a", GlobalBranchTestSupport.IndependentOrder(
                    [candidates[0], candidates[3]]), scope: scope),
                GlobalBranchTestSupport.Window("b", GlobalBranchTestSupport.IndependentOrder(
                    [candidates[1], candidates[4], candidates[0]]), scope: scope),
                GlobalBranchTestSupport.Window("c", GlobalBranchTestSupport.IndependentOrder(
                    [candidates[2], candidates[5]]), scope: scope)]);
        var expectedRanks = expected.Select((candidate, index) => new GlobalBranchExpectedRank(candidate.Reference, index + 1))
            .ToArray();

        await Assert.That(central.Candidates).IsEquivalentTo(expected, CollectionOrdering.Matching);
        await Assert.That(two.Candidates).IsEquivalentTo(expected, CollectionOrdering.Matching);
        await Assert.That(three.Candidates).IsEquivalentTo(expected, CollectionOrdering.Matching);
        await Assert.That(Ranks(central)).IsEquivalentTo(expectedRanks, CollectionOrdering.Matching);
        await Assert.That(Ranks(two)).IsEquivalentTo(expectedRanks, CollectionOrdering.Matching);
        await Assert.That(Ranks(three)).IsEquivalentTo(expectedRanks, CollectionOrdering.Matching);
        await Assert.That(two.ExaminedCandidateCount).IsEqualTo(7);
        await Assert.That(three.ExaminedCandidateCount).IsEqualTo(7);
        await Assert.That(central.Exhaustive && two.Exhaustive && three.Exhaustive).IsTrue();
    }

    private static GlobalBranchExpectedRank[] Ranks(GlobalBranchMergeResult result)
        => result.Candidates.Select((candidate, index) => new GlobalBranchExpectedRank(candidate.Reference, index + 1))
            .ToArray();

    [Test]
    public async Task AcRank002FullIdentityTieOrderUsesIndependentOrdinalComponents()
    {
        var tied = new[]
        {
            GlobalBranchTestSupport.Candidate("same", 1, tenant: "z"),
            GlobalBranchTestSupport.Candidate("same", 1, database: "b"),
            GlobalBranchTestSupport.Candidate("same", 1, domain: "aaa"),
            GlobalBranchTestSupport.Candidate("same", 1, partition: "0"),
            GlobalBranchTestSupport.Candidate("same", 1, collection: "a"),
            GlobalBranchTestSupport.Candidate("A", 1)
        };
        var expected = GlobalBranchTestSupport.IndependentOrder(tied);
        var request = GlobalBranchTestSupport.Request(["w"]);
        var result = GlobalBranchTestSupport.Merge(request,
            [GlobalBranchTestSupport.Window("w", expected)]);

        await Assert.That(result.Candidates).IsEquivalentTo(expected, CollectionOrdering.Matching);
        await Assert.That(result.Candidates.Select(candidate => candidate.Reference).Distinct().Count())
            .IsEqualTo(tied.Length);
    }

    [Test]
    public async Task AcRank001DuplicateCandidateContributesOnceAndAbsentBranchAddsNoRank()
    {
        var first = GlobalBranchTestSupport.Candidate("first", 0.9);
        var second = GlobalBranchTestSupport.Candidate("second", 0.8);
        var request = GlobalBranchTestSupport.Request(["one", "two"]);
        var windows = ImmutableArray.Create(
            GlobalBranchTestSupport.Window("one", [first, second]),
            GlobalBranchTestSupport.Window("two", [first]));
        var merged = GlobalBranchTestSupport.Merge(request, windows);
        var fusion = new SearchRankFusion(13, 10, new(UnitExecutionOptions.DatabaseLimits()));
        fusion.AddBranch(merged.Candidates.Select(candidate => new SearchScore(candidate.Reference, candidate.Score)).ToArray(), 1);
        var actual = fusion.Select();
        var independentScore = 1d / 14;

        await Assert.That(merged.ExaminedCandidateCount).IsEqualTo(3);
        await Assert.That(merged.Candidates.Length).IsEqualTo(2);
        await Assert.That(actual.Single(candidate => candidate.Reference == first.Reference).Score)
            .IsEqualTo(independentScore);
    }

    [Test]
    public async Task AcRank004LocalAdapterReusesFullOrderWithoutCopyingItsCanonicalArray()
    {
        var candidates = GlobalBranchTestSupport.IndependentOrder(
        [
            GlobalBranchTestSupport.Candidate("same", 1, tenant: "z"),
            GlobalBranchTestSupport.Candidate("same", 1, tenant: "a")
        ]);
        var branch = candidates.Select(candidate => new SearchScore(candidate.Reference, candidate.Score)).ToArray();
        var validated = GlobalBranchSinglePartitionAdapter.Prepare(branch);
        var fusion = new SearchRankFusion(13, 2, new(UnitExecutionOptions.DatabaseLimits()));
        fusion.AddBranch(branch, 1);

        await Assert.That(ReferenceEquals(validated, branch)).IsTrue();
        await Assert.That(fusion.Select().Select(candidate => candidate.Reference).ToArray())
            .IsEquivalentTo(candidates.Select(candidate => candidate.Reference).ToArray(), CollectionOrdering.Matching);
        GlobalBranchSinglePartitionAdapter.ValidateAt(branch, 0, new(UnitExecutionOptions.DatabaseLimits()));
        var invalid = Assert.ThrowsExactly<KeyLoadException>(() => new SearchRankFusion(13, 2,
            new(UnitExecutionOptions.DatabaseLimits())).AddBranch(branch.Reverse().ToArray(), 1));
        await Assert.That(invalid.Code).IsEqualTo(ErrorCode.Corruption);
    }
}
