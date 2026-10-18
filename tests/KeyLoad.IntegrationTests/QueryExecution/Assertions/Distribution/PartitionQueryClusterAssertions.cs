using System.Collections.Immutable;
using System.Text.Json;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

/// <summary>Checks public pages against seed inputs, never against another returned page.</summary>
internal static class PartitionQueryClusterAssertions
{
    internal static async Task AssertOracleAsync(PartitionQueryPageV1 page,
        PartitionQueryClusterScenario scenario, ImmutableArray<PartitionRef> selected)
    {
        await Assert.That(page.Version).IsEqualTo(PartitionQueryClusterProtocol.Version);
        await Assert.That(page.Complete).IsTrue();
        var expected = scenario.ExpectedRows(selected);
        await Assert.That(page.Rows.Length).IsEqualTo(expected.Length);
        for (var index = 0; index < expected.Length; index++)
        {
            await AssertRowAsync(page.Rows[index], expected[index], scenario.ExpectedReference(expected[index]));
        }
        await AssertLeavesAsync(page.Leaves, selected);
        var expectedDuplicates = expected.Where(row => row.Id == PartitionQueryClusterProtocol.DuplicateTextId)
            .Select(row => row.Partition).Distinct().Count();
        await Assert.That(page.Rows.Where(row => row.Reference.Id == PartitionQueryClusterProtocol.DuplicateTextId)
                .Select(row => row.Reference.Partition).Distinct().Count())
            .IsEqualTo(expectedDuplicates);
    }

    internal static async Task AssertSameLogicalRowsAsync(PartitionQueryPageV1 expected,
        PartitionQueryPageV1 actual)
    {
        await Assert.That(actual.Version).IsEqualTo(expected.Version);
        await Assert.That(actual.Complete).IsEqualTo(expected.Complete);
        await Assert.That(actual.Rows.Length).IsEqualTo(expected.Rows.Length);
        for (var index = 0; index < expected.Rows.Length; index++)
        {
            await Assert.That(actual.Rows[index].Reference).IsEqualTo(expected.Rows[index].Reference);
            await AssertRowEquivalentAsync(actual.Rows[index].Row, expected.Rows[index].Row).ConfigureAwait(false);
        }
        await Assert.That(actual.Leaves.Select(leaf => leaf.Partition))
            .IsEquivalentTo(expected.Leaves.Select(leaf => leaf.Partition));
    }

    private static async Task AssertRowEquivalentAsync(QueryRow actual, QueryRow expected)
    {
        await Assert.That(actual.EntityId).IsEqualTo(expected.EntityId);
        await Assert.That(actual.Revision).IsEqualTo(expected.Revision);
        await Assert.That(actual.Json).IsEqualTo(expected.Json);
        await Assert.That(actual.Redacted).IsEqualTo(expected.Redacted);
        await Assert.That(actual.RedactedFields.HasValue).IsTrue();
        await Assert.That(actual.RedactedFields!.Value.IsDefault).IsFalse();
        await Assert.That(actual.RedactedFields!.Value.IsEmpty).IsTrue();
        await Assert.That(expected.RedactedFields.HasValue).IsTrue();
        await Assert.That(expected.RedactedFields!.Value.IsDefault).IsFalse();
        await Assert.That(expected.RedactedFields!.Value.IsEmpty).IsTrue();
    }

    private static async Task AssertRowAsync(PartitionQueryRowV1 actual,
        PartitionQueryClusterInputRow expected, EntityRef expectedReference)
    {
        await Assert.That(actual.Reference).IsEqualTo(expectedReference);
        await Assert.That(actual.Row.EntityId).IsEqualTo(expected.Id);
        await Assert.That(actual.Row.Revision).IsEqualTo(PartitionQueryClusterProtocol.RowRevision);
        await Assert.That(actual.Row.Redacted).IsFalse();
        using var json = JsonDocument.Parse(actual.Row.Json);
        var root = json.RootElement;
        await Assert.That(root.ValueKind).IsEqualTo(JsonValueKind.Object);
        await Assert.That(root.EnumerateObject().Count()).IsEqualTo(1);
        await Assert.That(root.GetProperty(PartitionQueryClusterProtocol.ValueAlias).GetString())
            .IsEqualTo(expected.Value);
        await Assert.That(root.TryGetProperty(PartitionQueryClusterProtocol.RankJsonProperty, out _)).IsFalse();
    }

    private static async Task AssertLeavesAsync(ImmutableArray<PartitionQueryLeafWitnessV1> leaves,
        ImmutableArray<PartitionRef> selected)
    {
        var expected = selected.OrderBy(partition => partition.TenantId, StringComparer.Ordinal)
            .ThenBy(partition => partition.DatabaseId, StringComparer.Ordinal)
            .ThenBy(partition => partition.TransactionDomainId, StringComparer.Ordinal)
            .ThenBy(partition => partition.PartitionKey, StringComparer.Ordinal).ToArray();
        await Assert.That(leaves.Length).IsEqualTo(expected.Length);
        for (var index = 0; index < expected.Length; index++)
        {
            var leaf = leaves[index];
            await Assert.That(leaf.Partition).IsEqualTo(expected[index]);
            await Assert.That(leaf.CutPosition).IsGreaterThanOrEqualTo(0L);
            await Assert.That(leaf.PolicyEpoch).IsGreaterThanOrEqualTo(0L);
            await Assert.That(leaf.SchemaVersion).IsGreaterThanOrEqualTo(0L);
            await Assert.That(string.IsNullOrWhiteSpace(leaf.AccessPath)).IsFalse();
        }
        await Assert.That(leaves.Select(leaf => leaf.PolicyEpoch).Distinct().Count()).IsEqualTo(1);
    }
}
