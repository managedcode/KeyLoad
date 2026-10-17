using System.Runtime.InteropServices;
using System.Text;
using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class PartitionRecordOwnedCutContinuation
{
    private const int First = 0;
    private const int Second = 1;
    private const int Third = 2;
    private const byte PoisonBit = 0x40;
    private const long OneByte = 1;
    private const string UpdatedValue = "value-b-current\0Одеса";
    private const string ForeignValue = "foreign-native-value";
    private const string ForeignTenant = "tenant-b";
    private const string ForeignSuffix = "foreign";
    private const string ForeignDetail = "The continuation key does not belong to this partition family.";
    private const string RetainedDetail = "The partition record page exceeds its retained-byte budget.";
    private const string ExaminedDetail = "The partition record page exceeds its examined-byte budget.";

    internal static async Task RunAsync(PartitionRecordNativeFixture fixture, PartitionRef partition,
        string family, (byte[] Key, byte[] Value)[] original, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var held = PartitionRecordNativeFixture.Read(fixture.Store, partition, family,
            PartitionRecordOwnedCutAssertions.LimitedRecords, PartitionRecordOwnedCutAssertions.MaximumImageBytes,
            PartitionRecordOwnedCutAssertions.MaximumImageBytes, cancellationToken: token);
        var foreignPartition = partition with
        {
            TenantId = ForeignTenant
        };
        var foreign = PartitionRecordNativeFixture.Row(family, foreignPartition, ForeignSuffix, ForeignValue);
        var changed = (Key: original[Second].Key, Value: Encoding.UTF8.GetBytes(UpdatedValue));
        PartitionRecordNativeFixture.Seed(fixture.Store, changed, foreign);
        var selected = new[] { original[First], changed, original[Third] };
        var image = selected.Append(foreign).ToArray();
        var position = fixture.Store.Position;
        var identity = NativeSerialization.Serialize(fixture.Store.Identity);
        await PartitionRecordOwnedCutAssertions.HeldAsync(held, original);
        await PartitionRecordOwnedCutAssertions.ImageAsync(fixture.Store, image, position, token);
        Poison(held);
        await RefuseAsync(fixture, partition, family, image, selected, position,
            ErrorCode.Validation, ForeignDetail, held.Continuation!.Value, token);
        await RefuseAsync(fixture, partition, family, image, selected, position,
            ErrorCode.Validation, ForeignDetail, foreign.Key, token);
        await BudgetAsync(fixture, partition, family, image, selected, position, token);
        for (var cold = PartitionRecordOwnedCutAssertions.FirstCold;
            cold < PartitionRecordOwnedCutAssertions.ColdCuts; cold++)
        {
            token.ThrowIfCancellationRequested();
            fixture.Reopen();
            await Assert.That(NativeSerialization.Serialize(fixture.Store.Identity).AsSpan().SequenceEqual(identity)).IsTrue();
            await PartitionRecordOwnedCutAssertions.ImageAsync(fixture.Store, image, position, token);
            await PartitionRecordOwnedCutAssertions.RetainedSecondAsync(held, original);
            await PartitionRecordOwnedCutAssertions.FullAsync(fixture.Store, partition, family, selected, position, token);
            await PartitionRecordOwnedCutAssertions.FullAsync(fixture.Store, foreignPartition, family, [foreign], position, token);
        }
    }

    private static void Poison(PartitionRecordPage page)
    {
        var key = Buffer(page.Records[First].Key);
        var value = Buffer(page.Records[First].Value);
        var continuation = Buffer(page.Continuation!.Value);
        var lastKey = Buffer(page.Records[Second].Key);
        if (ReferenceEquals(key.Array, value.Array) || ReferenceEquals(key.Array, continuation.Array)
            || ReferenceEquals(value.Array, continuation.Array) || ReferenceEquals(lastKey.Array, continuation.Array))
        {
            throw new InvalidOperationException("Native page buffers must have independent ownership.");
        }
        key.Array![key.Offset] ^= PoisonBit;
        value.Array![value.Offset] ^= PoisonBit;
        continuation.Array![continuation.Offset] ^= PoisonBit;
    }

    private static ArraySegment<byte> Buffer(ReadOnlyMemory<byte> memory)
    {
        if (!MemoryMarshal.TryGetArray(memory, out var segment) || segment.Array is null || segment.Count == First)
        {
            throw new InvalidOperationException("The owned native page buffer is unavailable.");
        }
        return segment;
    }

    private static async Task RefuseAsync(PartitionRecordNativeFixture fixture, PartitionRef partition,
        string family, (byte[] Key, byte[] Value)[] image, (byte[] Key, byte[] Value)[] selected,
        long position, ErrorCode code, string detail, ReadOnlyMemory<byte> after, CancellationToken token)
    {
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => PartitionRecordNativeFixture.Read(fixture.Store,
            partition, family, PartitionRecordOwnedCutAssertions.LimitedRecords,
            PartitionRecordOwnedCutAssertions.MaximumImageBytes, PartitionRecordOwnedCutAssertions.MaximumImageBytes,
            after, token));
        await Assert.That(failure.Code).IsEqualTo(code);
        await Assert.That(failure.Message).IsEqualTo(detail);
        await PartitionRecordOwnedCutAssertions.ImageAsync(fixture.Store, image, position, token);
        await PartitionRecordOwnedCutAssertions.FullAsync(fixture.Store, partition, family, selected, position, token);
    }

    private static async Task BudgetAsync(PartitionRecordNativeFixture fixture, PartitionRef partition,
        string family, (byte[] Key, byte[] Value)[] image, (byte[] Key, byte[] Value)[] selected,
        long position, CancellationToken token)
    {
        var retained = selected.Take(PartitionRecordOwnedCutAssertions.LimitedRecords)
            .Sum(row => (long)row.Key.Length + row.Value.Length) + selected[Second].Key.Length;
        var examined = selected.Sum(row => (long)row.Key.Length + row.Value.Length);
        var exact = PartitionRecordNativeFixture.Read(fixture.Store, partition, family,
            PartitionRecordOwnedCutAssertions.LimitedRecords, retained, examined, cancellationToken: token);
        await Assert.That(exact.RetainedBytes).IsEqualTo(retained);
        await Assert.That(exact.ExaminedBytes).IsEqualTo(examined);
        await BudgetRefusalAsync(fixture, partition, family, image, selected, position,
            retained - OneByte, examined, RetainedDetail, token);
        await BudgetRefusalAsync(fixture, partition, family, image, selected, position,
            retained, examined - OneByte, ExaminedDetail, token);
    }

    private static async Task BudgetRefusalAsync(PartitionRecordNativeFixture fixture, PartitionRef partition,
        string family, (byte[] Key, byte[] Value)[] image, (byte[] Key, byte[] Value)[] selected,
        long position, long retained, long examined, string detail, CancellationToken token)
    {
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => PartitionRecordNativeFixture.Read(fixture.Store,
            partition, family, PartitionRecordOwnedCutAssertions.LimitedRecords, retained, examined, cancellationToken: token));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(failure.Message).IsEqualTo(detail);
        await PartitionRecordOwnedCutAssertions.ImageAsync(fixture.Store, image, position, token);
        await PartitionRecordOwnedCutAssertions.FullAsync(fixture.Store, partition, family, selected, position, token);
    }
}
