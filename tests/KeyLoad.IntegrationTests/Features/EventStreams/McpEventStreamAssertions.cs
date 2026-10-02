using System.Text.Json;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

/// <summary>Checks canonical bounded EventStreams results returned by real SDK callers.</summary>
internal static class McpEventStreamAssertions
{
    /// <summary>Requires canonical page parity, exact event bytes and a valid monotonic quorum cut.</summary>
    /// <param name="scenario">The actual configured stream and expected event batch.</param>
    /// <param name="sdkPage">The page returned by the public .NET SDK.</param>
    /// <param name="mcpPage">The page decoded from the official MCP SDK result.</param>
    /// <param name="expectedEvents">The exact ordered event subset for this page.</param>
    /// <param name="hasMore">Whether the bounded page must advertise a remaining event.</param>
    /// <param name="appendReceiptPosition">The committed position the read must cover.</param>
    /// <param name="previousCutPosition">The prior successful read cut which this read must not precede.</param>
    /// <returns>The later physical cut observed by the official MCP read.</returns>
    internal static async Task<long> EqualPageAsync(McpEventStreamScenario scenario, StreamPage sdkPage,
        StreamPage mcpPage, EventData[] expectedEvents, bool hasMore, long appendReceiptPosition,
        long previousCutPosition)
    {
        await Assert.That(JsonDefaults.Serialize(sdkPage.Events).AsSpan()
            .SequenceEqual(JsonDefaults.Serialize(mcpPage.Events))).IsTrue();
        await Assert.That(mcpPage.Stream).IsEqualTo(scenario.Stream);
        await Assert.That(mcpPage.Stream).IsEqualTo(sdkPage.Stream);
        await Assert.That(mcpPage.Head.TailRevision).IsEqualTo(McpEventStreamTokens.LastEventRevision);
        await Assert.That(mcpPage.Head.FirstAvailableRevision).IsEqualTo(McpEventStreamTokens.FirstEventRevision);
        await Assert.That(mcpPage.Head.Generation).IsEqualTo(McpEventStreamTokens.StreamGeneration);
        await Assert.That(mcpPage.Head).IsEqualTo(sdkPage.Head);
        await Assert.That(sdkPage.CutPosition).IsGreaterThanOrEqualTo(appendReceiptPosition);
        await Assert.That(mcpPage.CutPosition).IsGreaterThanOrEqualTo(appendReceiptPosition);
        await Assert.That(mcpPage.CutPosition).IsGreaterThanOrEqualTo(sdkPage.CutPosition);
        await Assert.That(sdkPage.CutPosition).IsGreaterThanOrEqualTo(previousCutPosition);
        await Assert.That(mcpPage.CutPosition).IsGreaterThanOrEqualTo(previousCutPosition);
        await Assert.That(mcpPage.HasMore).IsEqualTo(hasMore);
        await Assert.That(mcpPage.HasMore).IsEqualTo(sdkPage.HasMore);
        await Assert.That(mcpPage.Events.Select(record => record.Data.EventId).SequenceEqual(
            expectedEvents.Select(data => data.EventId))).IsTrue();
        await Assert.That(mcpPage.Events.Select(record => record.Data).SequenceEqual(expectedEvents)).IsTrue();
        await Assert.That(mcpPage.Events.Select(record => record.EventSequence).SequenceEqual(
            expectedEvents.Select(data => (long)McpEventStreamScenario.ExpectedEvents.IndexOf(data)
                + McpEventStreamTokens.FirstExpectedSequence))).IsTrue();
        await Assert.That(mcpPage.Events.All(record => record.Stream == scenario.Stream)).IsTrue();
        await Assert.That(mcpPage.Events.All(record => record.RecordedAt != default)).IsTrue();
        return mcpPage.CutPosition;
    }

    /// <summary>Requires a public SDK problem to omit caller credentials and private event payloads.</summary>
    /// <param name="problem">The actual SDK problem object.</param>
    /// <param name="credential">The private persisted API key secret.</param>
    /// <param name="privateValue">The private event payload marker.</param>
    /// <returns>The completed safe-error assertions.</returns>
    internal static async Task DoesNotDiscloseProblemAsync(object? problem, string credential, string privateValue)
    {
        var text = JsonSerializer.Serialize(problem);
        await Assert.That(text.Contains(credential, StringComparison.Ordinal)).IsFalse();
        await Assert.That(text.Contains(privateValue, StringComparison.Ordinal)).IsFalse();
    }
}
