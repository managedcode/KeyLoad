using System.Text.Json;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.CodeQuality;

internal static class CoverageProductEvidenceRejection
{
    private const string RequiredPhase = "true";
    private const string ReceiptName = "execution-product-admission-phase.json";
    private const string TrxOutcomeFailure = "An original execution test outcome report does not match the exact contributor inventory.";

    internal static async Task RunIfRequiredAsync(CoverageMergeProcess.ToolingOptions options,
        string resultsDirectory, CancellationToken cancellationToken)
    {
        var phase = Environment.GetEnvironmentVariable(CoverageMergeProcess.ProductAdmissionRequiredEnvironment);
        var descriptorPath = Environment.GetEnvironmentVariable(CoverageMergeProcess.ProductAdmissionDescriptorEnvironment);
        if (phase is null && descriptorPath is null)
        {
            return;
        }
        if (!string.Equals(phase, RequiredPhase, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(descriptorPath) || !Path.IsPathFullyQualified(descriptorPath))
        {
            throw new InvalidDataException("The required execution product-admission inputs are invalid.");
        }
        var descriptor = Path.GetFullPath(descriptorPath);
        if (!string.Equals(Path.GetFileName(descriptor), CoverageMergeProcess.ProductDescriptorName,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException("The required execution product descriptor is unavailable.");
        }
        var evidenceRoot = Path.GetDirectoryName(descriptor)
            ?? throw new InvalidDataException("The required execution product descriptor is unavailable.");
        var descriptorInfo = new FileInfo(descriptor);
        var optionsInfo = new FileInfo(Path.Combine(evidenceRoot, CoverageProductEvidenceOptions.FileName));
        if (!descriptorInfo.Exists || descriptorInfo.LinkTarget is not null
            || (descriptorInfo.Attributes & FileAttributes.ReparsePoint) != 0
            || !optionsInfo.Exists || optionsInfo.LinkTarget is not null
            || (optionsInfo.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("The required execution product descriptor or bounds input is unavailable.");
        }
        await RunAsync(options, evidenceRoot, descriptor, resultsDirectory, cancellationToken).ConfigureAwait(false);
    }

    private static async Task RunAsync(CoverageMergeProcess.ToolingOptions options,
        string evidenceRoot, string descriptor, string resultsDirectory, CancellationToken cancellationToken)
    {
        var originals = CoverageProductEvidenceFiles.CaptureOriginalFiles(evidenceRoot, options.Coverage,
            out var originalEntryCount);
        var originalDescriptor = originals[Path.GetFullPath(descriptor)];
        if (originalEntryCount > options.Coverage.MaximumFiles
            - CoverageProductEvidenceFiles.RejectedOwnedEntryCount)
        {
            throw new InvalidDataException(CoverageMergeProcess.OutputFailure);
        }
        var temporaryRoot = CoverageProductEvidenceFiles.SelectOwnedDirectory(
            Path.Combine(evidenceRoot, "unit-functional-01"));
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            CoverageProductEvidenceFiles.CreateOwnedDirectory(temporaryRoot);
            await AssertAdmissionSequenceAsync(options, evidenceRoot, descriptor, temporaryRoot,
                originals, cancellationToken).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.Observe(() => CoverageProductEvidenceFiles.DeleteOwnedDirectory(temporaryRoot), failures);
        if (failures.Count == 0)
        {
            ServerFailureObserver.Observe(() => WritePhaseReceipt(resultsDirectory, originalDescriptor, options.Coverage), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task AssertAdmissionSequenceAsync(CoverageMergeProcess.ToolingOptions options,
        string evidenceRoot, string descriptor, string temporaryRoot,
        IReadOnlyDictionary<string, CoverageProductEvidenceFiles.OriginalFile> originals,
        CancellationToken cancellationToken)
    {
        var initial = await CoverageMergeProcess.RunProductAdmissionAsync(options, evidenceRoot,
            descriptor, cancellationToken).ConfigureAwait(false);
        await AssertAdmittedAsync(initial).ConfigureAwait(false);
        CoverageProductEvidenceFiles.AssertOriginalFiles(originals, options.Coverage);
        var rejectedDescriptor = CoverageProductEvidenceFiles.WriteRejectedCopies(evidenceRoot,
            descriptor, temporaryRoot, originals, options.Coverage);
        var rejected = await CoverageMergeProcess.RunProductAdmissionAsync(options, evidenceRoot,
            rejectedDescriptor, cancellationToken).ConfigureAwait(false);
        await AssertExpectedOutcomeRejectionAsync(rejected).ConfigureAwait(false);
        CoverageProductEvidenceFiles.AssertOriginalFiles(originals, options.Coverage);
        var recovered = await CoverageMergeProcess.RunProductAdmissionAsync(options, evidenceRoot,
            descriptor, cancellationToken).ConfigureAwait(false);
        await AssertAdmittedAsync(recovered).ConfigureAwait(false);
        CoverageProductEvidenceFiles.AssertOriginalFiles(originals, options.Coverage);
    }

    private static void WritePhaseReceipt(string resultsDirectory,
        CoverageProductEvidenceFiles.OriginalFile descriptor,
        CoverageExecutionOptions options)
    {
        var receiptPath = Path.Combine(resultsDirectory, ReceiptName);
        if (descriptor.Length <= 0 || descriptor.Length > options.MaximumDescriptorBytes)
        {
            throw new InvalidDataException(CoverageMergeProcess.OutputFailure);
        }
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
        {
            throw new InvalidDataException(CoverageMergeProcess.OutputFailure);
        }
        using var stream = new FileStream(receiptPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.Write(receipt, 0, receipt.Length);
        stream.Flush(flushToDisk: true);
    }

    private static async Task AssertAdmittedAsync(CoverageMergeProcess.ChildResult result)
    {
        await CoverageMergeProcess.AssertSuccessfulChildAsync(result).ConfigureAwait(false);
        using var output = JsonDocument.Parse(result.StandardOutput);
        await Assert.That(output.RootElement.GetProperty(CoverageProductFields.Admitted).GetBoolean()).IsTrue();
    }

    private static async Task AssertExpectedOutcomeRejectionAsync(CoverageMergeProcess.ChildResult result)
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
