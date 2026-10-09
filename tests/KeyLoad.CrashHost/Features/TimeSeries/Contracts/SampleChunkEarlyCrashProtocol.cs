namespace KeyLoad.CrashHost;

internal enum SampleChunkEarlyCut { Open = 0, Append = 1, Correction = 2 }

internal static class SampleChunkEarlyCrashProtocol
{
    internal const string PrepareOpen = "sample-chunk-early-prepare-open";
    internal const string FaultOpen = "sample-chunk-early-fault-open";
    internal const string RecoverOpen = "sample-chunk-early-recover-open";
    internal const string VerifyOpen = "sample-chunk-early-verify-open";
    internal const string PrepareAppend = "sample-chunk-early-prepare-append";
    internal const string FaultAppend = "sample-chunk-early-fault-append";
    internal const string RecoverAppend = "sample-chunk-early-recover-append";
    internal const string VerifyAppend = "sample-chunk-early-verify-append";
    internal const string PrepareCorrection = "sample-chunk-early-prepare-correction";
    internal const string FaultCorrection = "sample-chunk-early-fault-correction";
    internal const string RecoverCorrection = "sample-chunk-early-recover-correction";
    internal const string VerifyCorrection = "sample-chunk-early-verify-correction";
    internal const long OpenRevision = 1;
    internal const long OpenGeneration = 0;
    internal const long EmptySequence = 0;
    internal const long FirstSequence = 1;
    internal const long InitialSequence = 2;
    internal const long CorrectedSequence = 3;
    internal const int ArgumentCount = 4;
    internal const int RootArgument = 0;
    internal const int StageArgument = 1;
    internal const int MutationArgument = 2;
    internal const int ModeArgument = 3;
    internal const int PrepareIndex = 0;
    internal const int FaultIndex = 1;
    internal const int VerifyIndex = 3;

    internal static bool IsPrepare(string mode) => mode is PrepareOpen or PrepareAppend or PrepareCorrection;
    internal static bool IsFault(string mode) => mode is FaultOpen or FaultAppend or FaultCorrection;
    internal static bool IsVerify(string mode) => mode is VerifyOpen or VerifyAppend or VerifyCorrection;
    internal static bool IsMode(string mode) => IsPrepare(mode) || IsFault(mode) || IsVerify(mode)
        || mode is RecoverOpen or RecoverAppend or RecoverCorrection;
    internal static SampleChunkEarlyCut Cut(string mode) => mode switch
    {
        PrepareOpen or FaultOpen or RecoverOpen or VerifyOpen => SampleChunkEarlyCut.Open,
        PrepareAppend or FaultAppend or RecoverAppend or VerifyAppend => SampleChunkEarlyCut.Append,
        PrepareCorrection or FaultCorrection or RecoverCorrection or VerifyCorrection => SampleChunkEarlyCut.Correction,
        _ => throw new InvalidOperationException(SampleChunkCrashContract.Invalid)
    };
    internal static string[] Modes(SampleChunkEarlyCut cut) => cut switch
    {
        SampleChunkEarlyCut.Open => [PrepareOpen, FaultOpen, RecoverOpen, VerifyOpen],
        SampleChunkEarlyCut.Append => [PrepareAppend, FaultAppend, RecoverAppend, VerifyAppend],
        SampleChunkEarlyCut.Correction => [PrepareCorrection, FaultCorrection, RecoverCorrection, VerifyCorrection],
        _ => throw new InvalidOperationException(SampleChunkCrashContract.Invalid)
    };
}
