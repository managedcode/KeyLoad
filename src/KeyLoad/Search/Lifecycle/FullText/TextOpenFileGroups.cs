using Microsoft.Extensions.Options;
using ZoneTree.AbstractFileStream;

namespace KeyLoad.Server.Features.Search;

internal sealed class TextOpenFileGroups
{
    private const int EmptyStreamCount = 0;
    private const long AbsentFileLength = 0;
    private const long NoAdditionalBytes = 0;
    private readonly Dictionary<string, TextOpenFileGroup> linked = new(StringComparer.Ordinal);
    private readonly HashSet<TextOpenFileGroup> owned = [];

    internal TextOpenFileGroup Open(string path, IFileStream stream)
    {
        if (!linked.TryGetValue(path, out var group))
        {
            group = new(path);
            linked.Add(path, group);
            owned.Add(group);
        }
        if (group.Ambiguous || !group.Streams.TryAdd(stream, new(stream)))
        {
            throw TextErrors.Ownership();
        }
        return group;
    }

    internal void BeginClose(TextOpenFileGroup group, IFileStream stream)
    {
        if (!owned.Contains(group) || !group.Streams.TryGetValue(stream, out var actual))
        {
            throw TextErrors.Ownership();
        }
        actual.BeginJoinedClose();
    }

    internal void Close(TextOpenFileGroup group, IFileStream stream)
    {
        if (!owned.Contains(group) || !group.Streams.Remove(stream))
        {
            throw TextErrors.Ownership();
        }
        if (group.Streams.Count != EmptyStreamCount)
        {
            return;
        }
        if (group.LinkedPath is { } path && linked.TryGetValue(path, out var current)
            && ReferenceEquals(current, group))
        {
            linked.Remove(path);
        }
        owned.Remove(group);
    }

    internal void RequireKnownPath(string path)
    {
        if (linked.TryGetValue(path, out var group) && group.Ambiguous)
        {
            throw TextErrors.Ownership();
        }
    }

    internal void DetachDirectory(string directory, bool ambiguous)
    {
        foreach (var path in linked.Keys.ToArray())
        {
            if (!path.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                continue;
            }
            if (ambiguous)
            {
                linked[path].Ambiguous = true;
            }
            else
            {
                Detach(path);
            }
        }
    }

    internal void Detach(string path)
    {
        if (linked.Remove(path, out var group))
        {
            group.LinkedPath = null;
        }
    }

    internal void Replace(string source, string destination, string? backup)
    {
        linked.Remove(source, out var sourceGroup);
        linked.Remove(destination, out var destinationGroup);
        if (backup is not null)
        {
            Detach(backup);
        }
        if (destinationGroup is not null)
        {
            destinationGroup.LinkedPath = backup;
            if (backup is not null)
            {
                linked.Add(backup, destinationGroup);
            }
        }
        if (sourceGroup is not null)
        {
            sourceGroup.LinkedPath = destination;
            linked.Add(destination, sourceGroup);
        }
    }

    internal void MarkAmbiguous(params string?[] paths)
    {
        foreach (var path in paths)
        {
            if (path is not null && linked.TryGetValue(path, out var group))
            {
                group.Ambiguous = true;
            }
        }
    }

    internal void Check(Dictionary<object, TextResourceFileReservation> reservations,
        ref int files, ref long bytes, IOptions<TextExecutionOptions> options)
    {
        foreach (var group in owned)
        {
            var actual = group.ObserveLength();
            if (reservations.TryGetValue(group, out var reservation))
            {
                actual = Math.Max(actual, reservation.TargetLength);
            }
            var linkedPath = group.LinkedPath;
            var exists = linkedPath is not null && File.Exists(linkedPath);
            var physical = exists ? new FileInfo(linkedPath!).Length : AbsentFileLength;
            var additional = group.Ambiguous ? actual : Math.Max(NoAdditionalBytes, actual - physical);
            if ((!exists || group.Ambiguous) && files >= options.Value.MaximumFiles
                || additional > options.Value.MaximumDiskBytes - bytes)
            {
                throw TextErrors.BoundExceeded();
            }
            if (!exists || group.Ambiguous)
            {
                files++;
            }
            bytes += additional;
        }
    }
}
