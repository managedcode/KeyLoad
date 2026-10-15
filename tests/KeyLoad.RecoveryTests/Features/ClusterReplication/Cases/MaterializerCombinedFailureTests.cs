using System.Reflection;
using KeyLoad.Replication;
using ManagedCode.Communication.CQRS;

namespace KeyLoad.RecoveryTests;

internal sealed class MaterializerCombinedFailureTests
{
    private const string MissingExpectedFailure = "The actual native failure assertion returned no exception.";
    [Test]
    public async Task ActualCancellationCallbackAndJournalFilesystemWorkerFailuresBothSurviveJoinedShutdownAndOriginalRootRecovery()
    {
        using var deadline = new CancellationTokenSource(MaterializerCombinedFailureProtocol.Bound, TimeProvider.System);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, TestContext.Current!.Execution.CancellationToken);
        var token = linked.Token;
        List<Exception> failures = [];
        try
        {
            await using var fixture = new MaterializerCombinedFailureFixture();
            try
            {
                await RunAsync(fixture, token);
            }
            catch (Exception primary) when (CqrsRuntimeFailures.FindFatal(primary) is null)
            {
                ReplicaMaterializerLifecycleErrors.Add(primary, failures);
            }
            catch (Exception primary) when (CqrsRuntimeFailures.FindFatal(primary) is not null)
            {
                ReplicaMaterializerLifecycleErrors.Add(primary, failures);
            }
            if (failures.Count != MaterializerCombinedFailureProtocol.NoFailures)
            {
                fixture.RetainOnFailure();
            }
        }
        catch (Exception failure) when (CqrsRuntimeFailures.FindFatal(failure) is null)
        {
            ReplicaMaterializerLifecycleErrors.Add(failure, failures);
        }
        catch (Exception failure) when (CqrsRuntimeFailures.FindFatal(failure) is not null)
        {
            ReplicaMaterializerLifecycleErrors.Add(failure, failures);
        }
        ReplicaMaterializerLifecycleErrors.Throw(failures);
    }

    private static async Task RunAsync(MaterializerCombinedFailureFixture fixture, CancellationToken token)
    {
        var lifetime = typeof(ReplicaMaterializer).GetField(MaterializerCombinedFailureProtocol.LifetimeField,
            BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(fixture.Materializer) as CancellationTokenSource
            ?? throw new InvalidOperationException(MaterializerCombinedFailureProtocol.LifetimeShapeChanged);
        using var registration = lifetime.Token.Register(() =>
        {
            throw fixture.CancellationFailure;
        });
        var waiter = fixture.Materializer.WaitForApplyAsync(MaterializerCombinedFailureProtocol.FaultIndex, token);
        fixture.ArmCommittedApply();
        await fixture.Entered.WaitAsync(token);
        await Assert.That(File.Exists(fixture.MissingFile)).IsFalse();
        var original = fixture.BeginShutdown();
        try
        {
            await Assert.That(original.IsCompleted).IsFalse();
        }
        finally
        {
            fixture.Release();
        }
        var combined = await Assert.ThrowsExactlyAsync<AggregateException>(() => original.WaitAsync(token))
            ?? throw new InvalidOperationException(MissingExpectedFailure);
        var failures = combined.Flatten().InnerExceptions;
        await Assert.That(failures.Count).IsEqualTo(MaterializerCombinedFailureProtocol.OriginalFailures);
        await Assert.That(failures.Any(error => ReferenceEquals(error, fixture.CancellationFailure))).IsTrue();
        var worker = failures.OfType<KeyLoadException>().Single();
        await Assert.That(worker.Code).IsEqualTo(ErrorCode.UnknownWriteOutcome);
        await Assert.That(worker.Message).IsEqualTo(MaterializerCombinedFailureProtocol.WorkerDetail);
        await Assert.That(fixture.PhysicalFailure).IsNotNull();
        var terminal = await Assert.ThrowsExactlyAsync<KeyLoadException>(() => waiter.WaitAsync(token))
            ?? throw new InvalidOperationException(MissingExpectedFailure);
        await Assert.That(terminal.Code).IsEqualTo(ErrorCode.RecoveryRequired);
        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() => fixture.Materializer.CreateCheckpointAsync(token));
        Assert.ThrowsExactly<ObjectDisposedException>(() => _ = lifetime.Token);
        fixture.MarkOriginalJoined(original);
        await MaterializerCombinedFailureRecovery.RunAsync(fixture, token);
    }
}
