using System.Security.Cryptography;
using KeyLoad.Query;
using KeyLoad.UnitTests.Features.Messaging;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Search;

internal static class TextWaitObservationAssertions
{
    internal static async Task RequireImageAsync(TestDatabase database, string[] image, long position)
    {
        await Assert.That(database.Store.Position).IsEqualTo(position);
        await Assert.That(QueueWholeFlowStorage.Bytes(database.Store)).IsEquivalentTo(image, CollectionOrdering.Matching);
    }

    internal static async Task RequireAsync(TestDatabase database, TextWaitObservationObserver observer,
        WaitForIndexRequest request, WaitForIndexResult result, string principal, string[] image, long position)
    {
        await Assert.That(observer.Count).IsEqualTo(1);
        var actual = observer.Value ?? throw new InvalidOperationException("The original wait observation is absent.");
        await Assert.That(actual.NodeId).IsEqualTo(database.Store.Identity.NodeId);
        await Assert.That(actual.Incarnation).IsEqualTo(database.Store.Identity.Incarnation);
        await Assert.That(actual.ReadGeneration).IsEqualTo(database.Store.Identity.ReadGeneration);
        await Assert.That(actual.StorePosition).IsEqualTo(position);
        await Assert.That(actual.PrincipalId).IsEqualTo(principal);
        await Assert.That(actual.Partition).IsEqualTo(request.Partition);
        await Assert.That(actual.PolicyEpoch).IsEqualTo(result.PolicyEpoch);
        await Assert.That(actual.SchemaVersion).IsEqualTo(result.SchemaVersion);
        await Assert.That(actual.RuntimeRequestDigest).IsEqualTo(
            Convert.ToHexStringLower(SHA256.HashData(OrleansSerialization.Serialize(request))));
        await Assert.That(OrleansSerialization.Serialize(actual.AppliedToken).SequenceEqual(
            OrleansSerialization.Serialize(result.AppliedToken))).IsTrue();
        await Assert.That(result.AppliedToken.Position).IsNotEqualTo(position);
        await Assert.That(observer.Image!).IsEquivalentTo(image, CollectionOrdering.Matching);
        await RequireImageAsync(database, image, position);
    }

    internal static async Task RequirePagesAsync(TestDatabase database, SearchEngine plain, string principal,
        CancellationToken token)
    {
        await TextBilingualAudit.VerifyAsync(plain, database.Partition, token);
        var request = TextBilingualAudit.Request(database.Partition, TextBilingualAudit.EnglishQuery);
        var original = await plain.SearchAsync("root", request, token);
        var actual = await plain.SearchAsync(principal, request, token);
        await Assert.That(JsonDefaults.Serialize(actual).SequenceEqual(JsonDefaults.Serialize(original))).IsTrue();
    }
}
