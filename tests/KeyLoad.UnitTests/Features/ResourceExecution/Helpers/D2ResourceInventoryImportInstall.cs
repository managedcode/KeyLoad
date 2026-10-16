using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.UnitTests.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class D2ResourceInventoryImportInstall
{
    internal static async Task<D2ResourceInventoryImportResult> ExecuteAsync(D2ResourceInventoryImportOwners owners,
        D2ResourceInventoryImportImage image, CancellationToken token)
    {
        ReplicatedOperation? concurrent = null;
        OperationResult? concurrentResult = null;
        ReplicatedOperation? installed = null;
        OperationResult? installedResult = null;
        for (var ordinal = D2ResourceInventoryImportData.Empty; ordinal <= image.Handle.PageCount; ordinal++)
        {
            var phase = await PageAsync(owners, image, ordinal, token);
            var actual = phase.Get<PartitionMovePhaseResult>();
            await Assert.That(actual.Stage).IsEqualTo(PartitionMovePeerStage.Install);
            await Assert.That(actual.PublishedPlacement).IsNull();
            await D2ResourceInventoryAssertions.EqualAsync(owners.Corpus.Destination.Owner, actual.Journal.PhysicalOwner);
            if (ordinal < image.Handle.PageCount)
            {
                await Assert.That(actual.InstalledReceipt).IsNull();
            }
            else
            {
                installed = await TerminalAsync(owners, image, actual);
                installedResult = phase;
            }
            if (IsDocumentPage(image, ordinal))
            {
                var written = D2ResourceInventoryImportSeed.Concurrent(owners.Target, token);
                concurrent = written.Operation;
                concurrentResult = written.Outcome;
                await ConcurrentReceiptAsync(owners, image, ordinal, written.Operation, written.Outcome);
                await D2ResourceInventoryImportQuery.RefusedAsync(owners.Target, owners.Corpus, token);
            }
            await D2ResourceInventoryImportAcknowledgement.ExecuteAsync(owners, image, actual, ordinal, token);
        }
        var expectedSourceIndex = checked(D2ResourceInventoryImportData.CapturedSourceEntries
            + D2ResourceInventoryImportData.AuthorizationAndAcknowledgement * image.Handle.PageCount
            + D2ResourceInventoryImportData.AuthorizationAndAcknowledgement
                * (image.Handle.PageCount + D2ResourceInventoryImportData.Single));
        await Assert.That(owners.Source.Journal.Log.State.LastIndex).IsEqualTo(expectedSourceIndex);
        await Assert.That(owners.Source.Journal.Log.State.CommittedIndex).IsEqualTo(expectedSourceIndex);
        return new(image, concurrent ?? throw new InvalidOperationException("A genuine document page was not installed."),
            concurrentResult ?? throw new InvalidOperationException("The actual concurrent target receipt is missing."),
            installed ?? throw new InvalidOperationException("The terminal native Install operation is missing."),
            installedResult ?? throw new InvalidOperationException("The terminal native Install receipt is missing."));
    }

    private static async Task ConcurrentReceiptAsync(D2ResourceInventoryImportOwners owners,
        D2ResourceInventoryImportImage image, int ordinal, ReplicatedOperation original, OperationResult result)
    {
        var index = checked(D2ResourceInventoryImportData.TargetSetupEntries + image.Handle.PageCount
            + ordinal + D2ResourceInventoryImportData.InstalledPageAndWriteEntries);
        var expected = new CommitReceipt(original.Id,
            new(owners.Corpus.Destination.Owner.Incarnation, D2ResourceInventoryImportData.Partition.AtomicPartitionId,
                index, owners.Corpus.Destination.Owner.PlacementEpoch),
            [new(D2ResourceInventoryImportData.PutDocumentKind, D2ResourceInventoryImportData.Resource,
                D2ResourceInventoryImportData.DocumentId, D2ResourceInventoryImportData.Single)],
            DurabilityProfile.ProcessDurable);
        await D2ResourceInventoryAssertions.EqualAsync(expected, result.Get<CommitReceipt>());
    }

    private static bool IsDocumentPage(D2ResourceInventoryImportImage image, int ordinal)
    {
        var result = image.Pages.SingleOrDefault(page => page.Ordinal == ordinal);
        return result is not null
            && NativeSerialization.Deserialize<PartitionMoveImagePage>(result.NativePage.Span) is { } page
            && page.Family == PartitionRecordFamilies.Document && !page.Records.IsEmpty;
    }

    private static async Task<OperationResult> PageAsync(D2ResourceInventoryImportOwners owners,
        D2ResourceInventoryImportImage image, int ordinal, CancellationToken token)
    {
        var expiry = ControlledPartitionMovementFirstPhaseExpiry.Create(owners.Source, owners.SourceRuntime,
            image.Handle.ExpiresAt, token);
        var authorization = await ControlledPartitionMovementVerifiedSubmit.SubmitAsync(owners.Source,
            owners.SourceRuntime, owners.SourceAdmission, ControlledPartitionMovementTargetInstallRequest.Authorize(
                image.Captured, image.Fence, image.Handle, ordinal, owners.Corpus, owners.CallerAddress, expiry), token);
        await Assert.That(authorization.Error).IsNull();
        var issued = authorization.Get<PartitionMovePhaseResult>();
        await Assert.That(issued.Grant).IsNotNull();
        await Assert.That(issued.Grant!.ExpiresAt).IsEqualTo(expiry);
        var request = ControlledPartitionMovementTargetInstallRequest.Install(image.Captured, image.Fence,
            image.Handle, ordinal, issued, owners.Corpus, owners.CallerAddress, issued.Grant.ExpiresAt);
        var result = await ControlledPartitionMovementVerifiedSubmit.SubmitAsync(owners.Target,
            owners.TargetRuntime, owners.TargetAdmission, request, token);
        await Assert.That(result.Error).IsNull();
        await Assert.That(result.Get<PartitionMovePhaseResult>().Journal.CommandId).IsEqualTo(request.CommandId);
        return result;
    }

    private static async Task<ReplicatedOperation> TerminalAsync(D2ResourceInventoryImportOwners owners,
        D2ResourceInventoryImportImage image, PartitionMovePhaseResult actual)
    {
        var index = checked(D2ResourceInventoryImportData.TargetSetupEntries
            + D2ResourceInventoryImportData.StageAndInstallPasses * image.Handle.PageCount
            + D2ResourceInventoryImportData.FinalAndConcurrentEntries);
        var expected = new CommitReceipt(ControlledPartitionMovementTargetPhaseIds.InstallCommand(image.Handle.PageCount),
            new(owners.Corpus.Destination.Owner.Incarnation, ControlledPartitionMovementCorpus.Partition.AtomicPartitionId,
                index, owners.Corpus.Destination.Owner.PlacementEpoch), [], DurabilityProfile.ProcessDurable);
        await D2ResourceInventoryAssertions.EqualAsync(expected, actual.InstalledReceipt);
        await Assert.That(owners.Target.Journal.Log.State.LastIndex).IsEqualTo(index);
        await Assert.That(owners.Target.Journal.Log.State.CommittedIndex).IsEqualTo(index);
        var entry = owners.Target.Journal.Log.Read(index, D2ResourceInventoryImportData.Single,
            owners.TargetRuntime.ReplicaConfiguration.Value.MaxAppendBytes).Single();
        var original = entry.Operation ?? throw new InvalidOperationException("The actual native Install entry is absent.");
        await Assert.That(original.Id).IsEqualTo(expected.CommandId);
        return original;
    }
}
