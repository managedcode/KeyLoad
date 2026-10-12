namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class PhysicalOwnerRegistrationIdentityOutcomeFlow
{
    private static readonly Guid RefusedId = Guid.Parse("5c015339-0b99-40f7-a07d-5588ec7c59d4");
    private static readonly Guid HealthyId = Guid.Parse("9ef89ca2-3813-4d9a-8b5f-c7520431eb87");
    private static readonly Guid ContinuationId = Guid.Parse("70c0fb45-ecc2-421b-a6a4-a8ff882f1c0c");
    private static readonly Guid ForeignIncarnation = Guid.Parse("ca32fdf7-1bd1-4415-b9c4-35a7a37c73f4");

    internal static async Task RunAsync()
    {
        var failures = new List<Exception>();
        await KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            using var fixture = new PhysicalShardCatalogFixture();
            await KeyLoad.Server.ServerFailureObserver.ObserveAsync(() => ExecuteAsync(fixture), failures);
        }, failures);
        KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task ExecuteAsync(PhysicalShardCatalogFixture fixture)
    {
        PhysicalOwnerDirectoryWholeFlow.Bootstrap(fixture);
        var registration = Operation(fixture, PhysicalOwnerDirectoryWholeFlow.Request(),
            PhysicalOwnerDirectoryWholeFlow.RegistrationId);
        var registered = fixture.Database.Apply(registration);
        await Assert.That(registered.Error).IsNull();
        var directory = PhysicalOwnerDirectoryWholeFlow.DirectoryBytes(fixture);
        var catalog = fixture.ReadCatalogBytes();
        var before = PhysicalOwnerDirectoryWholeFlow.Image(fixture);
        var request = PhysicalOwnerDirectoryWholeFlow.Request(PhysicalOwnerDirectoryWholeFlow.InitialRevision);
        var changed = request with
        {
            Destination = request.Destination with
            { Owner = request.Destination.Owner with { Incarnation = ForeignIncarnation } }
        };
        var refused = Operation(fixture, changed, RefusedId);
        var refusal = fixture.Database.Apply(refused);
        await PhysicalOwnerRegistrationIdentityOutcomeAssertions.RefusedAsync(refusal);
        await PhysicalOwnerRegistrationIdentityOutcomeAssertions.OnlyOutcomeAndEvaluatedClockAddedAsync(fixture, before, RefusedId, refused.EvaluatedAt);
        await PhysicalOwnerRegistrationIdentityOutcomeAssertions.AuthorityAsync(fixture, directory, catalog);
        await FirstColdAsync(fixture, registration, registered, refused, refusal, request);
        await FreshAndSecondColdAsync(fixture, registration, registered, refused, refusal, request, directory, catalog);
    }

    private static async Task FirstColdAsync(PhysicalShardCatalogFixture fixture,
        ReplicatedOperation registration, OperationResult registered, ReplicatedOperation refused,
        OperationResult refusal, RegisterPhysicalOwnerV1 correct)
    {
        var image = PhysicalShardCatalogPolicyOutcomeAssertions.Image(fixture);
        var cut = fixture.Store.Position;
        var outcome = PhysicalOwnerRegistrationIdentityOutcomeAssertions.Outcome(fixture, RefusedId);
        fixture.Reopen();
        await PhysicalShardCatalogPolicyOutcomeAssertions.UnchangedAsync(fixture, image, cut);
        await PhysicalOwnerRegistrationIdentityOutcomeAssertions.ReplayAsync(fixture, refused, refusal);
        await PhysicalOwnerRegistrationIdentityOutcomeAssertions.StoredAsync(fixture, RefusedId, refusal, outcome);
        await PhysicalOwnerRegistrationIdentityOutcomeAssertions.CommandConflictAsync(
            fixture.Database.Apply(Operation(fixture, correct, RefusedId)));
        await PhysicalOwnerRegistrationIdentityOutcomeAssertions.StoredAsync(fixture, RefusedId, refusal, outcome);
        await PhysicalOwnerRegistrationIdentityOutcomeAssertions.ReplayAsync(fixture, registration, registered);
        await PhysicalShardCatalogPolicyOutcomeAssertions.UnchangedAsync(fixture, image, cut);
    }

    private static async Task FreshAndSecondColdAsync(PhysicalShardCatalogFixture fixture,
        ReplicatedOperation registration, OperationResult registered, ReplicatedOperation refused,
        OperationResult refusal, RegisterPhysicalOwnerV1 correct, byte[] directory, byte[] catalog)
    {
        var before = PhysicalOwnerDirectoryWholeFlow.Image(fixture);
        var healthy = Operation(fixture, correct, HealthyId);
        var success = fixture.Database.Apply(healthy);
        await Assert.That(success.Error).IsNull();
        await Assert.That(NativeSerialization.Serialize(success).SequenceEqual(NativeSerialization.Serialize(registered))).IsTrue();
        await PhysicalOwnerRegistrationIdentityOutcomeAssertions.OnlyOutcomeAndEvaluatedClockAddedAsync(fixture, before, HealthyId, healthy.EvaluatedAt);
        await PhysicalOwnerRegistrationIdentityOutcomeAssertions.AuthorityAsync(fixture, directory, catalog);
        var image = PhysicalShardCatalogPolicyOutcomeAssertions.Image(fixture);
        var cut = fixture.Store.Position;
        fixture.Reopen();
        await PhysicalShardCatalogPolicyOutcomeAssertions.UnchangedAsync(fixture, image, cut);
        await PhysicalOwnerRegistrationIdentityOutcomeAssertions.ReplayAsync(fixture, registration, registered);
        await PhysicalOwnerRegistrationIdentityOutcomeAssertions.ReplayAsync(fixture, refused, refusal);
        await PhysicalOwnerRegistrationIdentityOutcomeAssertions.ReplayAsync(fixture, healthy, success);
        await PhysicalShardCatalogPolicyOutcomeAssertions.UnchangedAsync(fixture, image, cut);
        var continuationBefore = PhysicalOwnerDirectoryWholeFlow.Image(fixture);
        var continuationOperation = Operation(fixture, correct, ContinuationId);
        var continuation = fixture.Database.Apply(continuationOperation);
        await Assert.That(continuation.Error).IsNull();
        await Assert.That(NativeSerialization.Serialize(continuation).SequenceEqual(NativeSerialization.Serialize(success))).IsTrue();
        await PhysicalOwnerRegistrationIdentityOutcomeAssertions.OnlyOutcomeAndEvaluatedClockAddedAsync(fixture, continuationBefore, ContinuationId, continuationOperation.EvaluatedAt);
        await PhysicalOwnerRegistrationIdentityOutcomeAssertions.AuthorityAsync(fixture, directory, catalog);
    }

    private static ReplicatedOperation Operation(PhysicalShardCatalogFixture fixture,
        RegisterPhysicalOwnerV1 request, Guid id) => PhysicalOwnerDirectoryWholeFlow.Operation(fixture, request, id);
}
