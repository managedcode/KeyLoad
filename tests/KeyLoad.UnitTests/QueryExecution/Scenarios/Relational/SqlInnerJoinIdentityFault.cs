namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed record SqlInnerJoinIdentityFault(byte[] Key, byte[] Original, byte[] Damaged, bool LeftFault);
