namespace KeyLoad.Core.Features.GraphTraversal.Models;

internal sealed record GraphCrossPartitionRecordWrite(byte[] Key, byte[]? Value);
