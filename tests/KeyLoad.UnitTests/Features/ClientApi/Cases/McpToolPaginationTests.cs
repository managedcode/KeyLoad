using System.Text.Json;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.ClientApi;

/// <summary>AC-MCP-001/003/007: actual native discovery pages retain the sole catalog under an inclusive byte ceiling.</summary>
internal sealed class McpToolPaginationTests
{
    private const string ChangedInputSchemaJson = "{\"type\":\"object\",\"properties\":{\"isolationMarker\":{\"type\":\"string\"}},\"additionalProperties\":false}";

    /// <summary>Traversal visits all 47 catalog entries once, in order, using nonempty bounded pages and a terminal null cursor.</summary>
    [Test]
    public async Task NativePagesTraverseTheCompleteCatalogExactlyOnce()
    {
        var names = new List<string>();
        string? cursor = null;
        var ended = false;
        await Assert.That(McpOperationCatalog.Entries.Length).IsEqualTo(McpPaginationTestData.CatalogCount);
        for (var count = 0; count < McpPaginationTestData.CatalogCount; count++)
        {
            var page = McpToolPagination.Create(cursor, McpPaginationTestData.MaximumPageBytes);
            await Assert.That(page.Tools.Count).IsGreaterThan(0);
            await Assert.That(McpPaginationTestData.NativeBytes(page).Length).IsLessThanOrEqualTo(McpPaginationTestData.MaximumPageBytes);
            names.AddRange(page.Tools.Select(tool => tool.Name));
            cursor = page.NextCursor;
            if (cursor is null)
            { ended = true; break; }
        }
        await Assert.That(ended).IsTrue();
        await Assert.That(names.Count).IsEqualTo(McpPaginationTestData.CatalogCount);
        await Assert.That(names.Distinct(StringComparer.Ordinal).Count()).IsEqualTo(McpPaginationTestData.CatalogCount);
        await Assert.That(names.SequenceEqual(McpOperationCatalog.Entries.Select(entry => entry.Name))).IsTrue();
    }

    /// <summary>The first singleton accepts its exact complete serialized size and returns the next catalog index.</summary>
    [Test]
    public async Task ExactNativePageBudgetIncludesItsNextCursor()
    {
        var expected = McpPaginationTestData.Singleton(0);
        var exactBytes = McpPaginationTestData.NativeBytes(expected);
        var actual = McpToolPagination.Create(null, exactBytes.Length);
        await Assert.That(actual.Tools.Count).IsEqualTo(1);
        await Assert.That(actual.NextCursor).IsEqualTo(expected.NextCursor);
        await Assert.That(McpPaginationTestData.NativeBytes(actual).AsSpan().SequenceEqual(exactBytes)).IsTrue();
        expected.NextCursor = null;
        var withoutCursorBytes = McpPaginationTestData.NativeBytes(expected);
        await Assert.That(withoutCursorBytes.Length).IsLessThan(exactBytes.Length);
        var error = Assert.ThrowsExactly<KeyLoadException>(() => McpToolPagination.Create(null, withoutCursorBytes.Length));
        await Assert.That(error.Code).IsEqualTo(ErrorCode.ResourceExhausted);
    }

    /// <summary>A singleton which cannot fit is never silently omitted or replaced with an empty advancing page.</summary>
    [Test]
    public async Task UnfittableSingletonReturnsSafeResourceExhausted()
    {
        var exactBytes = McpPaginationTestData.NativeBytes(McpPaginationTestData.Singleton(0));
        var error = Assert.ThrowsExactly<KeyLoadException>(() => McpToolPagination.Create(null, exactBytes.Length - 1));
        await Assert.That(error.Code).IsEqualTo(ErrorCode.ResourceExhausted);
        await Assert.That(error.Message).DoesNotContain(McpPaginationTestData.Marker);
    }

    /// <summary>Strict positive cursor indices resume at their exact catalog position and the final page has no cursor.</summary>
    /// <param name="index">The accepted invariant decimal catalog position.</param>
    [Test]
    [Arguments(1)]
    [Arguments(36)]
    [Arguments(McpPaginationTestData.FinalIndex)]
    public async Task CanonicalCursorResumesAtExactIndex(int index)
    {
        var expected = McpPaginationTestData.Singleton(index);
        var page = McpToolPagination.Create(McpPaginationTestData.Cursor(index), McpPaginationTestData.NativeBytes(expected).Length);
        await Assert.That(page.Tools.Count).IsEqualTo(1);
        await Assert.That(page.Tools[0].Name).IsEqualTo(McpOperationCatalog.Entries[index].Name);
        await Assert.That(page.NextCursor).IsEqualTo(expected.NextCursor);
    }

    /// <summary>Tools, annotations and the page collection are freshly owned without changing the immutable catalog schemas.</summary>
    [Test]
    public async Task NativePageMutationCannotAlterLaterDiscovery()
    {
        var descriptor = McpOperationCatalog.Entries[0];
        var maximum = McpPaginationTestData.NativeBytes(McpPaginationTestData.Singleton(0)).Length;
        var first = McpToolPagination.Create(null, maximum);
        var second = McpToolPagination.Create(null, maximum);
        var changed = first.Tools[0];
        await Assert.That(ReferenceEquals(changed, second.Tools[0])).IsFalse();
        await Assert.That(ReferenceEquals(changed.Annotations, second.Tools[0].Annotations)).IsFalse();
        changed.Name = McpPaginationTestData.Marker;
        changed.Description = McpPaginationTestData.Marker;
        using var changedSchemaDocument = JsonDocument.Parse(ChangedInputSchemaJson);
        changed.InputSchema = changedSchemaDocument.RootElement.Clone();
        changed.Annotations!.ReadOnlyHint = !descriptor.ReadOnly;
        first.Tools.Clear();
        await Assert.That(JsonElement.DeepEquals(changed.InputSchema, descriptor.InputSchema)).IsFalse();
        await Assert.That(second.Tools.Count).IsEqualTo(1);
        await Assert.That(second.Tools[0].Name).IsEqualTo(descriptor.Name);
        await Assert.That(second.Tools[0].Description).IsEqualTo(descriptor.Description);
        await Assert.That(second.Tools[0].Annotations!.ReadOnlyHint).IsEqualTo(descriptor.ReadOnly);
        await Assert.That(JsonElement.DeepEquals(second.Tools[0].InputSchema, descriptor.InputSchema)).IsTrue();
    }

    /// <summary>Nonpositive native page capacities fail before any page is returned.</summary>
    /// <param name="maximumBytes">The invalid inclusive byte capacity.</param>
    [Test]
    [Arguments(0)]
    [Arguments(-1)]
    public void PageCapacityMustBePositive(int maximumBytes)
        => Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => McpToolPagination.Create(null, maximumBytes));
}

/// <summary>AC-MCP-003/005/007: malformed or out-of-range discovery cursors never appear in safe failures.</summary>
internal sealed class McpToolCursorTests
{
    /// <summary>Only the canonical prefix and an unsigned positive invariant decimal position are accepted.</summary>
    /// <param name="cursor">The concrete malformed or out-of-range cursor.</param>
    [Test]
    [Arguments(McpPaginationTestData.EmptyCursor)]
    [Arguments(McpPaginationTestData.Prefix)]
    [Arguments(McpPaginationTestData.ZeroCursor)]
    [Arguments(McpPaginationTestData.LeadingZeroCursor)]
    [Arguments(McpPaginationTestData.PlusCursor)]
    [Arguments(McpPaginationTestData.MinusCursor)]
    [Arguments(McpPaginationTestData.LeadingSpaceCursor)]
    [Arguments(McpPaginationTestData.TrailingSpaceCursor)]
    [Arguments(McpPaginationTestData.CaseCursor)]
    [Arguments(McpPaginationTestData.FractionCursor)]
    [Arguments(McpPaginationTestData.NonAsciiCursor)]
    [Arguments(McpPaginationTestData.IntegerMaximumCursor)]
    [Arguments(McpPaginationTestData.OverflowCursor)]
    [Arguments(McpPaginationTestData.MarkerCursor)]
    public async Task MalformedCursorUsesOneFixedSafeValidation(string cursor)
    {
        var error = Assert.ThrowsExactly<KeyLoadException>(() => McpToolPagination.Create(cursor, McpPaginationTestData.MaximumPageBytes));
        var baseline = Assert.ThrowsExactly<KeyLoadException>(() =>
            McpToolPagination.Create(McpPaginationTestData.Prefix, McpPaginationTestData.MaximumPageBytes));
        await Assert.That(error.Code).IsEqualTo(ErrorCode.Validation);
        await Assert.That(error.Message).IsEqualTo(baseline.Message);
        await Assert.That(error.Message).DoesNotContain(McpPaginationTestData.Marker);
    }

    /// <summary>The exclusive catalog end remains invalid when new public tools are appended.</summary>
    [Test]
    public async Task CatalogEndUsesOneFixedSafeValidation()
        => await MalformedCursorUsesOneFixedSafeValidation(McpPaginationTestData.Cursor(McpOperationCatalog.Entries.Length));
}
