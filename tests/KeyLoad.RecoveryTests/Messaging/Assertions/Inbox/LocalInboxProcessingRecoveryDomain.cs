using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.RecoveryTests.Features.Messaging;

internal static class LocalInboxProcessingRecoveryDomain
{
    private const int MaximumRows = 4096;
    private static readonly string[] Families = ["document", "document-epoch", "index", "unique", "stream-head", "event",
        "event-id", "event-feed", "event-sequence", "message-body", "message-meta", "queue-counters", "queue-order",
        "ready", "scheduled", "lease", "dead-letter", "inbox", "outbox", "outbox-head"];

    internal static string[] Read(ZoneTreeStore store) => store.Read(view => Families.SelectMany(family =>
    {
        var page = view.Scan(KeySpace.Partition(family, LocalInboxProcessingCrashContract.Partition), MaximumRows);
        if (page.HasMore)
        {
            throw new InvalidOperationException("The local processing domain exceeded its complete bounded native image.");
        }
        return page.Records.Select(row => Convert.ToHexString(row.Key.Span) + ":" + Convert.ToHexString(row.Value.Span));
    }).ToArray());

    internal static async Task SameAsync(ZoneTreeStore store, string[] image)
        => await Assert.That(Read(store)).IsEquivalentTo(image, CollectionOrdering.Matching);
}
