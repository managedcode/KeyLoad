using System.Text.Json;
using KeyLoad.Core;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class ControlledDocumentPublishedReadFlow
{
    private const long InitialRevision = 1;
    private const string OriginalJson = "{\"text\":\"Київ knowledge\",\"state\":\"original\"}";
    private const string Collection = "movement-documents";
    private const string DocumentId = "knowledge-1";
    private const string PartitionKey = "partition-a";
    private static readonly PartitionRef Partition = new("system", "movement", "knowledge", PartitionKey);
    private const string MissingPrincipal = "missing-protected-document-principal";
    private const int ReadAttempts = 2;
    private const string ForeignTokenDetail = "The document session token belongs to another incarnation.";
    private const string MissingPrincipalDetail = "The credential is unavailable or expired.";

    internal static async Task ExecuteAsync(ControlledPartitionMovementNode source,
        ControlledPartitionMovementNode target)
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        await ControlledDocumentRetiredBridgeDenials.ExecuteAsync(source, target, token);
        var sourceBytes = ControlledPartitionMovementRawImage.Bytes(source.Store);
        var targetBytes = ControlledPartitionMovementRawImage.Bytes(target.Store);
        var sourcePosition = source.Store.Position;
        var targetPosition = target.Store.Position;
        var reference = new EntityRef(Partition,
            Collection, DocumentId);
        var sourceOptions = Options.Create(source.Database.Limits);
        var denialWork = new ReadExecutionBudget(sourceOptions, source.Database.EvaluationClock, token);
        var denied = await Assert.ThrowsAsync<KeyLoadException>(() =>
        {
            source.Database.TryCaptureControlledDocumentRead(MissingPrincipal, reference, null,
                source.Database.EvaluationClock.GetUtcNow() +
                    TimeSpan.FromSeconds(source.Database.Limits.QueryDeadlineSeconds), denialWork);
            return Task.CompletedTask;
        });
        await Assert.That(denied).IsNotNull();
        var originalDenial = denied
            ?? throw new InvalidOperationException("The actual missing source principal was not rejected.");
        await Assert.That(originalDenial.Code).IsEqualTo(ErrorCode.Unauthenticated);
        await Assert.That(originalDenial.Message).IsEqualTo(MissingPrincipalDetail);
        await UnchangedAsync(source, target, sourceBytes, targetBytes, sourcePosition, targetPosition);
        await ForeignTokenAsync(source, target, reference, token);
        await UnchangedAsync(source, target, sourceBytes, targetBytes, sourcePosition, targetPosition);
        for (var attempt = default(int); attempt < ReadAttempts; attempt++)
        {
            await HealthyAsync(source, target, reference, token);
            await UnchangedAsync(source, target, sourceBytes, targetBytes, sourcePosition, targetPosition);
            if (attempt == default)
            { source.Reopen(); target.Reopen(); }
        }
    }

    private static async Task ForeignTokenAsync(ControlledPartitionMovementNode source,
        ControlledPartitionMovementNode target, EntityRef reference, CancellationToken token)
    {
        var original = source.Database.ResolveOutcome(
            ControlledPartitionMovementCorpus.SeedOperation(source.Database));
        await Assert.That(original.Error).IsNull();
        var originalReceipt = original.Get<CommitReceipt>();
        var parent = new ReadExecutionBudget(Options.Create(source.Database.Limits),
            source.Database.EvaluationClock, token);
        var frame = source.Database.CaptureControlledDocumentRead(PhysicalShardCatalogFixture.RootPrincipalId,
            reference, originalReceipt.Token, source.Database.EvaluationClock.GetUtcNow() +
                TimeSpan.FromSeconds(source.Database.Limits.QueryDeadlineSeconds), parent);
        var bytes = Math.Min(target.Database.Limits.MaxBatchBytes, target.Database.Limits.MaxQueryReadBytes);
        var records = Math.Min(target.Database.Limits.MaxBatchMutations, target.Database.Limits.MaxScanRecords);
        var receiver = new ReadExecutionBudget(Options.Create(target.Database.Limits),
            target.Database.EvaluationClock, token);
        var grant = receiver.CreateReadGrant(bytes, records);
        var denied = await Assert.ThrowsAsync<KeyLoadException>(() =>
        {
            target.Database.ReadControlledDocument(PhysicalShardCatalogFixture.RootPrincipalId,
                frame, receiver, grant, token);
            return Task.CompletedTask;
        });
        var actual = denied ?? throw new InvalidOperationException("The original source token was accepted at B.");
        await Assert.That(actual.Code).IsEqualTo(ErrorCode.TokenInvalidated);
        await Assert.That(actual.Message).IsEqualTo(ForeignTokenDetail);
    }

    private static async Task HealthyAsync(ControlledPartitionMovementNode source,
        ControlledPartitionMovementNode target, EntityRef reference, CancellationToken token)
    {
        var parent = new ReadExecutionBudget(Options.Create(source.Database.Limits),
            source.Database.EvaluationClock, token);
        var frame = source.Database.CaptureControlledDocumentRead(PhysicalShardCatalogFixture.RootPrincipalId,
            reference, null, source.Database.EvaluationClock.GetUtcNow() +
                TimeSpan.FromSeconds(source.Database.Limits.QueryDeadlineSeconds), parent);
        var bytes = Math.Min(source.Database.Limits.MaxBatchBytes, source.Database.Limits.MaxQueryReadBytes);
        var records = Math.Min(source.Database.Limits.MaxBatchMutations, source.Database.Limits.MaxScanRecords);
        var parentGrant = parent.CreateReadGrant(bytes, records);
        var receiver = new ReadExecutionBudget(Options.Create(target.Database.Limits),
            target.Database.EvaluationClock, token);
        var receiverGrant = receiver.CreateReadGrant(bytes, records);
        var actual = target.Database.ReadControlledDocument(PhysicalShardCatalogFixture.RootPrincipalId,
            frame, receiver, receiverGrant, token);
        parent.ImportReadGrant(parentGrant, receiverGrant.ReadBytes, receiverGrant.ExaminedRecords);
        source.Database.ValidateControlledDocumentRead(frame, parent);
        var expected = new DocumentResult(reference, InitialRevision,
            OriginalJson, false, []);
        await Assert.That(JsonSerializer.SerializeToUtf8Bytes(actual, JsonDefaults.Options)
            .SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(expected, JsonDefaults.Options))).IsTrue();
        parent.Check();
    }

    private static async Task UnchangedAsync(ControlledPartitionMovementNode source,
        ControlledPartitionMovementNode target, string[] sourceBytes, string[] targetBytes,
        long sourcePosition, long targetPosition)
    {
        await Assert.That(ControlledPartitionMovementRawImage.Bytes(source.Store)
            .SequenceEqual(sourceBytes, StringComparer.Ordinal)).IsTrue();
        await Assert.That(ControlledPartitionMovementRawImage.Bytes(target.Store)
            .SequenceEqual(targetBytes, StringComparer.Ordinal)).IsTrue();
        await Assert.That(source.Store.Position).IsEqualTo(sourcePosition);
        await Assert.That(target.Store.Position).IsEqualTo(targetPosition);
    }
}
