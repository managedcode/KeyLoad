using System.Globalization;
using KeyLoad;
using KeyLoad.RecoveryHost;
using KeyLoad.RecoveryHost.Features.AnalyticsStore;
using KeyLoad.RecoveryHost.Features.BlobStorage;
using KeyLoad.RecoveryHost.Features.Messaging;
using KeyLoad.RecoveryHost.Features.Search;
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
if (await BlobCommitCutScenario.TryRunAsync(args)
    || await UserAnalyticsProcessScenario.TryRunAsync(args)
    || await NativeChecksumProfileScenario.TryRunAsync(args)
    || await NativeTextOnlineCrashScenario.TryRunAsync(args)
    || await NativeTextIncrementalCrashScenario.TryRunAsync(args)
    || await NativeTextCrashScenario.TryRunAsync(args)
    || await ExistingStoreInspector.TryRunAsync(args)
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
