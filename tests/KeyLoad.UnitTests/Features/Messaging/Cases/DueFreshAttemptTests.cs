namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed class DueFreshAttemptTests
{
    private const string CreatorId = DueFreshAttemptAssertions.CreatorId;
    private const int DueSeconds = 1;
    private const int RevokedAttemptSeconds = 2;
    private const int RestorePrincipalSeconds = 3;
    private const int OriginalRetrySeconds = 4;
    private const int FreshAttemptSeconds = 5;
    private const int OriginalReplaySeconds = 6;
    private const int FreshReplaySeconds = 7;

    [Test]
    public async Task PersistedDenialRemainsTerminalButFreshAuthorizedAttemptEmitsOnce()
    {
        var policy = DueFreshAttemptAssertions.ProtectedPolicy();
        using var fixture = new RecurringSagaDatabase(fields: [policy], headers: [policy]);
        var creator = DueFreshAttemptAssertions.Creator();
        fixture.AddPrincipal(creator);
        var scheduleId = Guid.NewGuid();
        var firstDue = RecurringSagaDatabase.Epoch.AddSeconds(DueSeconds);
        await SeedScheduleAsync(fixture, scheduleId, firstDue);
        var scheduleBytes = DueFreshAttemptAssertions.ScheduleBytes(fixture, scheduleId);
        var emptyCounters = DueFreshAttemptAssertions.Counters(fixture);
        var outbox = fixture.Database.GetOutboxStatus(RecurringSagaDatabase.RootPrincipal,
            fixture.Partition).Head;

        await RevokeCreatorAsync(fixture, creator);
        var originalId = Guid.NewGuid();
        await Assert.That(originalId).IsNotEqualTo(Guid.Empty);
        var original = DueFreshAttemptAssertions.Emit(fixture, scheduleId, originalId);
        var denial = DueFreshAttemptAssertions.Apply(fixture, original, originalId,
            RecurringSagaDatabase.Epoch.AddSeconds(RevokedAttemptSeconds));
        await DueFreshAttemptAssertions.AssertDeniedAsync(fixture, originalId, denial);
        await DueFreshAttemptAssertions.AssertNoEffectsAsync(fixture, scheduleId, scheduleBytes,
            emptyCounters, outbox);

        var restored = await RestoreCreatorAsync(fixture);
        var originalRetry = DueFreshAttemptAssertions.Apply(fixture, original, originalId,
            RecurringSagaDatabase.Epoch.AddSeconds(OriginalRetrySeconds));
        await DueFreshAttemptAssertions.AssertDeniedAsync(fixture, originalId, originalRetry);
        await AssertPolicyEpochAdvancedAsync(creator, restored);
        await DueFreshAttemptAssertions.AssertNoEffectsAsync(fixture, scheduleId, scheduleBytes,
            emptyCounters, outbox);

        var freshId = Guid.NewGuid();
        await Assert.That(freshId).IsNotEqualTo(Guid.Empty);
        await Assert.That(freshId).IsNotEqualTo(originalId);
        var fresh = DueFreshAttemptAssertions.Emit(fixture, scheduleId, freshId);
        var committed = DueFreshAttemptAssertions.Apply(fixture, fresh, freshId,
            RecurringSagaDatabase.Epoch.AddSeconds(FreshAttemptSeconds));
        var committedOutbox = await DueFreshAttemptAssertions.AssertOneEmissionAsync(fixture, scheduleId, firstDue,
            committed, outbox);

        var originalAgain = DueFreshAttemptAssertions.Apply(fixture, original, originalId,
            RecurringSagaDatabase.Epoch.AddSeconds(OriginalReplaySeconds));
        await DueFreshAttemptAssertions.AssertDeniedAsync(fixture, originalId, originalAgain);
        await DueFreshAttemptAssertions.AssertOneEmissionAsync(fixture, scheduleId, firstDue,
            committed, outbox, committedOutbox);

        var freshReplay = DueFreshAttemptAssertions.Apply(fixture, fresh, freshId,
            RecurringSagaDatabase.Epoch.AddSeconds(FreshReplaySeconds));
        await DueFreshAttemptAssertions.AssertReplayAsync(committed, freshReplay);
        await DueFreshAttemptAssertions.AssertOneEmissionAsync(fixture, scheduleId, firstDue,
            freshReplay, outbox, committedOutbox);
    }

    private static async Task SeedScheduleAsync(RecurringSagaDatabase fixture, Guid scheduleId,
        DateTimeOffset firstDue)
    {
        var definition = new RecurringScheduleDefinition(fixture.Queue, scheduleId, firstDue,
            TimeSpan.FromHours(1), "UTC", RecurringMisfirePolicy.CatchUp,
            DueFreshAttemptAssertions.Payload, DueFreshAttemptAssertions.Headers);
        var receipt = fixture.CommitAs(CreatorId, fixture.Partition, RecurringSagaDatabase.Epoch,
            new ConfigureRecurringSchedule(definition, 0));
        await Assert.That(receipt.Mutations).HasSingleItem();
        await Assert.That(receipt.Mutations[0].Revision).IsEqualTo(1L);
    }

    private static async Task RevokeCreatorAsync(RecurringSagaDatabase fixture, PrincipalRecord creator)
    {
        fixture.AddPrincipal(creator with { FieldGrants = [RecurringSagaDatabase.FieldWriteGrant] },
            RecurringSagaDatabase.Epoch.AddSeconds(DueSeconds));
        var persisted = fixture.Principal(CreatorId);
        await Assert.That(persisted.FieldGrants).IsEquivalentTo([RecurringSagaDatabase.FieldWriteGrant]);
    }

    private static async Task<PrincipalRecord> RestoreCreatorAsync(RecurringSagaDatabase fixture)
    {
        var current = fixture.Principal(CreatorId);
        fixture.AddPrincipal(current with
        {
            FieldGrants = [RecurringSagaDatabase.FieldWriteGrant, RecurringSagaDatabase.RawUseGrant]
        }, RecurringSagaDatabase.Epoch.AddSeconds(RestorePrincipalSeconds));
        var restored = fixture.Principal(CreatorId);
        await Assert.That(restored.FieldGrants).IsEquivalentTo(
            [RecurringSagaDatabase.FieldWriteGrant, RecurringSagaDatabase.RawUseGrant]);
        return restored;
    }

    private static async Task AssertPolicyEpochAdvancedAsync(PrincipalRecord original, PrincipalRecord restored)
    {
        await Assert.That(restored.PolicyEpoch).IsGreaterThan(original.PolicyEpoch);
    }
}
