using System.Text.Json;
using KeyLoad.Query.Features.Search;
using Microsoft.Extensions.Options;
using TUnit.Assertions.Enums;
using static KeyLoad.UnitTests.Features.Search.AnnProviderQualificationProtocol;

namespace KeyLoad.UnitTests.Features.Search;

internal static class AnnProviderQualificationResults
{
    internal static async Task CurrentAsync(AnnProviderQualificationOwner owner, AnnProviderQualificationState state,
        PackedAnnIndex index, CancellationToken token)
    {
        var image = AnnProviderQualificationImages.Capture(owner);
        var records = AnnProviderQualificationCorpus.Read(owner);
        await Assert.That(JsonSerializer.Serialize(records, JsonDefaults.Options))
            .IsEqualTo(JsonSerializer.Serialize(AnnProviderQualificationCorpus.Expected(state), JsonDefaults.Options));
        await IndexAsync(owner, state, index, token);
        await AnnProviderQualificationImages.SameAsync(owner, image);
    }

    internal static async Task IndexAsync(AnnProviderQualificationOwner owner, AnnProviderQualificationState state,
        PackedAnnIndex index, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var expected = AnnProviderQualificationCorpus.Expected(state);
        await Assert.That(index.Space).IsEqualTo(AnnProviderQualificationCorpus.Space(state.Metric));
        await Assert.That(index.Count).IsEqualTo(expected.Length);
        await Assert.That(index.RetainedBytesUpperBound).IsGreaterThan(NoReservation);
        await Assert.That(index.RetainedBytesUpperBound).IsLessThanOrEqualTo(state.Policy.MaxIndexBytes);
        await Assert.That(index.BuildScratchBytesUpperBound).IsLessThanOrEqualTo(state.Policy.MaxScratchBytes);
        var snapshot = index.Snapshot(Field, Options.Create(new PackedAnnStorageOptions()),
            AnnProviderQualificationCorpus.Budget(owner, token));
        await Assert.That(JsonSerializer.Serialize(snapshot.Records, JsonDefaults.Options))
            .IsEqualTo(JsonSerializer.Serialize(expected, JsonDefaults.Options));
        var result = index.Search(AnnProviderQualificationCorpus.Query, expected.Length, null,
            AnnProviderQualificationCorpus.Budget(owner, token));
        await CandidatesAsync(result, AnnProviderQualificationCorpus.Candidates(state), state);
        var mask = state.Deleted && !state.Reinserted ? BothRemainingEligibility : AlphaGammaEligibility;
        ulong[] eligibility = [mask];
        var filtered = index.Search(AnnProviderQualificationCorpus.Query, FilteredCount, eligibility,
            AnnProviderQualificationCorpus.Budget(owner, token));
        await CandidatesAsync(filtered, AnnProviderQualificationCorpus.Candidates(state)
            .Where(row => row.DocumentId != Beta).ToArray(), state);
    }

    private static async Task CandidatesAsync(AnnSearchResult actual, AnnCandidate[] expected,
        AnnProviderQualificationState state)
    {
        await Assert.That(actual.Candidates.Select(row => (row.SourceOrdinal, row.DocumentId, row.DocumentRevision)).ToArray())
            .IsEquivalentTo(expected.Select(row => (row.SourceOrdinal, row.DocumentId, row.DocumentRevision)).ToArray(), CollectionOrdering.Matching);
        foreach (var pair in actual.Candidates.Zip(expected))
        {
            await Assert.That(Math.Abs(pair.First.Score - pair.Second.Score)).IsLessThanOrEqualTo(ScoreTolerance);
        }
        await Assert.That(actual.Mode).IsEqualTo(AnnSearchMode.Approximate);
        await Assert.That(actual.EligibleCount).IsEqualTo(expected.Length);
        await Assert.That(actual.ExpansionPasses).IsEqualTo(FirstExpansion);
        await Assert.That(actual.FallbackDistanceEvaluations).IsEqualTo(NoWork);
        await Assert.That(actual.WorkUnits).IsGreaterThan(NoWork);
        await Assert.That(actual.DistanceEvaluations).IsGreaterThan(NoWork);
        await Assert.That(actual.EdgeVisits).IsGreaterThan(NoWork);
        await Assert.That(actual.ScratchBytesUpperBound).IsGreaterThan(NoReservation);
        await Assert.That(actual.ScratchBytesUpperBound).IsLessThanOrEqualTo(state.Policy.MaxScratchBytes);
    }
}
