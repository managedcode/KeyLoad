using System.Text.Json;
using KeyLoad.AppHost.Features.CodeQuality;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.CodeQuality;

internal static class NativeCoverageProductEvidenceRejection
{
    private const string RequiredPhase = "true";
    private const string ReceiptName = "native-product-admission-phase.v1.json";
    private const string TrxOutcomeFailure = "An original native test outcome report does not match the exact contributor inventory.";

    internal static async Task RunIfRequiredAsync(NativeCoverageMergeProcess.ToolingOptions options,
        string resultsDirectory, CancellationToken cancellationToken)
    {
        var phase = Environment.GetEnvironmentVariable(NativeCoverageMergeProcess.ProductAdmissionRequiredEnvironment);
        var descriptorPath = Environment.GetEnvironmentVariable(NativeCoverageMergeProcess.ProductAdmissionDescriptorEnvironment);
        if (phase is null && descriptorPath is null)
        { return; }
        if (!string.Equals(phase, RequiredPhase, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(descriptorPath) || !Path.IsPathFullyQualified(descriptorPath))
        { throw new InvalidDataException("The required native product-admission inputs are invalid."); }
        var descriptor = Path.GetFullPath(descriptorPath);
        if (!string.Equals(Path.GetFileName(descriptor), NativeCoverageMergeProcess.ProductDescriptorName,
                StringComparison.Ordinal))
        { throw new InvalidDataException("The required native product descriptor is unavailable."); }
        var evidenceRoot = Path.GetDirectoryName(descriptor)
            ?? throw new InvalidDataException("The required native product descriptor is unavailable.");
        var descriptorInfo = new FileInfo(descriptor);
        var optionsInfo = new FileInfo(Path.Combine(evidenceRoot, NativeCoverageProductEvidenceOptions.FileName));
        if (!descriptorInfo.Exists || descriptorInfo.LinkTarget is not null
            || (descriptorInfo.Attributes & FileAttributes.ReparsePoint) != 0
            || !optionsInfo.Exists || optionsInfo.LinkTarget is not null
            || (optionsInfo.Attributes & FileAttributes.ReparsePoint) != 0)
        { throw new InvalidDataException("The required native product descriptor or bounds input is unavailable."); }
        await RunAsync(options, evidenceRoot, descriptor, resultsDirectory, cancellationToken).ConfigureAwait(false);
    }

    private static async Task RunAsync(NativeCoverageMergeProcess.ToolingOptions options,
        string evidenceRoot, string descriptor, string resultsDirectory, CancellationToken cancellationToken)
    {
        var originals = NativeCoverageProductEvidenceFiles.CaptureOriginalFiles(evidenceRoot, options.Coverage,
            out var originalEntryCount);
        var originalDescriptor = originals[Path.GetFullPath(descriptor)];
        if (originalEntryCount > options.Coverage.MaximumFiles
            - NativeCoverageProductEvidenceFiles.RejectedOwnedEntryCount)
        { throw new InvalidDataException(NativeCoverageMergeProcess.OutputFailure); }
        var temporaryRoot = NativeCoverageProductEvidenceFiles.SelectOwnedDirectory(
            Path.Combine(evidenceRoot, "unit-functional-01"));
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            NativeCoverageProductEvidenceFiles.CreateOwnedDirectory(temporaryRoot);
            await AssertAdmissionSequenceAsync(options, evidenceRoot, descriptor, temporaryRoot,
                originals, cancellationToken).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.Observe(() => NativeCoverageProductEvidenceFiles.DeleteOwnedDirectory(temporaryRoot), failures);
        if (failures.Count == 0)
        {
            ServerFailureObserver.Observe(() => WritePhaseReceipt(resultsDirectory, originalDescriptor, options.Coverage), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task AssertAdmissionSequenceAsync(NativeCoverageMergeProcess.ToolingOptions options,
        string evidenceRoot, string descriptor, string temporaryRoot,
        IReadOnlyDictionary<string, NativeCoverageProductEvidenceFiles.OriginalFile> originals,
        CancellationToken cancellationToken)
    {
        var initial = await NativeCoverageMergeProcess.RunProductAdmissionAsync(options, evidenceRoot,
            descriptor, cancellationToken).ConfigureAwait(false);
        await AssertAdmittedAsync(initial).ConfigureAwait(false);
        NativeCoverageProductEvidenceFiles.AssertOriginalFiles(originals, options.Coverage);
        var rejectedDescriptor = NativeCoverageProductEvidenceFiles.WriteRejectedCopies(evidenceRoot,
            descriptor, temporaryRoot, originals, options.Coverage);
        var rejected = await NativeCoverageMergeProcess.RunProductAdmissionAsync(options, evidenceRoot,
            rejectedDescriptor, cancellationToken).ConfigureAwait(false);
        await AssertExpectedOutcomeRejectionAsync(rejected).ConfigureAwait(false);
        NativeCoverageProductEvidenceFiles.AssertOriginalFiles(originals, options.Coverage);
        var recovered = await NativeCoverageMergeProcess.RunProductAdmissionAsync(options, evidenceRoot,
            descriptor, cancellationToken).ConfigureAwait(false);
        await AssertAdmittedAsync(recovered).ConfigureAwait(false);
        NativeCoverageProductEvidenceFiles.AssertOriginalFiles(originals, options.Coverage);
    }

    private static void WritePhaseReceipt(string resultsDirectory,
        NativeCoverageProductEvidenceFiles.OriginalFile descriptor,
        NativeCoverageExecutionOptions options)
    {
        var receiptPath = Path.Combine(resultsDirectory, ReceiptName);
        if (descriptor.Length <= 0 || descriptor.Length > options.MaximumDescriptorBytes)
        { throw new InvalidDataException(NativeCoverageMergeProcess.OutputFailure); }
        var receipt = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 1,
            phase = "product-admission",
            descriptorSha256 = descriptor.Sha256,
            healthyAdmissions = 2,
            rejectedAdmissions = 1,
            rejectedOutcome = TrxOutcomeFailure
        });
        if (receipt.LongLength > options.MaximumDescriptorBytes)
        { throw new InvalidDataException(NativeCoverageMergeProcess.OutputFailure); }
        using var stream = new FileStream(receiptPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.Write(receipt, 0, receipt.Length);
        stream.Flush(flushToDisk: true);
    }

    private static async Task AssertAdmittedAsync(NativeCoverageMergeProcess.ChildResult result)
    {
        await NativeCoverageMergeProcess.AssertSuccessfulChildAsync(result).ConfigureAwait(false);
        using var output = JsonDocument.Parse(result.StandardOutput);
        await Assert.That(output.RootElement.GetProperty(NativeCoverageProductFields.Admitted).GetBoolean()).IsTrue();
    }

    private static async Task AssertExpectedOutcomeRejectionAsync(NativeCoverageMergeProcess.ChildResult result)
    {
        await Assert.That(result.ExitCode).IsEqualTo(1);
        await Assert.That(result.ExitJoined).IsTrue();
        await Assert.That(result.OutputJoined).IsTrue();
        await Assert.That(result.ErrorJoined).IsTrue();
        await Assert.That(result.Disposed).IsTrue();
        var firstLine = result.StandardError.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        await Assert.That(firstLine).IsEqualTo(TrxOutcomeFailure);
    }
}
