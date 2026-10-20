using System.Text.Json;
using static KeyLoad.UnitTests.Features.Search.TextProviderAuditProtocol;

namespace KeyLoad.UnitTests.Features.Search;

internal static class TextProviderAuditResults
{
    internal static async Task CurrentAsync(TextProtectedRebuildColdOwner owner, TextProviderAuditState state,
        CancellationToken token)
    {
        await QueryAsync(owner, state.Updated ? UpdatedTerm : UkrainianTerm, Ukrainian,
            state.Updated ? ChangedRevision : InitialRevision, state.Updated ? UpdatedJson : UkrainianJson, token);
        await QueryAsync(owner, EnglishTerm, state.Updated ? null : English, InitialRevision, EnglishJson, token);
        await QueryAsync(owner, ControlTerm, Control, InitialRevision, ControlJson, token);
        if (state.Updated)
        {
            await QueryAsync(owner, UkrainianTerm, null, InitialRevision, UkrainianJson, token);
            await Assert.That(owner.Database.GetDocument(Root, new(owner.Source.Partition, Collection, English), cancellationToken: token)).IsNull();
        }
        if (state.Healthy)
        {
            await QueryAsync(owner, HealthyTerm, Healthy, InitialRevision, HealthyJson, token);
        }
    }

    private static async Task QueryAsync(TextProtectedRebuildColdOwner owner, string term, string? id,
        long revision, string json, CancellationToken token)
    {
        var original = TextProviderAuditImages.Capture(owner);
        var actual = await owner.Search.SearchAsync(Root,
            new(owner.Source.Partition, Collection, Field, term, Limit: QueryLimit), token);
        RankedDocument[] expected = id is null ? [] : [new(new(owner.Reference(new(owner.Source.Partition, Collection, id)),
            revision, json, false, []), SingleBranchScore)];
        await Assert.That(JsonSerializer.Serialize(actual, JsonDefaults.Options))
            .IsEqualTo(JsonSerializer.Serialize(expected, JsonDefaults.Options));
        await TextProviderAuditImages.SameAsync(owner, original);
    }
}
