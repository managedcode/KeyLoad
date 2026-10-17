namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed record NativeDropPageRecoveryImage(string[] Rows, long Position, long LastIndex,
    long CommittedIndex, long Term);
