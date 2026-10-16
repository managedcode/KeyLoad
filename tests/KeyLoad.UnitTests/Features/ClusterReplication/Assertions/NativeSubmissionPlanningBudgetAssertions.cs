using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Execution;
using KeyLoad.Core.Features.ClusterRouting.Identity;

namespace KeyLoad.UnitTests.Features.ClusterReplication;

internal static class NativeSubmissionPlanningBudgetAssertions
{
    internal static ReadExecutionBudget Work(TestDatabase fixture, CancellationToken token)
        => new(fixture.Database.OperationLimitsOptions, fixture.Database.EvaluationClock, token);

    internal static ReadExecutionBudget QueueLimit(TestDatabase fixture, ReplicatedOperation operation,
        bool records, CancellationToken token)
    {
        var prefix = Work(fixture, token);
        prefix.ConstrainNativeWork(prefix.MaximumNativeReadBytes, prefix.MaximumNativeScanRecords);
        fixture.Store.Read(raw => CommandOutcomeKeyResolver.Select(prefix.CreateView(raw), operation.PrincipalId,
            operation.Id, CommandOutcomePartitionIdentity.Resolve(operation)));
        var work = Work(fixture, token);
        work.ConstrainNativeWork(records ? work.MaximumNativeReadBytes : Math.Max(1, prefix.ReadBytes),
            records ? Math.Max(1, prefix.ConstrainedNativeRecords) : work.MaximumNativeScanRecords);
        return work;
    }

    internal static ReadExecutionBudget DocumentLimit(TestDatabase fixture, bool records, CancellationToken token)
    {
        var work = Work(fixture, token);
        work.ConstrainNativeWork(records ? work.MaximumNativeReadBytes : 1,
            records ? 1 : work.MaximumNativeScanRecords);
        return work;
    }

    internal static async Task UnchangedAsync(ReplicaSubmissionOutcomeFlow owner, Func<Task> body)
    {
        var state = ReplicaSubmissionOutcomeState.Read(owner.Database);
        var identity = NativeSerialization.Serialize(owner.Database.Store.Identity);
        var outbox = owner.Database.Database.GetOutboxStatus("root", owner.Database.Partition);
        await body();
        await state.RequireAsync(ReplicaSubmissionOutcomeState.Read(owner.Database));
        await ReplicaSubmissionOutcomeFlow.RequireBytesAsync(identity, NativeSerialization.Serialize(owner.Database.Store.Identity));
        await EqualAsync(outbox, owner.Database.Database.GetOutboxStatus("root", owner.Database.Partition));
    }

    internal static async Task EqualAsync<T>(T expected, T actual)
        => await Assert.That(JsonElement.DeepEquals(JsonSerializer.SerializeToElement(expected, JsonDefaults.Options),
            JsonSerializer.SerializeToElement(actual, JsonDefaults.Options))).IsTrue();

    internal static ReplicatedOperation Issue<T>(ReplicaSubmissionOutcomeFlow owner, OperationKind kind, T payload, Guid id)
        => owner.Database.Database.CreateNativeOperation(kind, id, "root",
            owner.Database.Database.EvaluationClock.GetUtcNow(), NativeSerialization.Serialize(payload));

    internal static CommitToken NextToken(ReplicaSubmissionOutcomeFlow owner)
    {
        var fixture = owner.Database;
        var placement = fixture.Database.ReadAtomicPartitionPlacement("root", new(1, fixture.Partition));
        return new(placement.Incarnation, fixture.Partition.AtomicPartitionId,
            checked(fixture.ReadJournalCut().LastIndex + 1), placement.PlacementEpoch);
    }

    internal static CommitReceipt Receipt(ReplicaSubmissionOutcomeFlow owner, Guid id, MutationReceipt effect)
        => new(id, NextToken(owner), [effect], DurabilityProfile.ProcessDurable);

    internal static async Task ReceiptAsync(CommitReceipt expected, CommitReceipt actual)
    {
        await EqualAsync(expected, actual);
        foreach (var effect in actual.Mutations)
        {
            await Assert.That(effect.CompositionReferences.IsEmpty).IsTrue();
        }
    }
}
