using KeyLoad.Server;
using P = KeyLoad.UnitTests.Features.ResourceExecution.OperationalAnalyticsNativeProtocol;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class OperationalAnalyticsNativeAssertions
{
    internal static async Task ReportAsync(OperationalAnalyticsOwner analytics, DateTimeOffset from,
        DateTimeOffset to, bool originalOwner, CancellationToken token)
    {
        var report = await analytics.ReadAsync(from, to, token);
        await ReportValueAsync(report, from, to, originalOwner);
    }

    internal static async Task ReportValueAsync(OperationalAnalyticsReport report, DateTimeOffset from,
        DateTimeOffset to, bool originalOwner)
    {
        await Assert.That(report.From).IsEqualTo(from);
        await Assert.That(report.To).IsEqualTo(to);
        await Assert.That(report.NativePlanComplete).IsTrue();
        await Assert.That(report.Rows.Sum(row => row.Samples)).IsEqualTo(P.Samples);
        await Assert.That(report.Rows.Sum(row => row.CommittedSum)).IsEqualTo(P.PositionSum);
        await Assert.That(report.Rows.Sum(row => row.AppliedSum)).IsEqualTo(P.AppliedSum);
        await Assert.That(report.Rows.Sum(row => row.BacklogSum)).IsEqualTo(P.BacklogSum);
        foreach (var row in report.Rows)
        {
            await Assert.That(row.Hour.Offset).IsEqualTo(TimeSpan.Zero);
            await Assert.That(row.Hour.UtcTicks % TimeSpan.TicksPerHour).IsEqualTo((long)P.NoValue);
            await Assert.That(row.Hour >= from && row.Hour < to).IsTrue();
            await Assert.That(row.Samples).IsGreaterThan((double)P.NoValue);
            await Assert.That(row.CommittedSum).IsGreaterThanOrEqualTo(row.AppliedSum);
            await Assert.That(row.BacklogSum).IsEqualTo(row.CommittedSum - row.AppliedSum);
        }
        await Assert.That(report.Rows.Select(row => row.Hour).Distinct().Count()).IsEqualTo(report.Rows.Length);
        if (originalOwner)
        {
            await Assert.That(report.Counters.Accepted).IsEqualTo((long)P.Samples);
            await Assert.That(report.Counters.Processed).IsEqualTo((long)P.Samples);
        }
        await Assert.That(report.Counters.Dropped).IsEqualTo((long)P.NoValue);
        await Assert.That(report.Counters.Errors).IsEqualTo((long)P.NoValue);
        await Assert.That(report.Counters.QueueLength).IsEqualTo((long)P.NoValue);
        var bytes = JsonDefaults.Serialize(report);
        await Assert.That(bytes.Length).IsLessThanOrEqualTo(P.MaximumReportBytes);
        var text = System.Text.Encoding.UTF8.GetString(bytes);
        await Assert.That(text.Contains(P.PrivateDocument, StringComparison.Ordinal)
            || text.Contains(P.PrivateVoter, StringComparison.Ordinal) || text.Contains(P.PrivateLeader, StringComparison.Ordinal)).IsFalse();
    }

    internal static async Task PrivateStorageAsync(OperationalAnalyticsNativeOwner owner)
    {
        var derived = Path.Combine(owner.NodeDirectory, P.DerivedDirectory);
        await Assert.That(Directory.Exists(derived)).IsTrue();
        await Assert.That(Directory.Exists(Path.Combine(derived, P.RawDirectory))).IsFalse();
        await Assert.That(Directory.Exists(Path.Combine(derived, P.SessionDirectory))).IsFalse();
    }

    internal static async Task CanonicalAsync(OperationalAnalyticsNativeOwner owner, OperationalAnalyticsNativeCut cut)
    {
        var db = owner.Canonical;
        var replay = db.SubmitIssued(cut.Original).Get<CommitReceipt>();
        // Public JSON contract equality; original persisted bytes are conserved by Rows below.
        await Assert.That(JsonDefaults.Serialize(replay).AsSpan()
            .SequenceEqual(JsonDefaults.Serialize(cut.Receipt))).IsTrue();
        await Assert.That(replay.Mutations.All(mutation => mutation.CompositionReferences.IsEmpty)).IsTrue();
        var document = db.Database.GetDocument(P.Principal, new(db.Partition, P.Collection, P.Document));
        var expected = new DocumentResult(new(db.Partition, P.Collection, P.Document), P.Revision, P.Json, false, []);
        await Assert.That(JsonDefaults.Serialize(document).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        await Assert.That(OperationalAnalyticsNativeSeed.Rows(db).SequenceEqual(cut.Canonical, StringComparer.Ordinal)).IsTrue();
        await Assert.That(db.Store.Position).IsEqualTo(cut.Position);
        await Assert.That(db.ReadJournalCut()).IsEqualTo(cut.Journal);
        var identity = db.Store.Identity;
        await Assert.That(identity.FormatVersion).IsEqualTo(cut.Identity.FormatVersion);
        await Assert.That(identity.KeyCodecVersion).IsEqualTo(cut.Identity.KeyCodecVersion);
        await Assert.That(identity.NodeId).IsEqualTo(cut.Identity.NodeId);
        await Assert.That(identity.Incarnation).IsEqualTo(cut.Identity.Incarnation);
        await Assert.That(identity.SigningKey.Span.SequenceEqual(cut.Identity.SigningKey.Span)).IsTrue();
        await Assert.That(identity.Durability).IsEqualTo(cut.Identity.Durability);
        await Assert.That(identity.DispatchPaused).IsEqualTo(cut.Identity.DispatchPaused);
        await Assert.That(identity.ReadGeneration).IsEqualTo(cut.Identity.ReadGeneration);
        await Assert.That(identity.MinimumReaderContract).IsEqualTo(cut.Identity.MinimumReaderContract);
    }
}
