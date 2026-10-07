using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.StorageRecovery.Assertions;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class DocumentIndexCommittedScanScenario : IAsyncDisposable
{
    private static readonly TimeSpan OperationDeadline = TimeSpan.FromSeconds(10);
    private readonly TestDatabase database = new();
    private readonly ManualResetEventSlim scanEntered = new();
    private readonly ManualResetEventSlim releaseScan = new();
    private readonly ManualResetEventSlim writerEntered = new();
    private readonly CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(
        TestContext.Current!.Execution.CancellationToken);
    private readonly List<Exception> failures = [];
    private Task<DocumentIndexCommittedScanCut>? scanTask;
    private Task<CommitReceipt>? writerTask;
    private ZoneTreeStore? reopened;
    private long oldPosition;
    private bool originalStoreOpen = true;
    private bool reopenedStoreOpen;
    private bool disposed;

    internal static async Task RunAsync()
    {
        var scenario = new DocumentIndexCommittedScanScenario();
        await scenario.ExecuteAsync();
    }

    private async Task ExecuteAsync()
    {
        try
        {
            await ExistingStoreInspectorFailureJoin.ObserveAsync(RunOperationsAsync(), failures);
        }
        finally
        {
            await ExistingStoreInspectorFailureJoin.ObserveAsync(DisposeAsync().AsTask(), failures);
        }
        ExistingStoreInspectorFailureJoin.Throw(failures);
    }

    private async Task RunOperationsAsync()
    {
        PrepareSeedAndScan();
        await StartWriterAtHeldCutAsync();
        await JoinOriginalTasksAsync(scanTask, writerTask, failures);
        if (failures.Count == 0)
        {
            await VerifyOldAndCommittedCutsAsync();
            await ReopenAndVerifyHealthyCommandAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        ExistingStoreInspectorFailureJoin.Capture(releaseScan.Set, failures);
        var settlement = JoinOriginalTasksAsync(scanTask, writerTask, failures);
        await ExistingStoreInspectorFailureJoin.ObserveAsync(DisposeReopenedStoreAsync(settlement), failures);
        await ExistingStoreInspectorFailureJoin.ObserveAsync(DisposeOriginalStoreAsync(settlement), failures);
        await ExistingStoreInspectorFailureJoin.ObserveAsync(DisposeCancellationAsync(settlement), failures);
        await ExistingStoreInspectorFailureJoin.ObserveAsync(DisposeScanEnteredAsync(settlement), failures);
        await ExistingStoreInspectorFailureJoin.ObserveAsync(DisposeReleaseScanAsync(settlement), failures);
        await ExistingStoreInspectorFailureJoin.ObserveAsync(DisposeWriterEnteredAsync(settlement), failures);
        await ExistingStoreInspectorFailureJoin.ObserveAsync(DisposeFixtureAsync(settlement), failures);
        ExistingStoreInspectorFailureJoin.Throw(failures);
    }

    private void PrepareSeedAndScan()
    {
        database.Configure(DocumentIndexCommittedScanOracle.Collection, ResourceKind.Collection,
            indexes: [new(DocumentIndexCommittedScanOracle.IndexName, [DocumentIndexCommittedScanOracle.LabelPath])]);
        database.Commit(
            new PutDocument(DocumentIndexCommittedScanOracle.Collection, DocumentIndexCommittedScanOracle.ReplaceId,
                DocumentIndexCommittedScanOracle.OriginalJson, DocumentIndexCommittedScanOperations.InitialDocumentRevision),
            new PutDocument(DocumentIndexCommittedScanOracle.Collection, DocumentIndexCommittedScanOracle.DeleteId,
                DocumentIndexCommittedScanOracle.DeletedJson, DocumentIndexCommittedScanOperations.InitialDocumentRevision),
            new PutDocument(DocumentIndexCommittedScanOracle.Collection, DocumentIndexCommittedScanOracle.StableId,
                DocumentIndexCommittedScanOracle.StableJson, DocumentIndexCommittedScanOperations.InitialDocumentRevision));
        oldPosition = database.Store.Position;
        scanTask = Task.Run(() => database.Store.Read(view => DocumentIndexCommittedScanOracle.ReadAndHold(
            view, database.Store, database.Partition, scanEntered, releaseScan, cancellation.Token)));
    }

    private async Task StartWriterAtHeldCutAsync()
    {
        if (!scanEntered.Wait(OperationDeadline, cancellation.Token))
        {
            throw new TimeoutException("The native document/index scan did not reach its owned barrier.");
        }
        writerTask = Task.Run(() =>
        {
            writerEntered.Set();
            return database.Commit(
                new PutDocument(DocumentIndexCommittedScanOracle.Collection, DocumentIndexCommittedScanOracle.ReplaceId,
                    DocumentIndexCommittedScanOracle.ReplacementJson, DocumentIndexCommittedScanOracle.InitialRevision,
                    ExplicitReplacement: true),
                new DeleteDocument(DocumentIndexCommittedScanOracle.Collection, DocumentIndexCommittedScanOracle.DeleteId,
                    DocumentIndexCommittedScanOracle.InitialRevision),
                new PutDocument(DocumentIndexCommittedScanOracle.Collection, DocumentIndexCommittedScanOracle.InsertId,
                    DocumentIndexCommittedScanOracle.InsertedJson, DocumentIndexCommittedScanOperations.InitialDocumentRevision));
        });
        if (!writerEntered.Wait(OperationDeadline, cancellation.Token))
        {
            throw new TimeoutException("The production document writer did not reach its operation boundary.");
        }
        await Assert.That(writerTask.IsCompleted).IsFalse();
        await Assert.That(database.Store.Position).IsEqualTo(oldPosition);
        releaseScan.Set();
    }

    private async Task VerifyOldAndCommittedCutsAsync()
    {
        var oldCut = await scanTask!;
        await DocumentIndexCommittedScanOracle.AssertOldCutAsync(oldCut, oldPosition);
        var receipt = await writerTask!;
        await DocumentIndexCommittedScanReceiptAssertions.AssertWriterReceiptAsync(receipt, oldPosition + 1);
        await DocumentIndexCommittedScanOracle.AssertCommittedCutAsync(
            DocumentIndexCommittedScanOracle.ReadCut(database.Store, database.Partition), receipt.Token.Position);
    }

    private async Task ReopenAndVerifyHealthyCommandAsync()
    {
        var expectedIdentity = database.Store.Identity with { SigningKey = database.Store.Identity.SigningKey.ToArray() };
        var committedPosition = (await writerTask!).Token.Position;
        database.Store.Dispose();
        originalStoreOpen = false;
        reopened = new(new(database.Directory), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        reopenedStoreOpen = true;
        var engine = DocumentIndexCommittedScanOperations.CreateEngine(reopened);
        await NativeReadCutStoreStateAssertions.AssertIdentityAndPositionAsync(reopened, expectedIdentity, committedPosition);
        await DocumentIndexCommittedScanOracle.AssertCommittedCutAsync(
            DocumentIndexCommittedScanOracle.ReadCut(reopened, database.Partition), committedPosition);

        var healthy = DocumentIndexCommittedScanOperations.ApplyHealthyCommand(engine, database.Partition);
        await Assert.That(healthy.Token.Position).IsEqualTo(committedPosition + 1);
        await DocumentIndexCommittedScanOracle.AssertHealthyCutAsync(
            DocumentIndexCommittedScanOracle.ReadCut(reopened, database.Partition), healthy.Token.Position);
    }

    private async Task DisposeReopenedStoreAsync(Task settlement)
    {
        await settlement;
        var store = reopened;
        if (reopenedStoreOpen && store is not null)
        {
            store.Dispose();
            reopenedStoreOpen = false;
        }
    }

    private async Task DisposeOriginalStoreAsync(Task settlement)
    {
        await settlement;
        if (originalStoreOpen)
        {
            database.Store.Dispose();
            originalStoreOpen = false;
        }
    }

    private async Task DisposeFixtureAsync(Task settlement)
    {
        await settlement;
        if (!originalStoreOpen && !reopenedStoreOpen)
        {
            database.Dispose();
        }
    }

    private async Task DisposeCancellationAsync(Task settlement)
    {
        await settlement;
        cancellation.Dispose();
    }

    private async Task DisposeScanEnteredAsync(Task settlement)
    {
        await settlement;
        scanEntered.Dispose();
    }

    private async Task DisposeReleaseScanAsync(Task settlement)
    {
        await settlement;
        releaseScan.Dispose();
    }

    private async Task DisposeWriterEnteredAsync(Task settlement)
    {
        await settlement;
        writerEntered.Dispose();
    }

    private static async Task JoinOriginalTasksAsync(Task? scan, Task? writer, List<Exception> failures)
    {
        var tasks = new List<Task>(2);
        if (scan is not null)
        {
            tasks.Add(scan);
        }
        if (writer is not null)
        {
            tasks.Add(writer);
        }
        if (tasks.Count == 0)
        {
            return;
        }
        var joined = Task.WhenAll(tasks);
        await ExistingStoreInspectorFailureJoin.ObserveAsync(joined.WaitAsync(OperationDeadline, TimeProvider.System), failures);
        await ExistingStoreInspectorFailureJoin.ObserveAsync(joined, failures);
    }

}
