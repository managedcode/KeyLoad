using System.Security.Cryptography;
using KeyLoad.Query;
using KeyLoad.UnitTests.Features.Messaging;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Search;

internal static class NativeTextWaitObservationAssertions
{
    internal static async Task RequireImageAsync(TestDatabase database, string[] image, long position)
    {
        await Assert.That(database.Store.Position).IsEqualTo(position);
        await Assert.That(QueueWholeFlowStorage.Bytes(database.Store)).IsEquivalentTo(image, CollectionOrdering.Matching);
    }

    internal static async Task RequireAsync(TestDatabase database, NativeTextWaitObservationObserver observer,
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
        await Assert.That(actual.NativeRequestDigest).IsEqualTo(
            Convert.ToHexStringLower(SHA256.HashData(NativeSerialization.Serialize(request))));
        await Assert.That(NativeSerialization.Serialize(actual.AppliedToken).SequenceEqual(
            NativeSerialization.Serialize(result.AppliedToken))).IsTrue();
        await Assert.That(result.AppliedToken.Position).IsNotEqualTo(position);
        await Assert.That(observer.Image!).IsEquivalentTo(image, CollectionOrdering.Matching);
        await RequireImageAsync(database, image, position);
    }

    internal static async Task RequirePagesAsync(TestDatabase database, SearchEngine plain, string principal,
        CancellationToken token)
    {
        await NativeTextBilingualAudit.VerifyAsync(plain, database.Partition, token);
        var request = NativeTextBilingualAudit.Request(database.Partition, NativeTextBilingualAudit.EnglishQuery);
        var original = await plain.SearchAsync("root", request, token);
        var actual = await plain.SearchAsync(principal, request, token);
        await Assert.That(JsonDefaults.Serialize(actual).SequenceEqual(JsonDefaults.Serialize(original))).IsTrue();
    }
}
