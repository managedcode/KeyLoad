namespace KeyLoad.ProcessRecoveryTests;

internal static class ChunkMultiblockCrashProtocol
{
    internal const string PrepareSeal = "sample-chunk-multiblock-prepare-seal";
    internal const string PrepareMerge = "sample-chunk-multiblock-prepare-merge";
    internal const string FaultSeal = "sample-chunk-multiblock-fault-seal";
    internal const string FaultMerge = "sample-chunk-multiblock-fault-merge";
    internal const string RecoverSeal = "sample-chunk-multiblock-recover-seal";
    internal const string RecoverMerge = "sample-chunk-multiblock-recover-merge";
    internal const string FirstColdSeal = "sample-chunk-multiblock-first-cold-seal";
    internal const string FirstColdMerge = "sample-chunk-multiblock-first-cold-merge";
    internal const string SecondColdSeal = "sample-chunk-multiblock-second-cold-seal";
    internal const string SecondColdMerge = "sample-chunk-multiblock-second-cold-merge";
    internal const string FirstColdFile = "sample-chunk-multiblock-first-cold.json";
    internal const string SecondColdFile = "sample-chunk-multiblock-second-cold.json";
    internal const int Arguments = 4;
    internal const int DirectoryArgument = 0;
    internal const int StageArgument = 1;
    internal const int MutationArgument = 2;
    internal const int ModeArgument = 3;
    internal const int SampleLimit = 258;
    internal const int PublishedBlocks = 2;
    internal const long PreparedSealRevision = 256;
    internal const long PreparedMergeRevision = 259;
    internal const long RecoveredSealRevision = 259;
    internal const long RecoveredMergeRevision = 260;
    internal const long HealthySealRevision = 261;
    internal const long HealthyMergeRevision = 262;
    internal const long OpenGeneration = 0;
    internal const long RecoveredSealGeneration = 1;
    internal const long RecoveredMergeGeneration = 2;
    internal const long HealthySealGeneration = 2;
    internal const long HealthyMergeGeneration = 3;
    internal const long PreparedSequence = 257;
    internal const long PreparedAutomaticSealSequence = 255;
    internal const long HealthySequence = 258;
    internal const long HealthyCommits = 2;

    internal static bool IsPrepare(string mode) => mode is PrepareSeal or PrepareMerge;
    internal static bool IsFault(string mode) => mode is FaultSeal or FaultMerge;
    internal static bool IsMerge(string mode) => mode is PrepareMerge or FaultMerge or RecoverMerge or FirstColdMerge or SecondColdMerge;
    internal static bool IsVerify(string mode) => mode is FirstColdSeal or FirstColdMerge or SecondColdSeal or SecondColdMerge;
    internal static bool IsFirstCold(string mode) => mode is FirstColdSeal or FirstColdMerge;
    internal static bool IsMode(string mode) => IsPrepare(mode) || IsFault(mode) || IsVerify(mode) || mode is RecoverSeal or RecoverMerge;
    internal static string[] Modes(bool merge) => [merge ? PrepareMerge : PrepareSeal, merge ? FaultMerge : FaultSeal,
        merge ? RecoverMerge : RecoverSeal, merge ? FirstColdMerge : FirstColdSeal, merge ? SecondColdMerge : SecondColdSeal];
}
