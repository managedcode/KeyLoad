using System.Text.Json;
using System.Text.Json.Nodes;

namespace KeyLoad.UnitTests.Features.ReleaseDelivery;

internal sealed class ReleaseVersionTests
{
    private const string Sha = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string BaseVersion = "0.1.0-dev";
    private const string Timestamp = "2026-10-03T12:00:00.000Z";
    private const string Date = "261003";
    private const string RunId = "700001";
    private const string Repository = "managedcode/KeyLoad";
    private const string ReservationError = "E_RELEASE_RESERVATION";
    private const string DateError = "E_RELEASE_DATE";
    private const string DailyRunsError = "E_RELEASE_DAILY_RUNS";
    private const string TagsError = "E_RELEASE_TAGS";
    private const string ExhaustedError = "E_RELEASE_SEQUENCE_EXHAUSTED";

    [Test]
    public async Task AcRel001CreatesCanonicalDatedVersionAndClrVersions()
    {
        var result = await ResolveAsync(Request(), TestContext.Current!.Execution.CancellationToken);
        var expectedVersion = "0.1." + Date + ".1";
        await Assert.That(result.GetProperty(ReleaseVersionFields.Version).GetString()).IsEqualTo(expectedVersion);
        await Assert.That(result.GetProperty(ReleaseVersionFields.Tag).GetString()).IsEqualTo("v0.1." + Date + ".1");
        await Assert.That(result.GetProperty(ReleaseVersionFields.AssemblyVersion).GetString()).IsEqualTo("0.1.0.0");
        await Assert.That(result.GetProperty(ReleaseVersionFields.FileVersion).GetString()).IsEqualTo("0.1.0.1");
        await Assert.That(result.GetProperty(ReleaseVersionFields.SourceRevision).GetString()).IsEqualTo(Sha);
        await Assert.That(result.GetProperty(ReleaseVersionFields.RunId).GetString()).IsEqualTo(RunId);
        await Assert.That(result.GetProperty(ReleaseVersionFields.SchemaVersion).GetInt32()).IsEqualTo(2);
        await Assert.That(result.GetProperty(ReleaseVersionFields.Repository).GetString()).IsEqualTo(Repository);
        await Assert.That(result.GetProperty(ReleaseVersionFields.Date).GetString()).IsEqualTo(Date);
        await Assert.That(result.GetProperty(ReleaseVersionFields.Sequence).GetInt32()).IsEqualTo(1);
        await Assert.That(result.GetProperty(ReleaseVersionFields.PackageVersion).GetString())
            .IsEqualTo(expectedVersion + "-dev");
    }

    [Test]
    public async Task AcRel001UsesDailyOrdinalAndMatchingTagInventoryWithoutCrossDateOrBaseCollisions()
    {
        var request = Request();
        request[ReleaseVersionFields.DailyRuns] = Runs(("700000", 10, "2026-10-03T08:00:00Z"),
            (RunId, 11, Timestamp));
        request[ReleaseVersionFields.Tags] = new JsonArray("v0.1." + Date + ".4", "v0.2." + Date + ".99", "v0.1.261002.99");
        var result = await ResolveAsync(request, TestContext.Current!.Execution.CancellationToken);
        await Assert.That(result.GetProperty(ReleaseVersionFields.Sequence).GetInt32()).IsEqualTo(5);

        request[ReleaseVersionFields.UtcTimestamp] = "2027-01-01T00:01:00Z";
        request[ReleaseVersionFields.DailyRuns] = Runs((RunId, 11, "2027-01-01T00:01:00Z"));
        var rollover = await ResolveAsync(request, TestContext.Current!.Execution.CancellationToken);
        await Assert.That(rollover.GetProperty(ReleaseVersionFields.Version).GetString()).IsEqualTo("0.1.270101.1");

        request[ReleaseVersionFields.BaseVersion] = "0.3.0-dev";
        request[ReleaseVersionFields.UtcTimestamp] = Timestamp;
        request[ReleaseVersionFields.DailyRuns] = Runs((RunId, 11, Timestamp));
        var newBase = await ResolveAsync(request, TestContext.Current!.Execution.CancellationToken);
        await Assert.That(newBase.GetProperty(ReleaseVersionFields.Version).GetString()).IsEqualTo("0.3." + Date + ".1");
    }

    [Test]
    public async Task AcRel002ReusesMatchingReservationAndRejectsForeignIdentity()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        var request = Request();
        var reservation = await ResolveAsync(request, token);
        request[ReleaseVersionFields.Reservation] = JsonNode.Parse(reservation.GetRawText());
        request[ReleaseVersionFields.Tags] = new JsonArray("v0.1." + Date + ".1");
        var retry = await ResolveAsync(request, token);
        await Assert.That(retry.GetRawText()).IsEqualTo(reservation.GetRawText());

        request[ReleaseVersionFields.SourceRevision] = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        await AssertFailureAsync(request, ReservationError, token);

        request = Request();
        request[ReleaseVersionFields.Reservation] = JsonNode.Parse(reservation.GetRawText());
        request[ReleaseVersionFields.RunId] = "700002";
        request[ReleaseVersionFields.DailyRuns] = Runs(("700002", 12, Timestamp));
        await AssertFailureAsync(request, ReservationError, token);

        request = Request();
        var invalidReservation = JsonNode.Parse(reservation.GetRawText())!.AsObject();
        invalidReservation[ReleaseVersionFields.Foreign] = true;
        request[ReleaseVersionFields.Reservation] = invalidReservation;
        await AssertFailureAsync(request, ReservationError, token);

        request = Request();
        invalidReservation = JsonNode.Parse(reservation.GetRawText())!.AsObject();
        invalidReservation[ReleaseVersionFields.PackageVersion] = "0.1." + Date + ".1";
        request[ReleaseVersionFields.Reservation] = invalidReservation;
        await AssertFailureAsync(request, ReservationError, token);

        request = Request();
        invalidReservation = JsonNode.Parse(reservation.GetRawText())!.AsObject();
        invalidReservation[ReleaseVersionFields.SchemaVersion] = 1;
        request[ReleaseVersionFields.Reservation] = invalidReservation;
        await AssertFailureAsync(request, ReservationError, token);

        request = Request();
        invalidReservation = JsonNode.Parse(reservation.GetRawText())!.AsObject();
        invalidReservation.Remove(ReleaseVersionFields.PackageVersion);
        request[ReleaseVersionFields.Reservation] = invalidReservation;
        await AssertFailureAsync(request, ReservationError, token);
    }

    [Test]
    public async Task AcRel002RejectsMalformedDateAndIncompleteOrDuplicateRunInventories()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        var request = Request();
        request[ReleaseVersionFields.UtcTimestamp] = "2026-10-03T12:00:00+00:00";
        await AssertFailureAsync(request, DateError, token);

        request = Request();
        request[ReleaseVersionFields.DailyRuns] = Runs(("700002", 12, Timestamp));
        await AssertFailureAsync(request, DailyRunsError, token);

        request = Request();
        request[ReleaseVersionFields.DailyRuns] = Runs((RunId, 11, Timestamp), (RunId, 12, Timestamp));
        await AssertFailureAsync(request, DailyRunsError, token);
    }

    [Test]
    public async Task AcRel002RejectsMalformedDuplicateAndExhaustedMatchingTags()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        var request = Request();
        request[ReleaseVersionFields.Tags] = new JsonArray("v0.1." + Date + ".01");
        await AssertFailureAsync(request, TagsError, token);

        request = Request();
        request[ReleaseVersionFields.Tags] = new JsonArray("v0.1." + Date + ".1", "v0.1." + Date + ".1");
        await AssertFailureAsync(request, TagsError, token);

        request = Request();
        request[ReleaseVersionFields.Tags] = new JsonArray("v0.1." + Date + ".65534");
        await AssertFailureAsync(request, ExhaustedError, token);
    }

    private static JsonObject Request() => new()
    {
        [ReleaseVersionFields.BaseVersion] = BaseVersion,
        [ReleaseVersionFields.SourceRevision] = Sha,
        [ReleaseVersionFields.RunId] = RunId,
        [ReleaseVersionFields.UtcTimestamp] = Timestamp,
        [ReleaseVersionFields.Tags] = new JsonArray(),
        [ReleaseVersionFields.DailyRuns] = Runs((RunId, 11, Timestamp)),
    };

    private static JsonArray Runs(params (string Id, int Number, string CreatedAt)[] runs) => new(
        runs.Select(run => (JsonNode?)new JsonObject
        {
            [ReleaseVersionFields.Id] = run.Id,
            [ReleaseVersionFields.RunNumber] = run.Number,
            [ReleaseVersionFields.CreatedAt] = run.CreatedAt,
        }).ToArray());

    private static async Task<JsonElement> ResolveAsync(JsonObject request, CancellationToken token)
    {
        var envelope = await ReleaseVersionNodeProcess.RunAsync(request, token);
        await Assert.That(envelope.TryGetProperty(ReleaseVersionFields.Result, out _)).IsTrue();
        return envelope.GetProperty(ReleaseVersionFields.Result);
    }

    private static async Task AssertFailureAsync(JsonObject request, string code, CancellationToken token)
    {
        var envelope = await ReleaseVersionNodeProcess.RunAsync(request, token);
        await Assert.That(envelope.GetProperty(ReleaseVersionFields.Error).GetString()).IsEqualTo(code);
    }
}
