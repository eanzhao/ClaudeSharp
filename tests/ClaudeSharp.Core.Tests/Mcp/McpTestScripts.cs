using ClaudeSharp.Core.Tests.Runtime;

namespace ClaudeSharp.Core.Tests.Mcp;

/// <summary>
/// Provides reusable MCP test process scripts.
/// </summary>
internal static class McpTestScripts
{
    public static string WriteShellScript(
        TempDirectory temp,
        string relativePath,
        string content)
    {
        var path = temp.FullPath(relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }
}
