using System.Globalization;
using SoulLink.Core.Decisions;

namespace SoulLink.Core.Sync;

/// <summary>Formats operation arguments. Numbers use the invariant culture and flags are "1" or "0".</summary>
public static class SyncArgs
{
    public static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);

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

/// <summary>Encodes a host commit so clients can apply exactly the same commit.</summary>
internal static class CommitCodec
{
    public static IReadOnlyList<string> Encode<TAtom>(Commit<TAtom> commit)
        where TAtom : notnull =>
    [
        SyncArgs.Number(commit.Sequence),
        commit.Kind.ToString(),
        commit.Follows is { } follows ? SyncArgs.Number(follows) : "",
        SyncArgs.Flag(commit.Overrides),
        commit.Mode.ToString(),
        .. commit.Link.Parts,
    ];

    public static Commit<TAtom> Decode<TAtom>(PlayerId player, Selection<TAtom> choice, IReadOnlyList<string> result)
        where TAtom : notnull
    {
        var reader = new ArgReader(result);
        var sequence = reader.Int();
        var kind = reader.Enum<CommitKind>();
        var follows = reader.OptionalInt();
        var overrides = reader.Bool();
        var mode = reader.Enum<LinkMode>();
        return new Commit<TAtom>(sequence, player, choice, new LinkKey(reader.Rest()), kind, follows, overrides, mode);
    }
}
