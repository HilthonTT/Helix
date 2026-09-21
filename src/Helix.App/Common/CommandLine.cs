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

internal sealed record CommandRequest(CommandVerb Verb, string Target = "")
{
    public const char Separator = '\u001f';

    public const char LineBreak = '\u001e';

    public static CommandRequest Parse(IReadOnlyList<string> arguments)
    {
        if (arguments.Count == 0)
        {
            return new CommandRequest(CommandVerb.None);
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

    public bool NeedsTarget => Verb is
        CommandVerb.Connect or
        CommandVerb.Disconnect or
        CommandVerb.ConnectGroup or
        CommandVerb.DisconnectGroup or
        CommandVerb.Wake;

    public string Encode() => string.Join(Separator, Verb.ToString(), Target);

    public static CommandRequest Decode(string payload)
    {
        string[] parts = payload.Split(Separator);

        return new CommandRequest(
            Enum.TryParse(parts[0], out CommandVerb verb) ? verb : CommandVerb.Unknown,
            parts.Length > 1 ? parts[1] : string.Empty);
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
        "  --help                    print this");
}
