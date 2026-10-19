namespace KeyLoad.Clustering;

internal readonly record struct CqrsReadStep<T>(bool HasItem, T? Item, Exception? Error);
