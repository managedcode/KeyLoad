namespace KeyLoad.RecoveryHost.Features.BlobStorage;

internal static class BlobCommitCutPayload
{
    private const byte OriginalFirstByte = 1;
    private const byte OriginalSecondByte = 2;
    private const byte OriginalThirdByte = 3;
    private const byte OriginalFourthByte = 4;
    private const int FirstByteOffset = 0;
    private const int PayloadCycleLength = 251;

    internal static byte[] OriginalBytes() => [OriginalFirstByte, OriginalSecondByte, OriginalThirdByte, OriginalFourthByte];

    internal static byte[] MultipartBytes()
    {
        var bytes = new byte[BlobCommitCutProtocol.MultipartLength];
        for (var i = FirstByteOffset; i < bytes.Length; i++)
        {
            bytes[i] = (byte)(i % PayloadCycleLength);
        }
        return bytes;
    }
}
