namespace ClaudeSharp.Tools.Shell;

/// <summary>
/// Defines bash command category values.
/// </summary>
public enum BashCommandCategory
{
    ReadOnly,
    Write,
    Destructive,
    Unknown,
}

/// <summary>
/// Represents bash command classification.
/// </summary>
public sealed record BashCommandClassification(
    BashCommandCategory Category,
    string BaseCommand,
    string? Reason = null);

/// <summary>
/// Represents bash command classifier.
/// </summary>
public static class BashCommandClassifier
{
    private static readonly HashSet<string> ReadOnlyCommands =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "[",
            "cat",
            "cd",
            "date",
            "diff",
            "echo",
            "env",
            "file",
            "find",
            "grep",
            "head",
            "hostname",
            "ls",
            "pwd",
            "printenv",
            "printf",
            "ps",
            "rg",
            "stat",
            "tail",
            "test",
            "type",
            "uname",
            "wc",
            "which",
            "whoami",
        };

    private static readonly HashSet<string> WriteCommands =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "chmod",
            "chown",
            "cp",
            "install",
            "ln",
            "mkdir",
            "mv",
            "tee",
            "touch",
        };

    private static readonly HashSet<string> DestructiveCommands =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "dd",
            "git-reset-hard",
            "git-clean",
            "git-checkout-force",
            "rm",
            "rmdir",
            "shred",
            "sudo",
        };

    private static readonly HashSet<string> GitReadOnlySubcommands =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "blame",
            "describe",
            "diff",
            "grep",
            "log",
            "ls-files",
            "remote",
            "rev-parse",
            "show",
            "shortlog",
            "status",
            "tag",
        };

    private static readonly HashSet<string> GitWriteSubcommands =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "add",
            "am",
            "apply",
            "branch",
            "checkout",
            "cherry-pick",
            "clone",
            "commit",
            "fetch",
            "merge",
            "pull",
            "push",
            "rebase",
            "restore",
            "revert",
            "stash",
            "switch",
            "worktree",
        };

    private static readonly HashSet<string> GitDestructiveSubcommands =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "clean",
            "reset",
        };

    public static BashCommandClassification Classify(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return new(BashCommandCategory.Unknown, string.Empty, "Command is empty");

        if (HasTopLevelWriteRedirection(command))
            return new(BashCommandCategory.Write, "redirection", "Contains shell redirection");

        var segments = SplitTopLevelSegments(command);
        if (segments.Count == 0)
            return new(BashCommandCategory.Unknown, string.Empty, "No command segments found");

        var overall = BashCommandCategory.ReadOnly;
        string? baseCommand = null;
        string? reason = null;

        foreach (var segment in segments)
        {
            var classification = ClassifySingleSegment(segment);
            if (string.IsNullOrEmpty(baseCommand))
                baseCommand = classification.BaseCommand;

            if (classification.Category == BashCommandCategory.Destructive)
                return classification;

            if (classification.Category == BashCommandCategory.Write)
                overall = BashCommandCategory.Write;
            else if (classification.Category == BashCommandCategory.Unknown &&
                     overall == BashCommandCategory.ReadOnly)
                overall = BashCommandCategory.Unknown;

            reason ??= classification.Reason;
        }

        return new(overall, baseCommand ?? string.Empty, reason);
    }

    private static BashCommandClassification ClassifySingleSegment(string command)
    {
        var tokens = Tokenize(command);
        if (tokens.Count == 0)
            return new(BashCommandCategory.Unknown, string.Empty, "No tokens found");

        var index = 0;
        while (index < tokens.Count && IsEnvironmentAssignment(tokens[index]))
            index++;

        while (index < tokens.Count && IsWrapperToken(tokens[index]))
        {
            if (string.Equals(tokens[index], "sudo", StringComparison.OrdinalIgnoreCase))
                return new(BashCommandCategory.Destructive, "sudo", "Uses sudo");

            index++;
        }

        if (index >= tokens.Count)
            return new(BashCommandCategory.Unknown, string.Empty, "Only wrappers detected");

        var baseCommand = tokens[index];
        var args = tokens.Skip(index + 1).ToList();

        if (ContainsInlineFileMutation(baseCommand, args))
            return new(BashCommandCategory.Write, baseCommand, "Contains inline file mutation flag");

        if (string.Equals(baseCommand, "git", StringComparison.OrdinalIgnoreCase))
            return ClassifyGit(args);

        if (string.Equals(baseCommand, "gh", StringComparison.OrdinalIgnoreCase))
            return ClassifyGitHubCli(args);

        if (DestructiveCommands.Contains(baseCommand))
            return new(BashCommandCategory.Destructive, baseCommand);

        if (WriteCommands.Contains(baseCommand))
            return new(BashCommandCategory.Write, baseCommand);

        if (ReadOnlyCommands.Contains(baseCommand))
            return new(BashCommandCategory.ReadOnly, baseCommand);

        return new(BashCommandCategory.Unknown, baseCommand, "Unknown command");
    }

    private static BashCommandClassification ClassifyGit(IReadOnlyList<string> args)
    {
        if (args.Count == 0)
            return new(BashCommandCategory.ReadOnly, "git", "Bare git command");

        var subcommand = args[0];
        var flags = args.Skip(1).ToList();

        if (GitDestructiveSubcommands.Contains(subcommand))
        {
            if (string.Equals(subcommand, "reset", StringComparison.OrdinalIgnoreCase) &&
                flags.Any(flag => string.Equals(flag, "--hard", StringComparison.OrdinalIgnoreCase)))
            {
                return new(BashCommandCategory.Destructive, "git", "git reset --hard");
            }

            return new(BashCommandCategory.Destructive, "git", $"git {subcommand}");
        }

        if (string.Equals(subcommand, "branch", StringComparison.OrdinalIgnoreCase))
        {
            if (flags.Count == 0 ||
                flags.All(flag => flag.StartsWith("-", StringComparison.Ordinal) &&
                    flag is "--all" or "-a" or "--remotes" or "-r" or "--list" or "--show-current"))
            {
                return new(BashCommandCategory.ReadOnly, "git", "git branch listing");
            }

            if (flags.Any(flag => flag is "-D" or "--delete" or "--force"))
                return new(BashCommandCategory.Destructive, "git", "git branch delete");

            return new(BashCommandCategory.Write, "git", "git branch mutation");
        }

        if (string.Equals(subcommand, "remote", StringComparison.OrdinalIgnoreCase))
        {
            if (flags.Count == 0 ||
                flags[0] is "show" or "-v" or "--verbose")
            {
                return new(BashCommandCategory.ReadOnly, "git", "git remote inspection");
            }

            return new(BashCommandCategory.Write, "git", "git remote mutation");
        }

        if (string.Equals(subcommand, "stash", StringComparison.OrdinalIgnoreCase))
        {
            if (flags.Count == 0 || flags[0] is "list" or "show")
                return new(BashCommandCategory.ReadOnly, "git", "git stash inspection");

            if (flags[0] is "drop" or "clear" or "pop")
                return new(BashCommandCategory.Destructive, "git", $"git stash {flags[0]}");

            return new(BashCommandCategory.Write, "git", "git stash mutation");
        }

        if (string.Equals(subcommand, "config", StringComparison.OrdinalIgnoreCase))
        {
            if (flags.Any(flag => flag is "--get" or "--get-all" or "--list"))
                return new(BashCommandCategory.ReadOnly, "git", "git config read");

            return new(BashCommandCategory.Write, "git", "git config mutation");
        }

        if (GitReadOnlySubcommands.Contains(subcommand))
            return new(BashCommandCategory.ReadOnly, "git", $"git {subcommand}");

        if (GitWriteSubcommands.Contains(subcommand))
        {
            if (string.Equals(subcommand, "checkout", StringComparison.OrdinalIgnoreCase) &&
                flags.Any(flag => string.Equals(flag, "--", StringComparison.Ordinal) ||
                                  string.Equals(flag, "-f", StringComparison.OrdinalIgnoreCase) ||
                                  string.Equals(flag, "--force", StringComparison.OrdinalIgnoreCase)))
            {
                return new(BashCommandCategory.Destructive, "git", "git checkout with force/path reset");
            }

            return new(BashCommandCategory.Write, "git", $"git {subcommand}");
        }

        return new(BashCommandCategory.Unknown, "git", $"Unknown git subcommand: {subcommand}");
    }

    private static BashCommandClassification ClassifyGitHubCli(IReadOnlyList<string> args)
    {
        if (args.Count == 0)
            return new(BashCommandCategory.Unknown, "gh", "Unknown gh command");

        if (args[0] is "pr" or "issue" or "run" or "repo" &&
            args.Count > 1 &&
            args[1] is "view" or "status" or "list")
        {
            return new(BashCommandCategory.ReadOnly, "gh", $"gh {args[0]} {args[1]}");
        }

        return new(BashCommandCategory.Unknown, "gh", "Unknown gh command");
    }

    private static bool ContainsInlineFileMutation(string baseCommand, IReadOnlyList<string> args)
    {
        if (string.Equals(baseCommand, "sed", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(baseCommand, "perl", StringComparison.OrdinalIgnoreCase))
        {
            return args.Any(arg =>
                arg.StartsWith("-i", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(arg, "--in-place", StringComparison.OrdinalIgnoreCase));
        }

        return false;
    }

    private static bool IsEnvironmentAssignment(string token)
    {
        var equalIndex = token.IndexOf('=');
        return equalIndex > 0 && !token[..equalIndex].Contains('/');
    }

    private static bool IsWrapperToken(string token) =>
        token is "command" or "builtin" or "env" or "noglob" or "time" or "sudo";

    private static bool HasTopLevelWriteRedirection(string command)
    {
        var inSingleQuotes = false;
        var inDoubleQuotes = false;

        for (var i = 0; i < command.Length; i++)
        {
            var current = command[i];

            if (current == '\'' && !inDoubleQuotes)
                inSingleQuotes = !inSingleQuotes;
            else if (current == '"' && !inSingleQuotes)
                inDoubleQuotes = !inDoubleQuotes;

            if (inSingleQuotes || inDoubleQuotes)
                continue;

            if (current == '>')
                return true;
        }

        return false;
    }

    private static List<string> SplitTopLevelSegments(string input)
    {
        var segments = new List<string>();
        var current = new System.Text.StringBuilder();
        var inSingleQuotes = false;
        var inDoubleQuotes = false;

        for (var i = 0; i < input.Length; i++)
        {
            var ch = input[i];

            if (ch == '\'' && !inDoubleQuotes)
            {
                inSingleQuotes = !inSingleQuotes;
                current.Append(ch);
                continue;
            }

            if (ch == '"' && !inSingleQuotes)
            {
                inDoubleQuotes = !inDoubleQuotes;
                current.Append(ch);
                continue;
            }

            if (!inSingleQuotes && !inDoubleQuotes)
            {
                if (ch == ';')
                {
                    AddSegment(segments, current);
                    continue;
                }

                if ((ch == '&' || ch == '|') && i + 1 < input.Length && input[i + 1] == ch)
                {
                    AddSegment(segments, current);
                    i++;
                    continue;
                }

                if (ch == '|')
                {
                    AddSegment(segments, current);
                    continue;
                }
            }

            current.Append(ch);
        }

        AddSegment(segments, current);
        return segments;
    }

    private static List<string> Tokenize(string command)
    {
        var tokens = new List<string>();
        var current = new System.Text.StringBuilder();
        var inSingleQuotes = false;
        var inDoubleQuotes = false;

        for (var i = 0; i < command.Length; i++)
        {
            var ch = command[i];

            if (ch == '\'' && !inDoubleQuotes)
            {
                inSingleQuotes = !inSingleQuotes;
                continue;
            }

            if (ch == '"' && !inSingleQuotes)
            {
                inDoubleQuotes = !inDoubleQuotes;
                continue;
            }

            if (!inSingleQuotes && !inDoubleQuotes && char.IsWhiteSpace(ch))
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }

                continue;
            }

            current.Append(ch);
        }

        if (current.Length > 0)
            tokens.Add(current.ToString());

        return tokens;
    }

    private static void AddSegment(ICollection<string> segments, System.Text.StringBuilder current)
    {
        var trimmed = current.ToString().Trim();
        if (!string.IsNullOrEmpty(trimmed))
            segments.Add(trimmed);

        current.Clear();
    }
}
