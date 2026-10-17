namespace KeyLoad.Storage.IO;

internal readonly record struct OfflineFileIdentity(ulong Device, ulong Inode, long Length);

internal readonly record struct OfflineFileMetadata(OfflineFileIdentity Identity, uint Type);
