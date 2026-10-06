namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal static class CurrentGitHubJobSelectionTestProtocol
{
    internal const string Image = "image";
    internal const string ImageJobName = "Build Docker images";
    internal const string Preflight = "preflight";
    internal const string Control = "control";
    internal const string Scaled = "scaled";
    internal const string Vector = "vector";
    internal const string OpenLoop = "openloop";
    internal const string Proof = "proof";
    internal const string UnknownJob = "unknown-job";
    internal const string MissingCellId = "missing-cell-id";
    internal const string MissingEvidenceProfile = "missing-evidence-profile";
    internal const string MissingTarget = "missing-target";
    internal const string MissingMatrixKind = "missing-matrix-kind";
    internal const string MismatchedJobName = "mismatched-job-name";
    internal const string MismatchedCellId = "mismatched-cell-id";
    internal const string MismatchedEvidenceProfile = "mismatched-evidence-profile";
    internal const string MismatchedRowSelector = "mismatched-row-selector";
    internal const string MismatchedTarget = "mismatched-target";
    internal const string MismatchedMatrixKind = "mismatched-matrix-kind";
    internal const string UnknownCellId = "unknown-cell-id";
    internal const string ImageWithCellId = "image-with-cell-id";
    internal const string ImageWithEvidenceProfile = "image-with-evidence-profile";
    internal const string ImageWithScaleProfile = "image-with-scale-profile";
    internal const string Accepted = "accepted";
    internal const string ContextUnchanged = "contextUnchanged";
    internal const string ReusesInputContext = "reusesInputContext";
    internal const string SelectedProfile = "selectedProfile";
    internal const string SelectedJobName = "selectedJobName";
    internal const string ExpectedProfile = "expectedProfile";
    internal const string NameField = "name";
    internal const string Failure = "The current-job selection Node regression failed.";
    internal const string RootEnvironment = "KEYLOAD_CURRENT_JOB_SELECTION_ROOT";
}
