namespace KeyLoad.Storage;

/// <summary>Minimum binary reader support required by a persisted store identity.</summary>
public static class StoreReaderContract
{
    /// <summary>The existing epoch7 record reader.</summary>
    public const int Legacy = 0;
    /// <summary>The reader which supports the private native runtime journal family.</summary>
    public const int RuntimeJournal = 1;
    /// <summary>The reserved private canonical namespace requiring the journal reader.</summary>
    public const string RuntimeJournalKeySpace = "runtime-journal";
}
