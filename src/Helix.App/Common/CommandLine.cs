namespace Helix.App.Common;

internal enum CommandVerb
{
    None,
    Unknown,
    Help,
    Show,
    Quit,
    Status,
    ConnectAll,
    DisconnectAll,
    Connect,
    Disconnect,
    ConnectGroup,
    DisconnectGroup,
    Wake,
}

internal sealed record CommandRequest(CommandVerb Verb, string Target = "", bool FromLink = false)
{
    public const char Separator = '\u001f';

    public const char LineBreak = '\u001e';

    public const string Scheme = "helix";

    public static CommandRequest Parse(IReadOnlyList<string> arguments)
    {
        if (arguments.Count == 0)
        {
            return new CommandRequest(CommandVerb.None);
        }

        if (arguments.Count == 1 && IsLink(arguments[0]))
        {
            return FromUri(arguments[0]);
        }

        string verb = arguments[0].Trim();
        string target = arguments.Count > 1 ? string.Join(' ', arguments.Skip(1)).Trim() : string.Empty;

        return verb.ToLowerInvariant() switch
        {
            "--help" or "-h" or "-?" or "/?" => new CommandRequest(CommandVerb.Help),
            "--show" => new CommandRequest(CommandVerb.Show),
            "--quit" => new CommandRequest(CommandVerb.Quit),
            "--status" => new CommandRequest(CommandVerb.Status),
            "--connect-all" => new CommandRequest(CommandVerb.ConnectAll),
            "--disconnect-all" => new CommandRequest(CommandVerb.DisconnectAll),
            "--connect" => new CommandRequest(CommandVerb.Connect, target),
            "--disconnect" => new CommandRequest(CommandVerb.Disconnect, target),
            "--connect-group" => new CommandRequest(CommandVerb.ConnectGroup, target),
            "--disconnect-group" => new CommandRequest(CommandVerb.DisconnectGroup, target),
            "--wake" => new CommandRequest(CommandVerb.Wake, target),
            _ => new CommandRequest(CommandVerb.Unknown, verb),
        };
    }

    public static bool IsLink(string argument) =>
        argument.TrimStart().StartsWith(Scheme + ":", StringComparison.OrdinalIgnoreCase);

    public static CommandRequest FromUri(string link)
    {
        if (!Uri.TryCreate(link.Trim(), UriKind.Absolute, out Uri? uri) ||
            !string.Equals(uri.Scheme, Scheme, StringComparison.OrdinalIgnoreCase))
        {
            return new CommandRequest(CommandVerb.Unknown, link, FromLink: true);
        }

        string path = (uri.Host + "/" + uri.AbsolutePath).Trim('/');

        int slash = path.IndexOf('/');
        string verb = slash < 0 ? path : path[..slash];
        string target = slash < 0
            ? string.Empty
            : new string([.. Uri.UnescapeDataString(path[(slash + 1)..]).Where(c => !char.IsControl(c))]).Trim('/', ' ');

        if (verb.Length == 0)
        {
            return new CommandRequest(CommandVerb.Show, FromLink: true);
        }

        CommandRequest request = Parse(target.Length == 0 ? [$"--{verb}"] : [$"--{verb}", target]);

        return request.Verb == CommandVerb.Unknown
            ? new CommandRequest(CommandVerb.Unknown, link, FromLink: true)
            : request with { FromLink = true };
    }

    public bool NeedsConfirmation => FromLink && Verb is
        CommandVerb.Disconnect or
        CommandVerb.DisconnectAll or
        CommandVerb.DisconnectGroup or
        CommandVerb.Quit;

    public bool NeedsTarget => Verb is
        CommandVerb.Connect or
        CommandVerb.Disconnect or
        CommandVerb.ConnectGroup or
        CommandVerb.DisconnectGroup or
        CommandVerb.Wake;

    public string Encode() => string.Join(Separator, Verb.ToString(), Target, FromLink ? "link" : string.Empty);

    public static CommandRequest Decode(string payload)
    {
        string[] parts = payload.Split(Separator);

        return new CommandRequest(
            Enum.TryParse(parts[0], out CommandVerb verb) ? verb : CommandVerb.Unknown,
            parts.Length > 1 ? parts[1] : string.Empty,
            parts.Length > 2 && parts[2] == "link");
    }

    public static string Usage => string.Join(
        LineBreak,
        "Helix accepts these while it is running and signed in:",
        "",
        "  --status                  list every drive and whether it is up",
        "  --connect-all             connect every drive",
        "  --disconnect-all          disconnect every drive",
        "  --connect <drive>         connect one drive, by letter or name",
        "  --disconnect <drive>      disconnect one drive, by letter or name",
        "  --connect-group <name>    connect a drive group",
        "  --disconnect-group <name> disconnect a drive group",
        "  --wake <drive>            send that drive's NAS a wake-up packet",
        "  --show                    bring the window forward",
        "  --quit                    close Helix",
        "  --help                    print this",
        "",
        "Each is also a link, for a Stream Deck button or a bookmark:",
        "helix://connect/Z, helix://connect-group/Office, helix://disconnect-all.",
        "A link that disconnects or quits asks in the window first.");
}
