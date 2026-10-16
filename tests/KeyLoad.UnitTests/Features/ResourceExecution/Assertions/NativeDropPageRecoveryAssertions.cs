using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class NativeDropPageRecoveryAssertions
{
    internal static async Task<KeyLoadException> RefusedAsync(Func<Task> action, ErrorCode code)
    {
        var error = await Assert.ThrowsExactlyAsync<KeyLoadException>(action);
        await Assert.That(error!.Code).IsEqualTo(code);
        return error;
    }

    internal static async Task RetainedAsync(NativeDropPageAttemptState state, ResourceRetirementAttemptV1 attempt,
        ReplicatedOperation original, ResourceRetirementPreparedOperationStage stage)
    {
        var source = state.Profile.Source;
        var current = source.Database.ReadResourceRetirementAttempt(state.Context.PrincipalId, state.Scope,
            Guid.Empty, NativeDropPreparedPageProducer.Work(state.Profile, state.Context.Token));
        await NativeDropRuntimeAssertions.SameAsync(attempt, current).ConfigureAwait(false);
        var prepared = source.Database.ReadPreparedResourceRetirementOperation(state.Context.PrincipalId,
            state.Scope, attempt, NativeDropPreparedPageProducer.Work(state.Profile, state.Context.Token));
        await Assert.That(prepared.Stage).IsEqualTo(stage);
        if (stage == ResourceRetirementPreparedOperationStage.Persisted)
        {
            await Assert.That(prepared.Original!.NativePayload.Span.SequenceEqual(original.NativePayload.Span)).IsTrue();
            var absent = source.Store.Read(view => view.ReadOwnedValue(KeySpace.GlobalOutcome(original.PrincipalId, original.Id)) is null);
            await Assert.That(absent).IsTrue();
        }
        else
        {
            await Assert.That(prepared.Original).IsNull();
            await Assert.That(prepared.PersistReceipt).IsNull();
            await Assert.That(prepared.Receipt.CommandId).IsEqualTo(ResourceRetirementIdentity.PreparedOperationCommandId(
                state.Scope, attempt.AttemptId, ResourceRetirementPreparedOperationAction.Close));
        }
        var checkpoint = source.Database.ReadResourceRetirementCleanupCheckpoint(PartitionStoreProtocol.AdministratorId,
            state.OriginalState.Control, state.Scope.Page!.Partition,
            NativeDropPreparedPageProducer.Work(state.Profile, state.Context.Token));
        await Assert.That(checkpoint).IsNull();
    }
}
