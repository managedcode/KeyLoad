using System.Collections.Immutable;
using KeyLoad.Core;
using KeyLoad.CrashHost.Features.ClusterRouting;

namespace KeyLoad.RecoveryTests.Features.ClusterRouting;

internal static class ControlledPartitionMovementNativeProcessColdAssertions
{
    private const int Owners = 6;
    private const int FirstOwner = 0;
    private const int AppliedRowCount = 1;
    private const long FirstNewEntry = 1;
    private const long InitialTerm = 0;
    private const string Separator = ":";
    private const string UnexpectedStartupReplay = "A cold native owner appended an operation absent from its original startup authority.";

    internal static async Task ExactAsync(ImmutableArray<ControlledPartitionMovementNativeProcessSnapshot> before,
        ImmutableArray<ControlledPartitionMovementNativeProcessSnapshot> after)
    {
        var failures = new List<Exception>();
        await KeyLoad.Server.ServerFailureObserver.ObserveAsync(() => ExactOriginalAsync(before, after), failures);
        if (failures.Count > FirstOwner)
        {
            await KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                for (var index = FirstOwner; index < Math.Min(Owners, Math.Min(before.Length, after.Length)); index++)
                {
                    var selected = index;
                    if (before[selected].State.LastIndex != after[selected].State.LastIndex)
                    {
                        await TailWitnessAsync(selected, before[selected], after[selected], failures);
                    }
                }
            }, failures);
        }
        KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task ExactOriginalAsync(ImmutableArray<ControlledPartitionMovementNativeProcessSnapshot> before,
        ImmutableArray<ControlledPartitionMovementNativeProcessSnapshot> after)
    {
        await Assert.That(before.Length).IsEqualTo(Owners);
        await Assert.That(after.Length).IsEqualTo(Owners);
        for (var index = FirstOwner; index < Owners; index++)
        {
            await EqualAsync(before[index].Identity, after[index].Identity);
            await Assert.That(after[index].State.Version).IsEqualTo(before[index].State.Version);
            await Assert.That(after[index].State.Incarnation).IsEqualTo(before[index].State.Incarnation);
            await Assert.That(after[index].State.Term).IsEqualTo(before[index].State.Term);
            await Assert.That(after[index].State.VotedFor).IsEqualTo(before[index].State.VotedFor);
            await Assert.That(after[index].State.LastIndex).IsEqualTo(before[index].State.LastIndex);
            await Assert.That(after[index].State.CommittedIndex).IsEqualTo(before[index].State.CommittedIndex);
            await EqualAsync(before[index].State.Snapshot, after[index].State.Snapshot);
            await EqualAsync(before[index].State, after[index].State);
            await Assert.That(after[index].Position).IsEqualTo(before[index].Position);
            await Assert.That(after[index].Rows.SequenceEqual(before[index].Rows)).IsTrue();
            await EqualAsync(before[index].Entries, after[index].Entries);
        }
    }

    private const string TailPrefix = "KEYLOAD_NATIVE_EXACT_TAIL";
    private const string OwnerField = " owner=";
    private const string BeforeField = " before=";
    private const string AfterField = " after=";
    private const string TermBeforeField = " termBefore=";
    private const string TermAfterField = " termAfter=";
    private const string PositionBeforeField = " positionBefore=";
    private const string PositionAfterField = " positionAfter=";
    private const string EntryField = " entry=";
    private const string HasOperationField = " hasOperation=";
    private const string KindField = " kind=";
    private const string PriorAuthorityField = " priorAuthority=";
    private const string PayloadField = " payloadEqual=";
    private const string OperationField = " operationEqual=";
    private const int MissingOperationKind = -1;

    private static async Task TailWitnessAsync(int owner,
        ControlledPartitionMovementNativeProcessSnapshot before,
        ControlledPartitionMovementNativeProcessSnapshot after, List<Exception> failures)
    {
        await KeyLoad.Server.ServerFailureObserver.ObserveAsync(() => Console.Error.WriteLineAsync(FormattableString.Invariant($"{TailPrefix}{OwnerField}{owner}{BeforeField}{before.State.LastIndex}{AfterField}{after.State.LastIndex}{TermBeforeField}{before.State.Term}{TermAfterField}{after.State.Term}{PositionBeforeField}{before.Position}{PositionAfterField}{after.Position}")), failures);
        foreach (var entry in after.Entries.Where(entry => entry.Index > before.State.LastIndex))
        {
            await KeyLoad.Server.ServerFailureObserver.ObserveAsync(() =>
            {
                var replay = entry.Operation;
                var original = replay is null ? null : before.Entries.Select(item => item.Operation)
                    .FirstOrDefault(item => item is not null && item.Id == replay.Id);
                var payloadEqual = original is not null && replay is not null
                    && original.NativePayload.Span.SequenceEqual(replay.NativePayload.Span);
                var operationEqual = original is not null && replay is not null
                    && JsonDefaults.Serialize(original).AsSpan().SequenceEqual(
                        JsonDefaults.Serialize(replay with
                        {
                            EvaluatedAt = original.EvaluatedAt
                        }));
                var kind = replay is null ? MissingOperationKind : (int)replay.Kind;
                return Console.Error.WriteLineAsync(FormattableString.Invariant($"{TailPrefix}{OwnerField}{owner}{EntryField}{entry.Index}{HasOperationField}{replay is not null}{KindField}{kind}{PriorAuthorityField}{original is not null}{PayloadField}{payloadEqual}{OperationField}{operationEqual}"));
            }, failures);
        }
    }

    internal static async Task ColdAsync(ImmutableArray<ControlledPartitionMovementNativeProcessSnapshot> before,
        ImmutableArray<ControlledPartitionMovementNativeProcessSnapshot> after)
    {
        await Assert.That(before.Length).IsEqualTo(Owners);
        await Assert.That(after.Length).IsEqualTo(Owners);
        for (var index = FirstOwner; index < Owners; index++)
        {
            await OwnerAsync(before[index], after[index]);
        }
    }

    private static async Task OwnerAsync(ControlledPartitionMovementNativeProcessSnapshot before,
        ControlledPartitionMovementNativeProcessSnapshot after)
    {
        await EqualAsync(before.Identity, after.Identity);
        await Assert.That(after.State.Version).IsEqualTo(before.State.Version);
        await Assert.That(after.State.Incarnation).IsEqualTo(before.State.Incarnation);
        await EqualAsync(before.State.Snapshot, after.State.Snapshot);
        await Assert.That(after.State.Term >= before.State.Term).IsTrue();
        await Assert.That(after.State.LastIndex).IsEqualTo(after.State.CommittedIndex);
        await Assert.That(before.State.LastIndex).IsEqualTo(before.State.CommittedIndex);
        await Assert.That(after.State.CommittedIndex >= before.State.CommittedIndex).IsTrue();
        await Assert.That(after.Entries.Length).IsEqualTo(checked((int)after.State.CommittedIndex));
        await EqualAsync(before.Entries, after.Entries.Take(before.Entries.Length).ToImmutableArray());
        var next = checked(before.State.CommittedIndex + FirstNewEntry);
        foreach (var entry in after.Entries.Skip(before.Entries.Length))
        {
            await Assert.That(entry.Index).IsEqualTo(next++);
            await StartupReplayAsync(before, after, checked((int)(entry.Index - FirstNewEntry)));
            await Assert.That(entry.Term).IsGreaterThan(InitialTerm);
        }
        var added = after.State.CommittedIndex - before.State.CommittedIndex;
        await Assert.That(after.Position).IsEqualTo(checked(before.Position + added));
        var key = Convert.ToHexString(KeySpace.Applied.ToArray()) + Separator;
        await Assert.That(before.Rows.Count(row => row.StartsWith(key, StringComparison.Ordinal))).IsEqualTo(AppliedRowCount);
        await Assert.That(after.Rows.Count(row => row.StartsWith(key, StringComparison.Ordinal))).IsEqualTo(AppliedRowCount);
        await Assert.That(before.Rows.Where(row => !row.StartsWith(key, StringComparison.Ordinal))
            .SequenceEqual(after.Rows.Where(row => !row.StartsWith(key, StringComparison.Ordinal)))).IsTrue();
        var expected = key + Convert.ToHexString(NativeSerialization.Serialize(after.State.CommittedIndex));
        await Assert.That(after.Rows.Single(row => row.StartsWith(key, StringComparison.Ordinal))).IsEqualTo(expected);
    }

    internal static async Task EqualAsync<T>(T expected, T actual)
        => await Assert.That(JsonDefaults.Serialize(expected).AsSpan().SequenceEqual(JsonDefaults.Serialize(actual))).IsTrue();

    private static async Task StartupReplayAsync(ControlledPartitionMovementNativeProcessSnapshot before,
        ControlledPartitionMovementNativeProcessSnapshot after, int entryOffset)
    {
        if (after.Entries[entryOffset].Operation is not { } replay)
        {
            return;
        }
        await Assert.That(replay.Kind is OperationKind.BootstrapPhysicalShardCatalog or OperationKind.RegisterPhysicalOwner).IsTrue();
        var original = before.Entries.Select(entry => entry.Operation)
            .FirstOrDefault(operation => operation is not null && operation.Id == replay.Id)
            ?? throw new InvalidOperationException(UnexpectedStartupReplay);
        await Assert.That(original.Kind).IsEqualTo(replay.Kind);
        await Assert.That(replay.EvaluatedAt >= original.EvaluatedAt).IsTrue();
        await Assert.That(replay.NativePayload.Span.SequenceEqual(original.NativePayload.Span)).IsTrue();
        await EqualAsync(original, replay with
        {
            EvaluatedAt = original.EvaluatedAt
        });
    }
}
