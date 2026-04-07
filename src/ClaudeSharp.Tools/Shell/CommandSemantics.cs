namespace ClaudeSharp.Tools.Shell;

/// <summary>
/// 命令退出码语义解释器。
/// 参考 Claude Code 的 tools/BashTool/commandSemantics.ts。
/// </summary>
public static class CommandSemantics
{
    private delegate CommandInterpretation Semantic(int exitCode, string stdout, string stderr);

    private static readonly IReadOnlyDictionary<string, Semantic> Semantics =
        new Dictionary<string, Semantic>(StringComparer.OrdinalIgnoreCase)
        {
            ["grep"] = (exitCode, _, _) => new CommandInterpretation(
                IsError: exitCode >= 2,
                Message: exitCode == 1 ? "No matches found" : null),
            ["rg"] = (exitCode, _, _) => new CommandInterpretation(
                IsError: exitCode >= 2,
                Message: exitCode == 1 ? "No matches found" : null),
            ["find"] = (exitCode, _, _) => new CommandInterpretation(
                IsError: exitCode >= 2,
                Message: exitCode == 1 ? "Some directories were inaccessible" : null),
            ["diff"] = (exitCode, _, _) => new CommandInterpretation(
                IsError: exitCode >= 2,
                Message: exitCode == 1 ? "Files differ" : null),
            ["test"] = (exitCode, _, _) => new CommandInterpretation(
                IsError: exitCode >= 2,
                Message: exitCode == 1 ? "Condition is false" : null),
            ["["] = (exitCode, _, _) => new CommandInterpretation(
                IsError: exitCode >= 2,
                Message: exitCode == 1 ? "Condition is false" : null),
        };

    public static CommandInterpretation Interpret(
        string command,
        int exitCode,
        string stdout,
        string stderr)
    {
        var baseCommand = ExtractBaseCommand(command);
        if (Semantics.TryGetValue(baseCommand, out var semantic))
            return semantic(exitCode, stdout, stderr);

        return new CommandInterpretation(
            IsError: exitCode != 0,
            Message: exitCode != 0 ? $"Command failed with exit code {exitCode}" : null);
    }

    private static string ExtractBaseCommand(string command)
    {
        var lastSegment = ExtractLastTopLevelSegment(command, '&', '|', ';');
        var lastPipelineCommand = ExtractLastTopLevelSegment(lastSegment, '|');

        return lastPipelineCommand
            .Trim()
            .Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault() ?? string.Empty;
    }

    private static string ExtractLastTopLevelSegment(string input, params char[] separators)
    {
        var segments = new List<string>();
        var start = 0;
        var inSingleQuotes = false;
        var inDoubleQuotes = false;

        for (var i = 0; i < input.Length; i++)
        {
            var current = input[i];

            if (current == '\'' && !inDoubleQuotes)
                inSingleQuotes = !inSingleQuotes;
            else if (current == '"' && !inSingleQuotes)
                inDoubleQuotes = !inDoubleQuotes;

            if (inSingleQuotes || inDoubleQuotes)
                continue;

            if (current == '&' && separators.Contains('&') && i + 1 < input.Length && input[i + 1] == '&')
            {
                segments.Add(input[start..i]);
                start = i + 2;
                i++;
                continue;
            }

            if (current == '|' && separators.Contains('|') && i + 1 < input.Length && input[i + 1] == '|')
            {
                segments.Add(input[start..i]);
                start = i + 2;
                i++;
                continue;
            }

            if (separators.Contains(current) && current == ';')
            {
                segments.Add(input[start..i]);
                start = i + 1;
                continue;
            }

            if (separators.Contains(current) && current == '|' &&
                !(i + 1 < input.Length && input[i + 1] == '|'))
            {
                segments.Add(input[start..i]);
                start = i + 1;
            }
        }

        segments.Add(input[start..]);
        return segments.LastOrDefault(segment => !string.IsNullOrWhiteSpace(segment)) ?? input;
    }
}

public readonly record struct CommandInterpretation(bool IsError, string? Message);
