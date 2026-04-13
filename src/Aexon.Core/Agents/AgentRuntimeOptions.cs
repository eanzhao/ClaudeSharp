using System.Threading;

namespace Aexon.Core.Agents;

/// <summary>
/// Holds mutable subagent runtime options that can change during a session.
/// </summary>
public sealed class AgentRuntimeOptions
{
    private int _autoResumeMode = (int)AgentAutoResumeMode.Queue;

    public AgentAutoResumeMode AutoResumeMode
    {
        get => (AgentAutoResumeMode)Volatile.Read(ref _autoResumeMode);
        set => Volatile.Write(ref _autoResumeMode, (int)value);
    }
}
