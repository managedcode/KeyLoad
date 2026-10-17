namespace KeyLoad.Storage;

/// <summary>Minimum binary reader support required by a persisted store identity.</summary>
public static class StoreReaderContract
{
    /// <summary>No supported reader capability was recorded.</summary>
    public const int Unspecified = 0;
    /// <summary>The reader which supports the private native runtime journal family.</summary>
    public const int RuntimeJournal = 1;
    /// <summary>The reserved private canonical namespace requiring the journal reader.</summary>
    public const string RuntimeJournalKeySpace = "runtime-journal";
}
