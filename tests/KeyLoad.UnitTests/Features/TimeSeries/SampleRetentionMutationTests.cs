using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.TimeSeries;

internal sealed class SampleRetentionMutationTests
{
    [Test]
    public async Task AcSeries013ExpiryAdvancesFloorInBoundedResumablePagesAndReplayIsStable()
    {
        using var db = new TestDatabase();
        SampleAggregateTestData.Configure(db);
        var start = SampleAggregateTestData.Start;
        var floor = start.AddSeconds(3);
        var offsetFloor = floor.ToOffset(TimeSpan.FromHours(2));
        SampleAggregateTestData.Append(db,
            SampleAggregateTestData.Data(1, start, 1),
            SampleAggregateTestData.Data(2, start.AddSeconds(1), 2),
            SampleAggregateTestData.Data(3, start.AddSeconds(2), 3),
            SampleAggregateTestData.Data(4, floor, 4));

        var commandId = Guid.NewGuid();
        var evaluatedAt = TimeProvider.System.GetUtcNow();
        var command = new CommandRequest(commandId, db.Partition,
            [new ExpireSamples(SampleAggregateTestData.Set, SampleAggregateTestData.Series, offsetFloor, 1)]);
        var first = db.Submit(OperationKind.Batch, command, id: commandId, time: evaluatedAt).Get<CommitReceipt>();
        var positionAfterFirst = db.Store.Position;
        var replay = db.Submit(OperationKind.Batch, command, id: commandId, time: evaluatedAt).Get<CommitReceipt>();
        var positionAfterReplay = db.Store.Position;
        var firstStatus = Status(db);

        var second = Expire(db, offsetFloor, 1);
        var secondStatus = Status(db);
        var third = Expire(db, offsetFloor, 1);
        var completeStatus = Status(db);

        await Assert.That(replay).IsEqualTo(first);
        await Assert.That(positionAfterReplay).IsEqualTo(positionAfterFirst);
        await Assert.That(firstStatus.Before).IsEqualTo(floor);
        await Assert.That(firstStatus.PurgedCount).IsEqualTo(1L);
        await Assert.That(firstStatus.HasMore).IsTrue();
        await Assert.That(second.Mutations.Length).IsEqualTo(1);
        await Assert.That(secondStatus.PurgedCount).IsEqualTo(2L);
        await Assert.That(secondStatus.HasMore).IsTrue();
        await Assert.That(third.Mutations.Length).IsEqualTo(1);
        await Assert.That(completeStatus.PurgedCount).IsEqualTo(3L);
        await Assert.That(completeStatus.HasMore).IsFalse();
    }

    [Test]
    public async Task AcSeries013OnlySeriesManagerCanExpireAndReadersNeedSeriesReadForStatus()
    {
        using var db = new TestDatabase();
        SampleAggregateTestData.Configure(db);
        db.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(new("series-manager", db.Partition.TenantId,
            [new(db.Partition.DatabaseId, SampleAggregateTestData.Set, Capability.SeriesManage)], [])));
        db.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(new("series-reader", db.Partition.TenantId,
            [new(db.Partition.DatabaseId, SampleAggregateTestData.Set, Capability.SeriesRead)], [])));
        var cutoff = SampleAggregateTestData.Start;
        var manager = new CommandRequest(Guid.NewGuid(), db.Partition,
            [new ExpireSamples(SampleAggregateTestData.Set, SampleAggregateTestData.Series, cutoff)]);

        var accepted = db.Submit(OperationKind.Batch, manager, principal: "series-manager", id: manager.CommandId);
        var readerCommandId = Guid.NewGuid();
        var forbidden = db.Submit(OperationKind.Batch, manager with { CommandId = readerCommandId },
            principal: "series-reader", id: readerCommandId);
        var status = db.Database.ReadSampleRetention("series-reader",
            new(db.Partition, SampleAggregateTestData.Set, SampleAggregateTestData.Series));
        var deniedStatus = Assert.ThrowsExactly<KeyLoadException>(() => db.Database.ReadSampleRetention("series-manager",
            new(db.Partition, SampleAggregateTestData.Set, SampleAggregateTestData.Series)));

        await Assert.That(accepted.Error).IsNull();
        await Assert.That(forbidden.Error).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(status.Before).IsEqualTo(cutoff);
        await Assert.That(deniedStatus.Code).IsEqualTo(ErrorCode.PermissionDenied);
    }

    [Test]
    public async Task AcSeries013FutureBackwardsAndInvalidPagesRejectWithoutChangingState()
    {
        using var db = new TestDatabase();
        SampleAggregateTestData.Configure(db);
        var now = SampleAggregateTestData.Start.AddHours(3);
        var firstCut = SampleAggregateTestData.Start.AddHours(1);
        Expire(db, firstCut, SampleRetentionDefaults.MaximumDeletes, now);
        var statusBefore = Status(db);

        var future = Failure(db, new(SampleAggregateTestData.Set, SampleAggregateTestData.Series,
            now.AddTicks(1)), now, ErrorCode.Validation);
        var backwards = Failure(db, new(SampleAggregateTestData.Set, SampleAggregateTestData.Series,
            firstCut.AddTicks(-1)), now, ErrorCode.RevisionConflict);
        var zero = Failure(db, new(SampleAggregateTestData.Set, SampleAggregateTestData.Series,
            firstCut, 0), now, ErrorCode.BudgetExceeded);
        var overLimit = Failure(db, new(SampleAggregateTestData.Set, SampleAggregateTestData.Series,
            firstCut, SampleRetentionDefaults.MaximumDeletes + 1), now, ErrorCode.BudgetExceeded);
        var statusAfter = Status(db);

        await Assert.That(future.Code).IsEqualTo(ErrorCode.Validation);
        await Assert.That(backwards.Code).IsEqualTo(ErrorCode.RevisionConflict);
        await Assert.That(zero.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(overLimit.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(statusAfter).IsEqualTo(statusBefore);

        using var limited = new TestDatabase(new DatabaseLimits { MaxScanRecords = 1 });
        SampleAggregateTestData.Configure(limited);
        var constrained = Failure(limited, new(SampleAggregateTestData.Set, SampleAggregateTestData.Series,
            SampleAggregateTestData.Start, 2), SampleAggregateTestData.Start.AddHours(1), ErrorCode.BudgetExceeded);
        await Assert.That(constrained.Code).IsEqualTo(ErrorCode.BudgetExceeded);
    }

    private static SampleRetentionStatus Status(TestDatabase db)
        => db.Database.ReadSampleRetention(SampleAggregateTestData.RootPrincipal,
            new(db.Partition, SampleAggregateTestData.Set, SampleAggregateTestData.Series));

    private static CommitReceipt Expire(TestDatabase db, DateTimeOffset before, int maximumDeletes,
        DateTimeOffset? now = null)
    {
        var commandId = Guid.NewGuid();
        return db.Submit(OperationKind.Batch, new CommandRequest(commandId, db.Partition,
            [new ExpireSamples(SampleAggregateTestData.Set, SampleAggregateTestData.Series, before, maximumDeletes)]),
            id: commandId, time: now).Get<CommitReceipt>();
    }

    private static KeyLoadException Failure(TestDatabase db, ExpireSamples mutation, DateTimeOffset now, ErrorCode expected)
    {
        var id = Guid.NewGuid();
        var error = Assert.ThrowsExactly<KeyLoadException>(() => db.Submit(OperationKind.Batch,
            new CommandRequest(id, db.Partition, [mutation]), id: id, time: now).Get<CommitReceipt>());
        if (error.Code != expected)
        {
            throw new InvalidOperationException($"Expected {expected} but received {error.Code}.", error);
        }

        return error;
    }
}
