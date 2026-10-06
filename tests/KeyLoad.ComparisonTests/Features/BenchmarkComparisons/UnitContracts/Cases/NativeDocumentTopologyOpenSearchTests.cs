using System.Net;
using System.Text.Json;
using KeyLoad.Comparisons;
using KeyLoad.Comparisons.Targets;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

/// <summary>AC-ISO-003/005 uses native response payloads without an HTTP double.</summary>
internal sealed class NativeDocumentTopologyOpenSearchTests
{
    private const string Error = "error";
    private const string Type = "type";
    private const string MissingDocument = "document_missing_exception";
    private const string MissingIndex = "index_not_found_exception";
    private const string CreateConflict = "version_conflict_engine_exception";
    private const string Updated = "updated";
    private const string Deleted = "deleted";
    private const string NotFound = "not_found";
    private const string Noop = "noop";
    private const string Document = "strict-document";
    private const string Json = "{\"nested\":{\"value\":2}}";
    private const string PriorJson = "{\"removed\":true,\"nested\":{\"value\":1,\"old\":true}}";
    private const string Upsert = "doc_as_upsert";
    private const string ScriptedUpsert = "scripted_upsert";
    private const string Script = "script";
    private const string ScriptSource = "source";
    private const string Parameters = "params";
    private const string ReplacementScript = "ctx._source = params.source";
    private const string Removed = "removed";
    private const string Old = "old";
    private const string Nested = "nested";

    [Test]
    public async Task AC_ISO_005_RequiresExactMutationResultAndEveryCopyAcknowledged()
    {
        OpenSearchWriteAcknowledgement.VerifyMutation(HttpStatusCode.OK, Receipt(Updated, 2), Scenario.DocumentUpdate, 2);
        OpenSearchWriteAcknowledgement.VerifyMutation(HttpStatusCode.OK, Receipt(Deleted, 3), Scenario.DocumentDelete, 3);
        Assert.ThrowsExactly<ComparisonFailureException>(() => OpenSearchWriteAcknowledgement.VerifyMutation(HttpStatusCode.OK,
            Receipt(Noop, 2), Scenario.DocumentUpdate, 2));
        Assert.ThrowsExactly<ComparisonFailureException>(() => OpenSearchWriteAcknowledgement.VerifyMutation(HttpStatusCode.OK,
            Receipt(Updated, 1), Scenario.DocumentUpdate, 2));
        var missing = Assert.ThrowsExactly<ComparisonFailureException>(() => OpenSearchWriteAcknowledgement.VerifyMutation(
            HttpStatusCode.NotFound, Receipt(NotFound, 2), Scenario.DocumentDelete, 2));
        await Assert.That(missing.Message).IsEqualTo(ComparisonMutationFailures.DeleteMissing);
    }

    [Test]
    public async Task AC_ISO_005_OnlyNativeSemanticErrorsBecomeExpectedNegatives()
    {
        var missing = Assert.ThrowsExactly<ComparisonFailureException>(() => OpenSearchWriteAcknowledgement.VerifyMutation(
            HttpStatusCode.NotFound, NativeError(MissingDocument), Scenario.DocumentUpdate, 2));
        var conflict = Assert.ThrowsExactly<ComparisonFailureException>(() => OpenSearchWriteAcknowledgement.VerifyMutation(
            HttpStatusCode.Conflict, NativeError(CreateConflict), Scenario.DocumentWrite, 2));
        await Assert.That(missing.Message).IsEqualTo(ComparisonMutationFailures.UpdateMissing);
        await Assert.That(conflict.Message).IsEqualTo(ComparisonMutationFailures.CreateConflict);
        Assert.ThrowsExactly<HttpRequestException>(() => OpenSearchWriteAcknowledgement.VerifyMutation(
            HttpStatusCode.NotFound, NativeError(MissingIndex), Scenario.DocumentUpdate, 2));
        Assert.ThrowsExactly<HttpRequestException>(() => OpenSearchWriteAcknowledgement.VerifyMutation(
            HttpStatusCode.Conflict, NativeError(CreateConflict), Scenario.DocumentUpdate, 2));
    }

    [Test]
    public async Task AC_ISO_005_UpdateBodyExplicitlyDisablesUpsertAndPreservesExactPayload()
    {
        var body = JsonSerializer.SerializeToElement(OpenSearchDocument.CreateUpdate(Document, Json), OpenSearchHttp.JsonOptions);
        await Assert.That(body.GetProperty(Upsert).GetBoolean()).IsFalse();
        await Assert.That(body.GetProperty(ScriptedUpsert).GetBoolean()).IsFalse();
        await Assert.That(body.GetProperty(Script).GetProperty(ScriptSource).GetString()).IsEqualTo(ReplacementScript);
        var stored = OpenSearchDocument.Read(body.GetProperty(Script).GetProperty(Parameters).GetProperty(ScriptSource));
        await Assert.That(stored.Id).IsEqualTo(Document);
        await Assert.That(BenchmarkDataset.SameJson(stored.Json, Json)).IsTrue();
        using var prior = JsonDocument.Parse(PriorJson);
        using var replacement = JsonDocument.Parse(stored.Json);
        await Assert.That(prior.RootElement.TryGetProperty(Removed, out _)).IsTrue();
        await Assert.That(replacement.RootElement.TryGetProperty(Removed, out _)).IsFalse();
        await Assert.That(replacement.RootElement.GetProperty(Nested).TryGetProperty(Old, out _)).IsFalse();
    }

    private static JsonElement Receipt(string result, int copies)
        => JsonSerializer.SerializeToElement(new Dictionary<string, object>
        {
            [OpenSearchNames.Result] = result,
            [OpenSearchNames.ShardsObject] = new Dictionary<string, int>
            {
                [OpenSearchNames.Total] = copies,
                [OpenSearchNames.Successful] = copies,
                [OpenSearchNames.Failed] = 0,
            },
        });

    private static JsonElement NativeError(string type)
        => JsonSerializer.SerializeToElement(new Dictionary<string, object> { [Error] = new Dictionary<string, string> { [Type] = type } });
}
