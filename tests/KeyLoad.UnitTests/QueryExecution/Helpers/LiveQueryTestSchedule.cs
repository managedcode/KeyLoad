namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class LiveQueryTestSchedule
{
    internal const int IdentityCount = 12;
    internal const int IdentityStride = 5;
    internal const int IdentitySeed = 41179;
    internal const int MutationCount = 100;
    internal const int FullRounds = 8;
    internal const int FinalUpdateCount = 4;
    internal const int InsertRound = 0;
    internal const int UpdateRound = 1;
    internal const int LeaveRound = 2;
    internal const int NonmatchingUpdateRound = 3;
    internal const int EnterRound = 4;
    internal const int DeleteRound = 5;
    internal const int NonmatchingInsertRound = 6;
    internal const int ReenterRound = 7;
    internal const int FinalUpdateRound = 8;
    internal static readonly int[] TransitionRounds =
    [
        InsertRound,
        UpdateRound,
        LeaveRound,
        NonmatchingUpdateRound,
        EnterRound,
        DeleteRound,
        NonmatchingInsertRound,
        ReenterRound,
        FinalUpdateRound
    ];

    internal static int IndexForTrial(int trial) => (trial * IdentityStride + IdentitySeed) % IdentityCount;
}
