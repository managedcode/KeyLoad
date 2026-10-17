using KeyLoad;
using System.Globalization;
using KeyLoad.CrashHost;
using KeyLoad.CrashHost.Features.AnalyticsStore;
using KeyLoad.CrashHost.Features.BlobStorage;
using KeyLoad.CrashHost.Features.ClusterReplication;
using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.CrashHost.Features.Messaging;
using KeyLoad.CrashHost.Features.ResourceExecution;
using KeyLoad.CrashHost.Features.Search;
using KeyLoad.Storage.ZoneTree;

const int DirectoryArgument = 0;
const int StageArgument = 1;
const int MutationArgument = 2;
const int ModeArgument = 3;

if (await ChunkMultiblockCrashScenario.TryRunAsync(args))
{
    return;
}
if (args.Length == SampleChunkEarlyCrashProtocol.ArgumentCount && SampleChunkEarlyCrashProtocol.IsMode(args[ModeArgument]))
{
    await SampleChunkEarlyCrashScenario.RunAsync(args);
    return;
}
if (await SampleChunkCrashScenario.TryRunAsync(args))
{
    return;
}

_ = SerializationExecutionRegistration.Process.Value;
if (await D2ImportCrashScenario.TryRunAsync(args)
    || await D2InventoryCrashScenario.TryRunAsync(args)
    || await BlobCommitCutScenario.TryRunAsync(args)
    || await UserAnalyticsProcessScenario.TryRunAsync(args)
    || await NativeChecksumProfileScenario.TryRunAsync(args)
    || await ClusterRestoreProcessCutScenario.TryRunAsync(args)
    || await NativeTextOnlineCrashScenario.TryRunAsync(args)
    || await NativeTextIncrementalCrashScenario.TryRunAsync(args)
    || await NativeTextCrashScenario.TryRunAsync(args)
    || await ControlledPartitionMovementProcessScenario.TryRunAsync(args)
    || await NativeInstallFrameInspection.TryRunAsync(args)
    || await C1OutcomeInspection.TryRunAsync(args)
    || await C1SubmissionOutcomeInspection.TryRunAsync(args)
    || await C1SubmissionJournalInspection.TryRunAsync(args)
    || await ExistingStoreInspector.TryRunAsync(args)
    || await SnapshotManifestEntryProbe.TryRunAsync(args)
    || await ReplicaPrefixGcCrashScenario.TryRunAsync(args)
    || await ReplicaCrashScenario.TryRunAsync(args)
    || await ExternalQueueWorkerCrashScenario.TryRunAsync(args))
{
    return;
}

var directory = args[DirectoryArgument];
var stage = Enum.Parse<CommitStage>(args[StageArgument]);
var mutationIndex = int.Parse(args[MutationArgument], CultureInfo.InvariantCulture);
var mode = args.Length > ModeArgument ? args[ModeArgument] : CrashFixtureValues.CommitMode;
var boundary = new CanonicalCrashBoundary(stage, mutationIndex, mode == CrashFixtureValues.CommitMode);
using var store = new ZoneTreeStore(new(directory)
{
    FaultObserver = boundary.Observe
}, CrashExecutionOptions.StorageExecution(), CrashExecutionOptions.PointCacheExecution());
await CrashScenarioDispatcher.RunAsync(directory, store, boundary, mode);
