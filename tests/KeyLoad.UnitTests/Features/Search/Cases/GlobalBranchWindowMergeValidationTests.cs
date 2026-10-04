using System.Collections.Immutable;
using KeyLoad.Query.Features.Search;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class GlobalBranchWindowMergeValidationTests
{
    [Test]
    public async Task AcRank002RejectsDuplicateOrUnknownSourceWindowIdsAsValidation()
    {
        var request = GlobalBranchTestSupport.Request(["expected"]);
        var valid = GlobalBranchTestSupport.Window("expected", [GlobalBranchTestSupport.Candidate("a", 1)]);
        var duplicate = GlobalBranchTestSupport.Window("expected", [GlobalBranchTestSupport.Candidate("b", 1)]);
        var unknown = GlobalBranchTestSupport.Window("other", [GlobalBranchTestSupport.Candidate("b", 1)]);

        await AssertFailure(request, [valid, duplicate], ErrorCode.Validation);
        await AssertFailure(request, [unknown], ErrorCode.Validation);
        await AssertFailure(GlobalBranchTestSupport.Request(["expected", "expected"]), [valid], ErrorCode.Validation);
    }

    [Test]
    public async Task AcRank002RejectsIncomparableScopesAndConflictingCandidateEvidence()
    {
        var request = GlobalBranchTestSupport.Request(["a", "b"]);
        var candidate = GlobalBranchTestSupport.Candidate("same", 0.75);
        var first = GlobalBranchTestSupport.Window("a", [candidate]);
        var mismatchedScopes = new[]
        {
            GlobalBranchTestSupport.Scope(profile: "profile-v2"),
            GlobalBranchTestSupport.Scope(corpus: "another-corpus"),
            GlobalBranchTestSupport.Scope(statistics: "stats-18")
        };

        foreach (var scope in mismatchedScopes)
        {
            await AssertFailure(request, [first, GlobalBranchTestSupport.Window("b", [candidate], scope: scope)],
                ErrorCode.Corruption);
        }
        await AssertFailure(request, [first, GlobalBranchTestSupport.Window("b", [candidate with { Revision = 2 }])],
            ErrorCode.Corruption);
        await AssertFailure(request, [first, GlobalBranchTestSupport.Window("b", [candidate with { Score = 0.74 }])],
            ErrorCode.Corruption);
        await AssertFailure(request, [first, GlobalBranchTestSupport.Window("b", [candidate], kind: GlobalBranchKind.Graph)],
            ErrorCode.Corruption);
        await AssertFailure(GlobalBranchTestSupport.Request(["a"]),
            [GlobalBranchTestSupport.Window("a", [candidate with { Score = double.NaN }])], ErrorCode.Corruption);
    }

    [Test]
    public async Task AcRank002RejectsUnorderedWindowAndInvalidRequestIdentifiers()
    {
        var low = GlobalBranchTestSupport.Candidate("low", 0.2);
        var high = GlobalBranchTestSupport.Candidate("high", 0.9);
        var unordered = GlobalBranchTestSupport.Window("w", [low, high]);
        var invalidRequest = GlobalBranchTestSupport.Request(["w"]) with { BranchName = " " };

        await AssertFailure(GlobalBranchTestSupport.Request(["w"]), [unordered], ErrorCode.Corruption);
        var invalid = Assert.ThrowsExactly<KeyLoadException>(() => GlobalBranchTestSupport.Merge(invalidRequest,
            [GlobalBranchTestSupport.Window("w", [high, low])]));
        await Assert.That(invalid.Code).IsEqualTo(ErrorCode.Validation);
    }

    [Test]
    public async Task AcRank002RejectsDefaultArraysAndInvalidLimitsBeforeCandidateProcessing()
    {
        var validWindow = GlobalBranchTestSupport.Window("w", [GlobalBranchTestSupport.Candidate("a", 1)]);
        var validRequest = GlobalBranchTestSupport.Request(["w"]);
        var defaultIds = validRequest with { ExpectedWindowIds = default };
        var zeroLimit = validRequest with { Limit = 0 };
        var invalidKind = validRequest with { Kind = (GlobalBranchKind)9 };

        await AssertFailure(defaultIds, [validWindow], ErrorCode.Validation);
        await AssertFailure(zeroLimit, [validWindow], ErrorCode.Validation);
        await AssertFailure(invalidKind, [validWindow], ErrorCode.Validation);
    }

    [Test]
    public async Task AcRank003StrictMissingFailsAndExplicitIncompleteRetainsMissingIds()
    {
        var onlyWindow = GlobalBranchTestSupport.Window("first", [GlobalBranchTestSupport.Candidate("a", 0.8)]);
        var strict = GlobalBranchTestSupport.Request(["first", "second"]);
        var incomplete = strict with { AllowIncomplete = true };

        var failure = Assert.ThrowsExactly<KeyLoadException>(() => GlobalBranchTestSupport.Merge(strict, [onlyWindow]));
        var result = GlobalBranchTestSupport.Merge(incomplete, [onlyWindow]);

        await Assert.That(failure.Code).IsEqualTo(ErrorCode.OwnershipLost);
        await Assert.That(result.MissingWindowIds).IsEquivalentTo(["second"], CollectionOrdering.Matching);
        await Assert.That(result.WindowComplete).IsFalse();
        await Assert.That(result.Exhaustive).IsFalse();
    }

    [Test]
    public async Task AcRank003ApproximateAndTruncatedWindowsCannotSatisfyStrictExactRequest()
    {
        var candidate = GlobalBranchTestSupport.Candidate("a", 0.8);
        var request = GlobalBranchTestSupport.Request(["w"]);
        var approximate = GlobalBranchTestSupport.Window("w", [candidate], approximate: true);
        var truncated = GlobalBranchTestSupport.Window("w", [candidate], truncated: true, exhaustive: false);
        var incomplete = GlobalBranchTestSupport.Window("w", [candidate], complete: false, exhaustive: false);

        var approximateFailure = Assert.ThrowsExactly<KeyLoadException>(() =>
            GlobalBranchTestSupport.Merge(request, [approximate]));
        var truncatedFailure = Assert.ThrowsExactly<KeyLoadException>(() =>
            GlobalBranchTestSupport.Merge(request, [truncated]));
        var partial = GlobalBranchTestSupport.Merge(request with { AllowIncomplete = true }, [truncated]);
        var approximatePartial = GlobalBranchTestSupport.Merge(request with { AllowIncomplete = true }, [approximate]);
        var incompleteFailure = Assert.ThrowsExactly<KeyLoadException>(() =>
            GlobalBranchTestSupport.Merge(request, [incomplete]));
        var incompletePartial = GlobalBranchTestSupport.Merge(request with { AllowIncomplete = true }, [incomplete]);

        await Assert.That(approximateFailure.Code).IsEqualTo(ErrorCode.UnsupportedCapability);
        await Assert.That(truncatedFailure.Code).IsEqualTo(ErrorCode.UnsupportedCapability);
        await Assert.That(incompleteFailure.Code).IsEqualTo(ErrorCode.OwnershipLost);
        await Assert.That(approximatePartial.Approximate).IsTrue();
        await Assert.That(partial.Truncated).IsTrue();
        await Assert.That(partial.Exhaustive).IsFalse();
        await Assert.That(incompletePartial.WindowComplete).IsFalse();
    }

    [Test]
    public async Task AcRank003RequestedOutputWindowTruncationClearsExhaustive()
    {
        var candidates = new[]
        {
            GlobalBranchTestSupport.Candidate("a", 0.9),
            GlobalBranchTestSupport.Candidate("b", 0.8)
        };
        var result = GlobalBranchTestSupport.Merge(GlobalBranchTestSupport.Request(["w"], limit: 1),
            [GlobalBranchTestSupport.Window("w", candidates)]);

        await Assert.That(result.Candidates).HasSingleItem();
        await Assert.That(result.WindowComplete).IsTrue();
        await Assert.That(result.Truncated).IsTrue();
        await Assert.That(result.Exhaustive).IsFalse();
    }

    [Test]
    public async Task AcRank003CompleteFiniteTopWindowIsNotExhaustive()
    {
        var candidates = new[]
        {
            GlobalBranchTestSupport.Candidate("a", 0.9),
            GlobalBranchTestSupport.Candidate("b", 0.8)
        };
        var result = GlobalBranchTestSupport.Merge(GlobalBranchTestSupport.Request(["w"], limit: 2),
            [GlobalBranchTestSupport.Window("w", candidates, exhaustive: false)]);

        await Assert.That(result.WindowComplete).IsTrue();
        await Assert.That(result.Truncated).IsFalse();
        await Assert.That(result.Exhaustive).IsFalse();
    }

    private static async Task AssertFailure(GlobalBranchMergeRequest request,
        ImmutableArray<GlobalBranchWindow> windows, ErrorCode code)
    {
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => GlobalBranchTestSupport.Merge(request, windows));
        await Assert.That(failure.Code).IsEqualTo(code);
    }
}
