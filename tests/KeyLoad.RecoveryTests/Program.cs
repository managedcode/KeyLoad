using System.Globalization;
using KeyLoad;
using KeyLoad.RecoveryTests;
using KeyLoad.RecoveryTests.Features.AnalyticsStore;
using KeyLoad.RecoveryTests.Features.BlobStorage;
using KeyLoad.RecoveryTests.Features.Messaging;
using KeyLoad.RecoveryTests.Features.Search;
using KeyLoad.Storage.ZoneTree;

if (args.Length == 0 || args[0] != CrashFixtureValues.ProcessMode)
{
    return await MicrosoftTestingPlatformApplication.RunAsync(args);
}
args = args[1..];

const int DirectoryArgument = 0;
const int StageArgument = 1;
const int MutationArgument = 2;
const int ModeArgument = 3;

if (await ChunkMultiblockCrashScenario.TryRunAsync(args))
{
    return Environment.ExitCode;
}
if (args.Length == SampleChunkEarlyCrashProtocol.ArgumentCount && SampleChunkEarlyCrashProtocol.IsMode(args[ModeArgument]))
{
    await SampleChunkEarlyCrashScenario.RunAsync(args);
    return Environment.ExitCode;
}
if (await SampleChunkCrashScenario.TryRunAsync(args))
{
    return Environment.ExitCode;
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
    return Environment.ExitCode;
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
return Environment.ExitCode;
