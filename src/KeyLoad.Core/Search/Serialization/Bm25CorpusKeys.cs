namespace KeyLoad.Core.Features.Search;

internal static class Bm25CorpusKeys
{
    internal static byte[] Prefix(PartitionRef partition)
        => KeySpace.Partition(Bm25CorpusProtocol.CurrentFamily, partition);

    internal static byte[] Current(Bm25CorpusCurrent current)
        => Current(current.Partition, current.Collection, current.Field, current.TokenizerVersion, current.PrincipalId);

    internal static byte[] Current(PartitionRef partition, string collection, string field, string tokenizer, string principal)
        => KeySpace.Partition(Bm25CorpusProtocol.CurrentFamily, partition, collection, field, tokenizer, principal);
}
