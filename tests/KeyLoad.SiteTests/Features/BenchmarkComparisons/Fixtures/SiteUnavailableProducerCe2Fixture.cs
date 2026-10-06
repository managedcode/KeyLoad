using System.Security.Cryptography;
using System.Text.Json;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal static class SiteUnavailableProducerCe2Fixture
{
    internal const string RelativePath = "tests/KeyLoad.SiteTests/Features/BenchmarkComparisons/Fixtures/SiteUnavailableProducerCe2.json";
    internal const string ExpectedSha256 = "10cae70f6a210d78df6992e079eec7157ec0563b96597dcc84cbc581472b8ac9";
    internal const string WorkflowSourceSha256 = "d3362e7ec1afe1f990c31600af6d4f5904b2d96564d5ea2da5d275b6270c448a";
    internal const string RunSourceSha256 = "2d01ea8e70d82718fde0c83532d231d2833a4387bdefc121de083c645a8e766f";
    internal const string JobsSourceSha256 = "59521b3508ff752511cba0ecf61476504c35d4a76e703e9121d0195fee9cd7eb";
    internal const string SourceRevision = "ce2eace916b3660a4c7fe2976a012637600c3b28";
    internal const string RunShaField = "run";
    internal const string WorkflowShaField = "workflow";
    internal const string JobsShaField = "jobs";
    internal const string Sha256Field = "sha256";
    internal const string ProvenanceField = "provenance";
    internal const string SourceRevisionField = "sourceRevision";
    internal const string ArtifactField = "artifact";
    internal const string AggregateJobField = "aggregateJob";
    internal const string CandidateAcceptedField = "candidateAccepted";
    internal const string UnavailableField = "unavailable";
    internal const string InputsPreservedField = "inputsPreserved";
    internal const string CandidatePreservedField = "candidatePreserved";
    internal const string FollowupUnavailableField = "followupUnavailable";
    internal const string ChangedStepMutation = "changed-step";
    internal const string ChangedSourceMutation = "changed-source";
    internal const string ChangedRepositoryMutation = "changed-repository";
    internal const long RunId = 37328642737;
    internal const long AggregateJobId = 111915038450;
    internal const long WorkflowId = 373808964;
    internal const long Attempt = 1;
    internal const int FixtureBytes = 19574;

    public static async Task<byte[]> ReadAsync(SiteContentInputs inputs, CancellationToken token)
    {
        var bytes = await File.ReadAllBytesAsync(Path.Combine(inputs.Repository, RelativePath), token);
        await Assert.That(bytes.Length).IsEqualTo(FixtureBytes);
        await Assert.That(Convert.ToHexStringLower(SHA256.HashData(bytes))).IsEqualTo(ExpectedSha256);
        using var fixture = JsonDocument.Parse(bytes);
        var root = fixture.RootElement;
        var provenance = root.GetProperty(ProvenanceField);
        await Assert.That(provenance.GetProperty(ArtifactField).GetString()).IsEqualTo("site-evidence/isolated-capture/metadata");
        await Assert.That(provenance.GetProperty(RunIdField).GetInt64()).IsEqualTo(RunId);
        await Assert.That(provenance.GetProperty(AttemptField).GetInt64()).IsEqualTo(Attempt);
        await Assert.That(provenance.GetProperty(SourceRevisionField).GetString()).IsEqualTo(SourceRevision);
        await Assert.That(provenance.GetProperty(AggregateJobIdField).GetInt64()).IsEqualTo(AggregateJobId);
        await Assert.That(provenance.GetProperty(WorkflowShaField).GetProperty(Sha256Field).GetString())
            .IsEqualTo(WorkflowSourceSha256);
        await Assert.That(provenance.GetProperty(RunShaField).GetProperty(Sha256Field).GetString())
            .IsEqualTo(RunSourceSha256);
        await Assert.That(provenance.GetProperty(JobsShaField).GetProperty(Sha256Field).GetString())
            .IsEqualTo(JobsSourceSha256);
        await Assert.That(root.GetProperty(WorkflowShaField).GetProperty(SiteIsolatedGitHubTokens.Id).GetInt64())
            .IsEqualTo(WorkflowId);
        await Assert.That(root.GetProperty(RunShaField).GetProperty(SiteIsolatedGitHubTokens.Id).GetInt64())
            .IsEqualTo(RunId);
        await Assert.That(root.GetProperty(AggregateJobField).GetProperty(SiteIsolatedGitHubTokens.Id).GetInt64())
            .IsEqualTo(AggregateJobId);
        return bytes;
    }

    public static async Task VerifyUnchangedAsync(SiteContentInputs inputs, byte[] original, CancellationToken token)
    {
        var after = await File.ReadAllBytesAsync(Path.Combine(inputs.Repository, RelativePath), token);
        await Assert.That(after).IsEquivalentTo(original);
        await Assert.That(Convert.ToHexStringLower(SHA256.HashData(after))).IsEqualTo(ExpectedSha256);
    }

    private const string RunIdField = "runId";
    private const string AttemptField = "attempt";
    private const string AggregateJobIdField = "aggregateJobId";
}
