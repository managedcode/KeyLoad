namespace KeyLoad.RecoveryTests.Features.BlobStorage;

internal readonly record struct BlobCommitCutSeedProof(Guid NodeId, Guid Incarnation, long ReadGeneration, long Position,
    byte[] PrincipalBytes, byte[] DeniedPrincipalBytes, byte[][] Operations, byte[][] Results, string RawImage);
