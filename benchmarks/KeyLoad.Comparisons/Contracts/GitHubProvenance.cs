namespace KeyLoad.Comparisons;

/// <summary>Identifies the GitHub Actions workflow run and selected comparison profile.</summary>
/// <param name="RunId">The GitHub Actions run identifier.</param>
/// <param name="Attempt">The run attempt number.</param>
/// <param name="Repository">The repository that owns the workflow run.</param>
/// <param name="Ref">The Git reference used by the run.</param>
/// <param name="Workflow">The workflow name.</param>
/// <param name="Profile">The selected comparison profile.</param>
public sealed record GitHubProvenance(long RunId, int Attempt, string Repository, string Ref, string Workflow, string Profile);
