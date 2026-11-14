using System.Globalization;
using SoulLink.Core.Decisions;

namespace SoulLink.Core.Sync;

/// <summary>Formats operation arguments. Numbers use the invariant culture and flags are "1" or "0".</summary>
public static class SyncArgs
{
    public static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);

    public static string Player(PlayerId player) => player.Value.ToString(CultureInfo.InvariantCulture);

    public static string Flag(bool value) => value ? "1" : "0";
}

/// <summary>Reads operation arguments in order, rejecting the operation when they're malformed.</summary>
internal sealed class ArgReader(IReadOnlyList<string> args)
{
    private int _next;

    public bool HasMore => _next < args.Count;

    public string String() =>
        _next < args.Count ? args[_next++] : throw new SyncRejectedException("An argument is missing.");

    public int Int() =>
        int.TryParse(String(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new SyncRejectedException("An argument isn't a number.");

    public long Long() =>
        long.TryParse(String(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new SyncRejectedException("An argument isn't a number.");

    /// <summary>A list length, from 0 to <see cref="SyncCodec.MaxListLength"/>.</summary>
    public int Count()
    {
        var count = Int();
        return count is >= 0 and <= SyncCodec.MaxListLength ? count : throw new SyncRejectedException("A list length is out of range.");
    }

    public PlayerId Player() =>
        ulong.TryParse(String(), NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            ? new PlayerId(value)
            : throw new SyncRejectedException("An argument isn't a player.");

    /// <summary>The next <paramref name="count"/> arguments.</summary>
    public List<string> Strings(int count)
    {
        var strings = new List<string>(count);
        for (var i = 0; i < count; i++)
        {
            strings.Add(String());
        }

        return strings;
    }

    /// <summary>A number, or null for an empty argument.</summary>
    public int? OptionalInt()
    {
        var text = String();
        if (text.Length == 0)
        {
            return null;
        }

        return int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new SyncRejectedException("An argument isn't a number.");
    }

    public bool Bool() => String() switch
    {
        "1" => true,
        "0" => false,
        _ => throw new SyncRejectedException("An argument isn't a flag."),
    };

    public TEnum Enum<TEnum>()
        where TEnum : struct, Enum =>
        System.Enum.TryParse<TEnum>(String(), ignoreCase: false, out var value) && System.Enum.IsDefined(value)
            ? value
            : throw new SyncRejectedException($"An argument isn't a {typeof(TEnum).Name}.");

    public List<string> Rest()
    {
        var rest = new List<string>();
        while (HasMore)
        {
            rest.Add(String());
        }

        return rest;
    }

    public void End()
    {
        if (HasMore)
        {
            throw new SyncRejectedException("There are too many arguments.");
        }
    }
}

/// <summary>
/// Encodes the commits the host made while applying an operation, for any player, so clients apply exactly the same
/// commits instead of working them out from their own copy of the links.
/// </summary>
internal static class CommitCodec
{
    /// <summary>
    /// A count, then for each commit: the player, the choice's length and items, the sequence, kind, followed commit,
    /// override flag and mode, then the link's length and parts.
    /// </summary>
    public static IEnumerable<string> Encode<TAtom>(IEnumerable<Commit<TAtom>> commits, Func<TAtom, string> format)
        where TAtom : notnull
    {
        var list = commits.ToList();
        yield return SyncArgs.Number(list.Count);
        foreach (var commit in list)
        {
            yield return SyncArgs.Player(commit.Player);
            yield return SyncArgs.Number(commit.Choice.Count);
            foreach (var item in commit.Choice.Items)
            {
                yield return format(item);
            }

            yield return SyncArgs.Number(commit.Sequence);
            yield return commit.Kind.ToString();
            yield return commit.Follows is { } follows ? SyncArgs.Number(follows) : "";
            yield return SyncArgs.Flag(commit.Overrides);
            yield return commit.Mode.ToString();
            yield return SyncArgs.Number(commit.Link.Parts.Count);
            foreach (var part in commit.Link.Parts)
            {
                yield return part;
            }
        }
    }

    public static List<Commit<TAtom>> Decode<TAtom>(ArgReader reader, Func<string, TAtom> parse)
        where TAtom : notnull
    {
        var count = reader.Count();
        var commits = new List<Commit<TAtom>>(count);
        for (var i = 0; i < count; i++)
        {
            var player = reader.Player();
            var choice = new Selection<TAtom>(reader.Strings(reader.Count()).Select(parse));
            var sequence = reader.Int();
            var kind = reader.Enum<CommitKind>();
            var follows = reader.OptionalInt();
            var overrides = reader.Bool();
            var mode = reader.Enum<LinkMode>();
            var link = new LinkKey(reader.Strings(reader.Count()));
            commits.Add(new Commit<TAtom>(sequence, player, choice, link, kind, follows, overrides, mode));
        }

        return commits;
    }
}
