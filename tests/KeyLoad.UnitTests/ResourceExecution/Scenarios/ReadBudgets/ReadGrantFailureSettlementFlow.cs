using KeyLoad.Core;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class ReadGrantFailureSettlementFlow
{
    private const int RuntimeRecords = 2;
    private const int OneRecord = 1;
    private const int ExtraByte = 1;
    private const long NoAvailableBytes = 0L;
    private const string GrantParameter = "grant";
    private const string ConsumerFailure = "The original execution read consumer failed.";

    internal static async Task RunAsync()
    {
        var maximum = FirstBytes + SecondBytes;
        var failures = new List<Exception>();
        try
        {
            using var fixture = ReadExecutionBudgetGrantSeed.CreateDatabase(new()
            {
                MaxQueryReadBytes = maximum,
                MaxScanRecords = RuntimeRecords
            });
            ZoneTreeStore? cold = null;
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var original = TenantAdmissionAssertions.State(fixture.Store);
                var position = fixture.Store.Position;
                using var cancellation = new CancellationTokenSource();
                var actual = fixture.Store.Read(view => FailAndSettle(view, fixture, cancellation));
                await AssertFailureAsync(actual, cancellation.Token);
                await HealthyAsync(fixture.Store, maximum);
                await Assert.That(TenantAdmissionAssertions.State(fixture.Store).SequenceEqual(original)).IsTrue();
                await Assert.That(fixture.Store.Position).IsEqualTo(position);
                fixture.Store.Dispose();
                cold = new(new(fixture.Directory), fixture.StorageExecution, UnitExecutionOptions.PointCacheExecution());
                await Assert.That(TenantAdmissionAssertions.State(cold).SequenceEqual(original)).IsTrue();
                await Assert.That(cold.Position).IsEqualTo(position);
                await HealthyAsync(cold, maximum);
                await Assert.That(TenantAdmissionAssertions.State(cold).SequenceEqual(original)).IsTrue();
                await Assert.That(cold.Position).IsEqualTo(position);
            }, failures);
            ServerFailureObserver.Observe(() => cold?.Dispose(), failures);
        }
        catch (Exception failure) when (global::KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
        catch (Exception failure) when (!global::KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static FailureObservation FailAndSettle(global::KeyLoad.Storage.IKeyValueView view,
        TestDatabase fixture, CancellationTokenSource cancellation)
    {
        var budget = fixture.CreateReadWork(cancellation.Token);
        var grant = budget.CreateReadGrant(FirstBytes + SecondBytes, RuntimeRecords);
        var originalFailure = new IOException(ConsumerFailure);
        ArgumentException? active = null;
        KeyLoadException? refused = null;
        byte[]? actual = null;
        var thrown = Assert.ThrowsExactly<IOException>(() =>
        {
            using var scope = budget.EnterReadGrant(grant);
            grant.ReadValue(view, ReadExecutionBudgetGrantSeed.FirstKey, value =>
            {
                actual = value.ToArray();
                active = Assert.ThrowsExactly<ArgumentException>(() => budget.CompleteSettledReadGrant(grant));
                refused = Assert.ThrowsExactly<KeyLoadException>(() => budget.CreateReadGrant(ExtraByte, OneRecord));
                throw originalFailure;
            });
        });
        var held = budget.RemainingReadGrantBytes;
        var stillRefused = Assert.ThrowsExactly<KeyLoadException>(() => budget.CreateReadGrant(ExtraByte, OneRecord));
        cancellation.Cancel();
        var cancelled = Assert.ThrowsExactly<OperationCanceledException>(() => budget.CompleteReadGrant(grant));
        var before = budget.RemainingReadGrantBytes;
        budget.CompleteSettledReadGrant(grant);
        var after = budget.RemainingReadGrantBytes;
        var records = budget.RemainingReadGrantRecords;
        var duplicate = Assert.ThrowsExactly<ArgumentException>(() => budget.CompleteSettledReadGrant(grant));
        return new(originalFailure, thrown, active, refused, stillRefused, actual, held, before, after,
            budget.RemainingReadGrantBytes, records, budget.ReadBytes, grant.ReadBytes,
            grant.ExaminedRecords, grant.IsCompleted, duplicate.ParamName, cancelled.CancellationToken);
    }

    private static async Task AssertFailureAsync(FailureObservation actual, CancellationToken token)
    {
        await Assert.That(ReferenceEquals(actual.Original, actual.Thrown)).IsTrue();
        await Assert.That(actual.Active?.ParamName).IsEqualTo(GrantParameter);
        await Assert.That(actual.Refused?.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(actual.StillRefused.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(actual.Value).IsNotNull();
        await Assert.That(actual.Value!.SequenceEqual(ReadExecutionBudgetGrantSeed.FirstValue)).IsTrue();
        await Assert.That(actual.Held).IsEqualTo(NoAvailableBytes);
        await Assert.That(actual.Before).IsEqualTo(NoAvailableBytes);
        await Assert.That(actual.After).IsEqualTo((long)SecondBytes);
        await Assert.That(actual.AfterDuplicate).IsEqualTo((long)SecondBytes);
        await Assert.That(actual.RemainingRecords).IsEqualTo(OneRecord);
        await Assert.That(actual.TotalBytes).IsEqualTo((long)FirstBytes);
        await Assert.That(actual.GrantBytes).IsEqualTo((long)FirstBytes);
        await Assert.That(actual.Examined).IsEqualTo(OneRecord);
        await Assert.That(actual.Completed).IsTrue();
        await Assert.That(actual.Cancelled).IsEqualTo(token);
        await Assert.That(actual.DuplicateParameter).IsEqualTo(GrantParameter);
    }

    private static async Task HealthyAsync(ZoneTreeStore store, int maximum)
    {
        var actual = store.Read(view =>
        {
            var budget = new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(new()
            {
                MaxQueryReadBytes = maximum,
                MaxScanRecords = RuntimeRecords
            }));
            var grant = budget.CreateReadGrant(maximum, RuntimeRecords);
            byte[]? first = null;
            byte[]? second = null;
            using (budget.EnterReadGrant(grant))
            {
                grant.ReadValue(view, ReadExecutionBudgetGrantSeed.FirstKey, value => first = value.ToArray());
                grant.ReadValue(view, ReadExecutionBudgetGrantSeed.SecondKey, value => second = value.ToArray());
            }
            budget.CompleteReadGrant(grant);
            var refusal = Assert.ThrowsExactly<KeyLoadException>(() => budget.CreateReadGrant(ExtraByte, OneRecord));
            return (first, second, budget.ReadBytes, grant.ExaminedRecords, refusal.Code);
        });
        await Assert.That(actual.first).IsNotNull();
        await Assert.That(actual.second).IsNotNull();
        await Assert.That(actual.first!.SequenceEqual(ReadExecutionBudgetGrantSeed.FirstValue)).IsTrue();
        await Assert.That(actual.second!.SequenceEqual(ReadExecutionBudgetGrantSeed.SecondValue)).IsTrue();
        await Assert.That(actual.ReadBytes).IsEqualTo((long)maximum);
        await Assert.That(actual.ExaminedRecords).IsEqualTo(RuntimeRecords);
        await Assert.That(actual.Code).IsEqualTo(ErrorCode.BudgetExceeded);
    }

    private static int FirstBytes => ReadExecutionBudgetGrantSeed.FirstKey.Length + ReadExecutionBudgetGrantSeed.FirstValue.Length;
    private static int SecondBytes => ReadExecutionBudgetGrantSeed.SecondKey.Length + ReadExecutionBudgetGrantSeed.SecondValue.Length;

    private sealed record FailureObservation(IOException Original, IOException Thrown, ArgumentException? Active,
        KeyLoadException? Refused, KeyLoadException StillRefused, byte[]? Value, long Held, long Before,
        long After, long AfterDuplicate, int RemainingRecords, long TotalBytes, long GrantBytes,
        int Examined, bool Completed, string? DuplicateParameter, CancellationToken Cancelled);
}
