using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.AppHost.Features.ClusterReplication;
using KeyLoad.AppHost.Features.TestInfrastructure;
using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.StorageRecovery;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KeyLoad.IntegrationTests.Features.BackupRestore;

/// <summary>The original native two-RF3 graph gates all servers on one owning offline CLI publication.</summary>
internal sealed class ClusterRestoreRf3Fixture : IAsyncDisposable
{
    private const string ProfileRootName = "target-profile";
    private const string DataRootName = "target-data";
    private const int SecretBytes = 32;
    private const int FirstMapping = 0;
    private readonly string profileRoot;
    internal Guid OperationId { get; } = Guid.NewGuid();
    private readonly NodeEpochRf3Profile profile;
    private readonly string peer;
    private readonly string credential;
    private readonly ImmutableArray<ClusterBackupOwnerReceipt> receipts;
    private readonly ImmutableArray<string> archives;
    private DistributedApplication? application;
    private bool started;
    private bool joined;
    internal bool ServersAdmitted { get; private set; }
    private bool operatorOnly;
    private bool retainPublishedTarget;
    private string? changedSigner;
    private ImmutableArray<ClusterRestoreOwnerMapping>? changedMappings;
    internal ClusterRestoreRf3StageObservation? HeldObservation { get; private set; }
    private ClusterRestoreRf3OperatorObservation? operatorObservation;

    internal ClusterRestoreRf3Fixture(string ownedRoot, string credential,
        ImmutableArray<ClusterBackupOwnerReceipt> receipts, ImmutableArray<string> archives)
    {
        this.credential = credential;
        this.receipts = receipts;
        this.archives = archives;
        profileRoot = Path.Combine(ownedRoot, ProfileRootName);
        DataRoot = Path.Combine(ownedRoot, DataRootName);
        Mappings = [.. receipts.Select(receipt => new ClusterRestoreOwnerMapping(
            ClusterRestoreRf3Protocol.CurrentVersion, receipt.Cut.Owner,
            new(Guid.NewGuid(), Guid.NewGuid(), receipt.Cut.Owner.VoterIds,
                ClusterRestoreRf3Protocol.InitialOwnerEpoch),
            [.. receipt.Cut.Owner.VoterIds.Select(voter => voter + "/")]))];
        var first = Mappings.First().Target;
        profile = new(new LocalProfile(ClusterProfileStore.CurrentVersion, first.PhysicalShardId, first.Incarnation,
            Secret(), Secret(), credential));
        peer = Secret();
    }

    private const string IncorrectDigest = "0000000000000000000000000000000000000000000000000000000000000000";
    private readonly List<int> originalOperatorExits = [];
    private readonly List<ClusterRestoreRf3OperatorObservation> originalOperatorObservations = [];

    internal IReadOnlyList<int> OriginalOperatorExits => originalOperatorExits;
    internal int? OriginalOperatorExitCode { get; private set; }
    internal void RecordOperatorExit(int value) { OriginalOperatorExitCode = value; originalOperatorExits.Add(value); }
    internal void AdmitServers(bool value) => ServersAdmitted = value;
    internal string DataRoot { get; }
    internal string ExpectedSignerFingerprint => ClusterRestoreRf3SignerIdentity.Fingerprint(profile.SigningKey);
    internal ImmutableArray<ClusterRestoreOwnerMapping> Mappings { get; }
    internal DistributedApplication Application => application
        ?? throw new InvalidOperationException(ClusterRestoreRf3Protocol.Invalid);

    internal Task StartAsync(bool restore, CancellationToken cancellationToken)
        => StartCoreAsync(restore, null, null, wrongDigest: false, null, null, null, cancellationToken);

    internal Task StartOperatorOnlyAsync(CancellationToken cancellationToken)
        => StartOperatorAsync(null, cancellationToken);

    internal Task StartHeldOperatorAsync(NativeClusterRestoreStage selected, CancellationToken cancellationToken)
        => StartOperatorAsync(selected, cancellationToken);

    private async Task StartOperatorAsync(NativeClusterRestoreStage? cut, CancellationToken cancellationToken)
    {
        operatorOnly = true;
        try
        { await StartCoreAsync(restore: true, null, null, wrongDigest: false, null, null, cut, cancellationToken).ConfigureAwait(false); }
        finally { operatorOnly = false; }
    }

    internal Task StartRejectedTerminalAsync(CancellationToken cancellationToken)
        => StartRetainedRejectionAsync(new(ErrorCode.RecoveryRequired, true, null,
            ClusterRestoreRf3ResumeProtocol.TerminalCutLost, null, null), cancellationToken);

    internal Task StartRejectedResumedCredentialAsync(string actualCredential, bool published, CancellationToken cancellationToken)
        => StartRetainedRejectionAsync(new(ErrorCode.Unauthenticated, published, actualCredential, null, null, null), cancellationToken);

    internal Task StartRejectedPlanAsync(bool signer, bool published, CancellationToken cancellationToken)
        => StartRetainedRejectionAsync(new(ErrorCode.Conflict, published, null,
            ClusterRestoreRf3ResumeProtocol.PlanMismatch, signer ? Secret() : null, signer ? null
                : [.. Mappings.Select((mapping, ordinal) => ordinal == FirstMapping
                    ? mapping with { Target = mapping.Target with { PhysicalShardId = Guid.NewGuid() } } : mapping)]), cancellationToken);

    internal Task StartRejectedCorruptPlanAsync(bool published, CancellationToken cancellationToken)
        => StartRetainedRejectionAsync(new(ErrorCode.FormatUnsupported, published, null,
            ClusterRestoreRf3ResumeProtocol.StateCorruption, null, null), cancellationToken);

    internal Task StartRejectedMissingProgressAsync(bool published, CancellationToken cancellationToken)
        => StartRetainedRejectionAsync(new(ErrorCode.RecoveryRequired, published, null,
            ClusterRestoreRf3ResumeProtocol.MissingProgress, null, null), cancellationToken);

    internal Task StartRejectedMissingCompletedSlotAsync(CancellationToken cancellationToken)
        => StartRetainedRejectionAsync(new(ErrorCode.RecoveryRequired, true, null,
            ClusterRestoreRf3ResumeProtocol.MissingSlot, null, null), cancellationToken);

    private async Task StartRetainedRejectionAsync(ClusterRestoreRf3Rejection invocation, CancellationToken cancellationToken)
    {
        retainPublishedTarget = invocation.Published;
        changedSigner = invocation.Signer;
        changedMappings = invocation.Mappings;
        try
        {
            await StartCoreAsync(restore: true, invocation.Expected, invocation.Credential, wrongDigest: false, null,
                invocation.Detail, null, cancellationToken).ConfigureAwait(false);
        }
        finally { changedSigner = null; changedMappings = null; retainPublishedTarget = false; }
    }

    internal Task StartRejectedAsync(ErrorCode expected, CancellationToken cancellationToken)
        => StartCoreAsync(restore: true, expected, null, wrongDigest: false, null, null, null, cancellationToken);

    internal Task StartRejectedCredentialAsync(string actualCredential, CancellationToken cancellationToken)
        => StartCoreAsync(restore: true, ErrorCode.Unauthenticated, actualCredential, wrongDigest: false, null, null, null, cancellationToken);

    internal Task StartRejectedManifestAsync(CancellationToken cancellationToken)
        => StartCoreAsync(restore: true, ErrorCode.Corruption, null, wrongDigest: true, null, null, null, cancellationToken);

    internal Task StartRejectedMissingAsync(ImmutableArray<string> derivatives, CancellationToken cancellationToken)
        => StartCoreAsync(restore: true, ErrorCode.Corruption, null, wrongDigest: false, derivatives, ClusterRestoreRf3ArchiveOmissionTrial.MissingDetail, null,
            cancellationToken);

    internal Task StartDerivativeArchiveAsync(ImmutableArray<string> derivatives, bool rejectModified,
        CancellationToken cancellationToken)
        => StartCoreAsync(restore: true, rejectModified ? ErrorCode.Corruption : null, null, wrongDigest: false,
            derivatives, rejectModified ? ClusterRestoreRf3ArchiveMutationTrial.ModifiedDetail : null, null, cancellationToken);

    private async Task StartCoreAsync(bool restore, ErrorCode? expectedFailure, string? operatorCredential, bool wrongDigest,
        ImmutableArray<string>? sourceArchives, string? exactFailureDetail, NativeClusterRestoreStage? cut, CancellationToken cancellationToken)
    {
        if (application is not null || (!restore && (!joined || !ServersAdmitted)))
        { throw new InvalidOperationException(ClusterRestoreRf3Protocol.Invalid); }
        if (restore)
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(profile.Value,
                ClusterProfileStore.CreateJson(IntegrationProfileOptions.Execution()));
            _ = await profile.CopyExactAsync(profileRoot, bytes, cancellationToken).ConfigureAwait(false);
        }
        var image = await ClusterFixtureImageIdentity.ReadVerifiedReferenceAsync(cancellationToken).ConfigureAwait(false);
        var builder = await ClusterRestoreRf3Graph.CreateAsync(profileRoot, Mappings.Last().Target, peer,
            cancellationToken).ConfigureAwait(false);
        var repository = ClusterFixtureDiagnostics.FindRepositoryRoot().FullName;
        var expectedReceipts = wrongDigest ? receipts.Select(receipt => receipt with { ManifestDigest = IncorrectDigest }).ToImmutableArray() : receipts;
        IResourceBuilder<ExecutableResource>? cli = null;
        application = await ClusterRestoreRf3BuilderCleanup.BuildAsync(builder, () =>
        {
            cli = restore ? ClusterRestoreRf3Operator.Add(builder, repository, DataRoot, expectedReceipts, sourceArchives ?? archives,
                changedMappings ?? Mappings, operatorCredential ?? credential, changedSigner ?? profile.SigningKey, OperationId, cut) : null;
            ClusterRestoreRf3Operator.BindTargetData(builder, profileRoot, DataRoot, cli);
            if (operatorOnly)
            {
                foreach (var node in builder.Resources.OfType<ContainerResource>().Where(value =>
                    ClusterRestoreRf3Protocol.Nodes.Contains(value.Name, StringComparer.Ordinal)))
                { builder.CreateResourceBuilder(node).WithExplicitStart(); }
            }
            return builder.BuildAsync(cancellationToken);
        }).ConfigureAwait(false);
        await TwoRf3MembershipImageAssertions.VerifyAsync(application, image, cancellationToken).ConfigureAwait(false);
        if (cli is not null)
        {
            operatorObservation = new(application.Services.GetRequiredService<ResourceLoggerService>(),
                application.Services.GetRequiredService<IOptions<TestExecutionOptions>>().Value.CleanupOutputCharacters,
                cancellationToken);
            originalOperatorObservations.Add(operatorObservation);
            if (cut is { } selected)
            {
                HeldObservation = new(application.Services.GetRequiredService<ResourceLoggerService>(), OperationId,
                    selected, application.Services.GetRequiredService<IOptions<TestExecutionOptions>>().Value.CleanupOutputCharacters,
                    cancellationToken);
            }
        }
        started = true;
        joined = false;
        if (!restore)
        { ServersAdmitted = true; }
        await application.StartAsync(cancellationToken).ConfigureAwait(false);
        await ClusterRestoreRf3StartupCompletion.WaitAsync(this, cli, cut, operatorOnly,
            expectedFailure, exactFailureDetail, cancellationToken).ConfigureAwait(false);
    }

    internal Task RequireRejectedServersAsync(ErrorCode expected, string? exactDetail, CancellationToken cancellationToken)
        => ClusterRestoreRf3Rejections.RequireAsync(this, expected, exactDetail, retainPublishedTarget,
            operatorObservation ?? throw new InvalidOperationException(ClusterRestoreRf3Protocol.Invalid), cancellationToken);

    internal async Task StopAsync()
    {
        if (application is not { } owned)
        { return; }
        await ClusterRestoreRf3Shutdown.JoinAsync(this, owned, operatorObservation, HeldObservation,
            started, ServersAdmitted).ConfigureAwait(false);
        application = null;
        joined = started && ServersAdmitted;
    }

    internal Task KillHeldAndJoinAsync(NativeClusterRestoreStage stage, CancellationToken cancellationToken)
        => ClusterRestoreRf3HeldShutdown.JoinAsync(this, stage, cancellationToken);

    internal Task RequireOperatorReceiptAsync(ImmutableArray<ClusterRestoreRf3OperatorNode> nodes)
        => (operatorObservation ?? throw new InvalidOperationException(ClusterRestoreRf3Protocol.Invalid))
            .RequireAsync(receipts.First().Cut.CaptureId, nodes);

    internal Task<ClusterRestoreRf3OperatorReceipt> ReadOriginalReceiptAsync()
        => (operatorObservation ?? throw new InvalidOperationException(ClusterRestoreRf3Protocol.Invalid)).ReadOriginalReceiptAsync();

    internal Task RequireReceiptReplayAsync(ClusterRestoreRf3OperatorReceipt original)
        => (operatorObservation ?? throw new InvalidOperationException(ClusterRestoreRf3Protocol.Invalid)).RequireReplayAsync(original);

    public ValueTask DisposeAsync() => new(StopAsync());
    private static string Secret()
    {
        var bytes = RandomNumberGenerator.GetBytes(SecretBytes);
        try
        { return Convert.ToBase64String(bytes); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
}
