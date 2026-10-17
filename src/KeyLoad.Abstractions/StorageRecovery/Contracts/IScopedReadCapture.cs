namespace KeyLoad.Storage;

/// <summary>Owns bounded snapshot copies and explicit original-cut lookup/range closure; no live store view is exposed.</summary>
internal interface IScopedReadCapture
{
    void AdmitRetainedBytes(long bytes);
    void CapturePrefix(byte[] prefix);
    void CaptureExact(byte[] key);
    bool ReadCapturedValue(byte[] key, StorageValueReader reader);
    void VisitCapturedPrefix(byte[] prefix, StorageRecordVisitor visitor);
}
