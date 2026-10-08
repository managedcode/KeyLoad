using System.Security.Cryptography;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Runs the complete original native terminal operation under one finite process and continuation deadline.</summary>
internal static class ControlledPartitionMovementTerminalProcessTrial
{
    private const int PeerKeyBytes = 32;

    internal static Task ExecuteAsync(PartitionMovePeerStage stage, CommitStage cut)
        => ControlledPartitionMovementTerminalOwners.ExecuteAsync(async (source, target, listeners, corpus) =>
        {
            var options = UnitExecutionOptions.NativeMovementProcess().Value;
            using var deadline = new CancellationTokenSource(options.OperationTimeout, source.Database.EvaluationClock);
            using var original = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token,
                TestContext.Current!.Execution.CancellationToken);
            var token = original.Token;
            var initialPosition = source.Store.Position;
            var seeded = await ControlledPartitionMovementPrepareSeed.ExecuteAsync(source, target, corpus, token);
            var controlSecret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(PeerKeyBytes));
            var targetSecret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(PeerKeyBytes));
            var sourceRuntime = ControlledPartitionMovementLoopbackOptions.Bind(source, corpus,
                controlSecret, targetSecret, control: true);
            var targetRuntime = ControlledPartitionMovementLoopbackOptions.Bind(target, corpus,
                controlSecret, targetSecret, control: false);
            using var process = new ControlledPartitionMovementProcessScope(source, target, listeners, stage, cut);
            if (stage == PartitionMovePeerStage.Abort)
            {
                await ControlledPartitionMovementAbortOperation.ExecuteAsync(source, target, corpus,
                    sourceRuntime, targetRuntime, null!, null!,
                    ControlledPartitionMovementPrepareRequest.CallerAddress(listeners), seeded.Receipt,
                    seeded.Authority, seeded.Blob, seeded.RecordedAt, initialPosition, token);
                await Assert.That(process.Exercised).IsTrue();
                return;
            }
            await ControlledPartitionMovementTerminalOperation.ExecuteAsync(source, target, corpus,
                sourceRuntime, targetRuntime, null!, null!,
                ControlledPartitionMovementPrepareRequest.CallerAddress(listeners), seeded.Receipt,
                seeded.Authority, seeded.RecordedAt, initialPosition, token);
            await Assert.That(process.Exercised).IsTrue();
            await ControlledDocumentNativeCommandOperation.ExecuteAsync(source, target, listeners, corpus,
                sourceRuntime, targetRuntime, null!, null!, token);
        });
}
