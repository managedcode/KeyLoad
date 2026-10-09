using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class ReadExecutionBudgetScopedGrantTests
{
    [Test]
    public async Task NativeMetadataAndRawProducerReadsShareExactScopeWhileOrdinaryViewsKeepTheirContract()
    {
        var firstBytes = ReadExecutionBudgetGrantSeed.FirstKey.Length + ReadExecutionBudgetGrantSeed.FirstValue.Length;
        var secondBytes = ReadExecutionBudgetGrantSeed.SecondKey.Length + ReadExecutionBudgetGrantSeed.SecondValue.Length;
        using var database = ReadExecutionBudgetGrantSeed.CreateDatabase();
        var actual = database.Store.Read(view =>
        {
            var work = new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(database.Database.Limits));
            var grant = work.CreateReadGrant(firstBytes + secondBytes, 2);
            bool metadata;
            bool producer;
            using (work.EnterReadGrant(grant))
            {
                metadata = work.CreateView(view).ReadValue(ReadExecutionBudgetGrantSeed.FirstKey, static _ => { });
                producer = view.ReadValue(ReadExecutionBudgetGrantSeed.SecondKey, static _ => { }, work.ChargeNativeReadRecord);
            }
            work.CompleteReadGrant(grant);
            var ordinary = work.CreateView(view).ReadValue(ReadExecutionBudgetGrantSeed.FirstKey, static _ => { });
            return (metadata, producer, ordinary, grant.ReadBytes, grant.ExaminedRecords, Total: work.ReadBytes);
        });
        await Assert.That(actual.metadata).IsTrue();
        await Assert.That(actual.producer).IsTrue();
        await Assert.That(actual.ordinary).IsTrue();
        await Assert.That(actual.ReadBytes).IsEqualTo((long)(firstBytes + secondBytes));
        await Assert.That(actual.ExaminedRecords).IsEqualTo(2);
        await Assert.That(actual.Total).IsEqualTo((long)(firstBytes + secondBytes + firstBytes));
    }

    [Test]
    public async Task FailedNativeReaderRetainsAllowanceAndFreshIndependentReaderContinues()
    {
        var bytes = ReadExecutionBudgetGrantSeed.FirstKey.Length + ReadExecutionBudgetGrantSeed.FirstValue.Length;
        using var database = ReadExecutionBudgetGrantSeed.CreateDatabase(new() { MaxQueryReadBytes = bytes });
        var actual = database.Store.Read(view =>
        {
            var work = new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(database.Database.Limits));
            var grant = work.CreateReadGrant(bytes - 1, 1);
            var consumed = false;
            KeyLoadException failure;
            using (work.EnterReadGrant(grant))
            {
                failure = Assert.ThrowsExactly<KeyLoadException>(() => work.CreateView(view)
                    .ReadValue(ReadExecutionBudgetGrantSeed.FirstKey, _ => consumed = true));
            }
            var reserve = Assert.ThrowsExactly<KeyLoadException>(() => work.CreateReadGrant(2, 0));
            var fresh = new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(database.Database.Limits));
            var freshGrant = fresh.CreateReadGrant(bytes, 1);
            bool found;
            using (fresh.EnterReadGrant(freshGrant))
            { found = fresh.CreateView(view).ReadValue(ReadExecutionBudgetGrantSeed.FirstKey, static _ => { }); }
            fresh.CompleteReadGrant(freshGrant);
            return (failure.Code, Reserve: reserve.Code, consumed, found, grant.IsCompleted,
                Remaining: work.RemainingReadGrantBytes, FreshBytes: fresh.ReadBytes);
        });
        await Assert.That(actual.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(actual.Reserve).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(actual.consumed).IsFalse();
        await Assert.That(actual.IsCompleted).IsFalse();
        await Assert.That(actual.Remaining).IsEqualTo(1L);
        await Assert.That(actual.found).IsTrue();
        await Assert.That(actual.FreshBytes).IsEqualTo((long)bytes);
    }

    [Test]
    public async Task NativeDeadlineCapCannotRenewOriginalLifetimeAndExpiredCapDoesNotAffectFreshRead()
    {
        using var database = ReadExecutionBudgetGrantSeed.CreateDatabase();
        var work = new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(database.Database.Limits));
        var original = work.RemainingLifetime;
        work.ConstrainLifetime(TimeProvider.System.GetUtcNow().Add(original + original));
        await Assert.That(work.RemainingLifetime <= original).IsTrue();
        var expired = Assert.ThrowsExactly<KeyLoadException>(() => work.ConstrainLifetime(TimeProvider.System.GetUtcNow().AddTicks(-1)));
        await Assert.That(expired.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        var fresh = new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(database.Database.Limits));
        var found = database.Store.Read(view => fresh.CreateView(view).ReadValue(ReadExecutionBudgetGrantSeed.FirstKey, static _ => { }));
        await Assert.That(found).IsTrue();
    }
}
