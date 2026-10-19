namespace KeyLoad.Storage.ZoneTree.IO;

internal readonly record struct OfflineFileIdentity(ulong Device, ulong Inode, long Length);
