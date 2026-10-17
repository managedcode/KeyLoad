namespace KeyLoad;

/// <summary>The canonical deadline whose exact retained state is advanced.</summary>
[Orleans.GenerateSerializer, Orleans.Alias(QueueDeadlineContract.KindAlias)]
public enum QueueDeadlineKind
{
    /// <summary>Make a scheduled message eligible at its retained NotBefore.</summary>
    PromoteScheduled = 0,
    /// <summary>Expire a retained lease and apply the existing retry/dead-letter policy.</summary>
    ExpireLease = 1,
    /// <summary>Expire a pending message at its retained absolute lifetime.</summary>
    ExpireMessage = 2
}
