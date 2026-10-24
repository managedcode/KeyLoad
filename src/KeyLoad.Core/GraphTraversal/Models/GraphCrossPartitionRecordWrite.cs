namespace KeyLoad.Core.GraphTraversal.Models;

internal sealed record GraphCrossPartitionRecordWrite(byte[] Key, byte[]? Value);
