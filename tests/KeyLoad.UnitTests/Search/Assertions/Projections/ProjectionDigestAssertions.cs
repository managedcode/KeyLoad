using KeyLoad.Core;
using KeyLoad.Core.Features.Search;
using KeyLoad.Server;
using KeyLoad.Storage;
using ManagedCode.Communication;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.Search;

internal static class ProjectionDigestAssertions
{
    private const string Corrupt = "The stored vector projection effect is corrupt.";
    private const double Rank = 1d / 61d;

    internal static async Task<(CommandRequest Command, Result<object> Result)> MalformedOwnerAsync(EventProjectionFixture fixture)
    {
        var request = fixture.Request();
        var malformed = request with
        {
            Target = request.Target with
            {
                Resource = "shadowed-projection-owner"
            }
        };
        var command = new CommandRequest(Guid.NewGuid(), fixture.Partition, [malformed]);
        var domain = ProjectionDigestImage.Domain(fixture);
        var before = fixture.Harness.Store.Position;
        Result<object>? result = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var actual = fixture.Harness.Submit(OperationKind.Batch, command, EventProjectionFixture.WorkerId, command.CommandId);
            result = actual;
            await EqualAsync(actual, Result<object>.Fail(Errors.Problem(ErrorCode.Validation, "The mutation resource and canonical owner differ.")));
            await Assert.That(fixture.Harness.Store.Position).IsEqualTo(before + 1L);
            var outcome = fixture.Harness.Store.Read(view => view.GetRecord<StoredOutcome>(KeySpace.PartitionOutcome(
                fixture.Partition, EventProjectionFixture.WorkerId, command.CommandId)));
            await Assert.That(outcome).IsNotNull();
            await Assert.That(outcome!.PolicyEpoch).IsEqualTo(1L);
            await Assert.That(outcome.Incarnation).IsEqualTo(fixture.Harness.Store.Identity.Incarnation);
            await EqualAsync(outcome.Result, actual);
        }, failures);
        await ServerFailureObserver.ObserveAsync(() => EqualAsync(ProjectionDigestImage.Domain(fixture), domain), failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return (command, result ?? throw new InvalidOperationException("The original malformed-owner operation did not settle."));
    }

    internal static async Task ReceiptAsync(EventProjectionFixture fixture, CommandRequest command,
        CommitReceipt receipt, long before)
    {
        var expected = new CommitReceipt(command.CommandId, new(fixture.Harness.Store.Identity.Incarnation, fixture.Partition.AtomicPartitionId, before + 1L), [new("applyVectorProjection", fixture.TargetCollection, EventProjectionFixture.TargetId, 1L)], DurabilityProfile.ProcessDurable, 1);
        await EqualAsync(receipt, expected);
    }

    internal static async Task RefusedAsync(EventProjectionFixture fixture)
    {
        var error = await Assert.ThrowsExactlyAsync<KeyLoadException>(fixture.SearchAsync);
        await Assert.That(error!.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(error.Message).IsEqualTo(Corrupt);
        AnnSeed? partial = null;
        var seed = Assert.ThrowsExactly<KeyLoadException>(() => partial = Capture(fixture));
        await Assert.That(seed.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(seed.Message).IsEqualTo(Corrupt);
        await Assert.That(partial).IsNull();
    }

    internal static async Task RefusedApplyAsync(EventProjectionFixture fixture, CommandRequest command)
    {
        var refused = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Harness.Submit(OperationKind.Batch,
            command, EventProjectionFixture.WorkerId, command.CommandId).Get<CommitReceipt>());
        await Assert.That(refused.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(refused.Message).IsEqualTo(Corrupt);
    }

    internal static async Task HealthyAsync(EventProjectionFixture fixture, long schema, bool visible = true)
        => await ProjectionDigestImage.ObserveAsync(fixture, async () =>
        {
            RankedDocument[] expected = visible
                ? [new(new(new(fixture.Partition, fixture.TargetCollection, EventProjectionFixture.TargetId),
                    1L, "{}", true, [EventProjectionFixture.InputField, EventProjectionFixture.VectorField]), Rank)] : [];
            await EqualAsync(await fixture.SearchAsync(), expected);
            var beforeTime = fixture.Database.EvaluationClock.GetUtcNow();
            var seed = Capture(fixture);
            var afterTime = fixture.Database.EvaluationClock.GetUtcNow();
            await Assert.That(seed.Scope.EvaluatedAt).IsGreaterThanOrEqualTo(beforeTime);
            await Assert.That(seed.Scope.EvaluatedAt).IsLessThanOrEqualTo(afterTime);
            VectorRecord[] vectors = visible
                ? [new(EventProjectionFixture.TargetId, EventProjectionFixture.VectorField, EventProjectionFixture.Space, [1f, 0f], 1L)] : [];
            await EqualAsync(seed.Records.ToArray(), vectors);
            await Assert.That(seed.Scope.PrincipalId).IsEqualTo(EventProjectionFixture.ReaderId);
            await Assert.That(seed.Scope.PolicyEpoch).IsEqualTo(1L);
            await Assert.That(seed.Scope.Partition).IsEqualTo(fixture.Partition);
            await Assert.That(seed.Scope.Collection).IsEqualTo(fixture.TargetCollection);
            await Assert.That(seed.Scope.Field).IsEqualTo(EventProjectionFixture.VectorField);
            await Assert.That(seed.Scope.Space).IsEqualTo(EventProjectionFixture.Space);
            await Assert.That(seed.Scope.SchemaVersion).IsEqualTo(schema);
            await SeedCutAsync(fixture, seed);
        });

    private static async Task SeedCutAsync(EventProjectionFixture fixture, AnnSeed seed)
    {
        var expected = fixture.Harness.Store.Read(view =>
        {
            var identity = fixture.Harness.Store.Identity;
            var head = fixture.Database.ReadOutboxHead(view, fixture.Partition);
            var applied = view.ReadOwnedValue(KeySpace.AppliedBytes) is { } bytes
                ? OrleansSerialization.Deserialize<long>(bytes) : 0L;
            return new AnnSeedCut(identity.NodeId, identity.Incarnation, identity.FormatVersion,
                identity.KeyCodecVersion, identity.ReadGeneration, fixture.Harness.Store.Position,
                applied, head.Tail, head.FirstAvailable);
        });
        await Assert.That(seed.Cut).IsEqualTo(expected);
    }

    private static AnnSeed Capture(EventProjectionFixture fixture)
        => AnnSeedCollector.Capture(fixture.Database, EventProjectionFixture.ReaderId, fixture.Partition,
            fixture.TargetCollection, EventProjectionFixture.VectorField, EventProjectionFixture.Space,
            Options.Create(new AnnSeedOptions()), new(UnitExecutionOptions.DatabaseLimits(fixture.Database.Limits)));

    internal static async Task EqualAsync<T>(T actual, T expected)
        => await Assert.That(OrleansSerialization.Serialize(actual).AsSpan().SequenceEqual(OrleansSerialization.Serialize(expected))).IsTrue();
}
