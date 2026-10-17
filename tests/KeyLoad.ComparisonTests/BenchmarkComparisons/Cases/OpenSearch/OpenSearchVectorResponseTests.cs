using System.Text.Json;
using KeyLoad.Comparisons;
using KeyLoad.Comparisons.Targets;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal sealed class OpenSearchVectorResponseTests
{
    private const string EnvelopePrefix = "{\"timed_out\":false,\"_shards\":{\"failed\":0,\"successful\":1},\"aggregations\":{\"exact_neighbors\":{\"value\":";
    private const string EnvelopeSuffix = "}}}";
    private const string Projected = "{\"nested\":{\"number\":1.25},\"items\":[true,null,\"value\"]}";
    private const string PrecisionRows = "[{\"id\":\"d000002289\",\"score\":0.27605485016755293,\"payload\":{\"value\":2289}},{\"id\":\"d000001272\",\"score\":0.2760548285997265,\"payload\":{\"value\":1272}}]";
    private const string ProjectionRows = "[{\"id\":\"a\",\"score\":-0.5,\"payload\":" + Projected + "},{\"id\":\"b\",\"score\":-0.5,\"payload\":{}}]";

    /// <summary>AC-BC-FAIL-010: native double distinctions survive reading and projection retains nested JSON values.</summary>
    [Test]
    public async Task ResponsePreservesDoubleOrderingAndExactJsonProjection()
    {
        using var precision = JsonDocument.Parse(EnvelopePrefix + PrecisionRows + EnvelopeSuffix);
        var rows = OpenSearchSearchResponse.Read(precision.RootElement, 2);
        await Assert.That(rows.Select(row => row.Id)).IsEquivalentTo(["d000002289", "d000001272"]);
        await Assert.That(rows[0].Id).IsEqualTo("d000002289");
        using var projection = JsonDocument.Parse(EnvelopePrefix + ProjectionRows + EnvelopeSuffix);
        var projected = OpenSearchSearchResponse.Read(projection.RootElement, 2);
        await Assert.That(projected[0].Id).IsEqualTo("a");
        await Assert.That(projected[1].Id).IsEqualTo("b");
        await Assert.That(BenchmarkDataset.SameJson(projected[0].Json, Projected)).IsTrue();
    }

    /// <summary>AC-BC-FAIL-010: authentic empty native states produce no neighbors, without inventing a score.</summary>
    [Test]
    public async Task ResponseAcceptsEmptyNeighbors()
    {
        using var json = JsonDocument.Parse(EnvelopePrefix + "[]" + EnvelopeSuffix);
        await Assert.That(OpenSearchSearchResponse.Read(json.RootElement, 10)).IsEmpty();
    }

    [Test]
    public async Task ResponseUsesOrdinalTieOrderForEitherSignedZero()
    {
        const string rows = "[{\"id\":\"a\",\"score\":-0.0,\"payload\":{}},{\"id\":\"b\",\"score\":0.0,\"payload\":{}}]";
        using var json = JsonDocument.Parse(EnvelopePrefix + rows + EnvelopeSuffix);
        var neighbors = OpenSearchSearchResponse.Read(json.RootElement, 2);
        await Assert.That(neighbors.Select(row => row.Id).SequenceEqual(["a", "b"], StringComparer.Ordinal)).IsTrue();
    }

    /// <summary>AC-BC-FAIL-010: corrupt native aggregation rows never become measured neighbors.</summary>
    [Test]
    [Arguments("null")]
    [Arguments("{}")]
    [Arguments("[1]")]
    [Arguments("[{\"id\":\"a\",\"score\":1e999,\"payload\":{}}]")]
    [Arguments("[{\"id\":\"a\",\"score\":null,\"payload\":{}}]")]
    [Arguments("[{\"id\":\"a\",\"score\":\"NaN\",\"payload\":{}}]")]
    [Arguments("[{\"id\":\"a\",\"payload\":{}}]")]
    [Arguments("[{\"score\":1,\"payload\":{}}]")]
    [Arguments("[{\"id\":\"a\",\"score\":1}]")]
    [Arguments("[{\"id\":\"a\",\"score\":1,\"payload\":null}]")]
    [Arguments("[{\"id\":\"a\",\"score\":1,\"payload\":[]}]")]
    [Arguments("[{\"id\":\"a\",\"score\":0,\"payload\":{}},{\"id\":\"b\",\"score\":1,\"payload\":{}}]")]
    [Arguments("[{\"id\":\"b\",\"score\":1,\"payload\":{}},{\"id\":\"a\",\"score\":1,\"payload\":{}}]")]
    [Arguments("[{\"id\":\"a\",\"score\":1,\"payload\":{}},{\"id\":\"a\",\"score\":0,\"payload\":{}}]")]
    public async Task ResponseRejectsMalformedNonFiniteOrUnorderedRows(string rows)
    {
        using var json = JsonDocument.Parse(EnvelopePrefix + rows + EnvelopeSuffix);
        await Assert.That(() => OpenSearchSearchResponse.Read(json.RootElement, 2)).Throws<ComparisonFailureException>();
    }

    [Test]
    public async Task ResponseRejectsMoreThanRequestedTopK()
    {
        using var json = JsonDocument.Parse(EnvelopePrefix + PrecisionRows + EnvelopeSuffix);
        await Assert.That(() => OpenSearchSearchResponse.Read(json.RootElement, 1)).Throws<ComparisonFailureException>();
    }

    /// <summary>AC-BC-FAIL-010: incomplete, timed-out or failed native shard responses retain their failure contract.</summary>
    [Test]
    [Arguments("{}")]
    [Arguments("{\"timed_out\":true}")]
    [Arguments("{\"timed_out\":false,\"_shards\":{\"failed\":1,\"successful\":1}}")]
    [Arguments("{\"timed_out\":false,\"_shards\":{\"failed\":0,\"successful\":0}}")]
    [Arguments("{\"timed_out\":false,\"_shards\":{\"failed\":0,\"successful\":-1}}")]
    [Arguments("{\"timed_out\":false,\"_shards\":{\"failed\":0,\"successful\":1}}")]
    [Arguments("{\"timed_out\":false,\"_shards\":{\"failed\":0,\"successful\":1},\"aggregations\":{}}")]
    public async Task ResponseRejectsIncompleteOrFailedNativeEnvelope(string response)
    {
        using var json = JsonDocument.Parse(response);
        await Assert.That(() => OpenSearchSearchResponse.Read(json.RootElement, 2)).Throws<ComparisonFailureException>();
    }
}
