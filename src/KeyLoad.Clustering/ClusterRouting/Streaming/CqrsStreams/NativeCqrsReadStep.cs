namespace KeyLoad.Clustering;

internal readonly record struct NativeCqrsReadStep<T>(bool HasItem, T? Item, Exception? Error);
