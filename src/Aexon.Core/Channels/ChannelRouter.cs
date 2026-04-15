namespace Aexon.Core.Channels;

/// <summary>
/// Routes channel messages to the appropriate transport based on target prefix.
/// </summary>
public sealed class ChannelRouter
{
    private readonly IChannelTransport _bridgeTransport;
    private readonly IChannelTransport _udsTransport;

    public ChannelRouter(
        IChannelTransport bridgeTransport,
        IChannelTransport udsTransport)
    {
        _bridgeTransport = bridgeTransport;
        _udsTransport = udsTransport;
    }

    public static bool IsChannelTarget(string target) =>
        target.StartsWith("bridge:", StringComparison.OrdinalIgnoreCase) ||
        target.StartsWith("uds:", StringComparison.OrdinalIgnoreCase);

    public static ChannelKind? ParseChannelKind(string target)
    {
        if (target.StartsWith("bridge:", StringComparison.OrdinalIgnoreCase))
            return ChannelKind.Bridge;
        if (target.StartsWith("uds:", StringComparison.OrdinalIgnoreCase))
            return ChannelKind.Uds;
        return null;
    }

    public IChannelTransport? ResolveTransport(string target)
    {
        return ParseChannelKind(target) switch
        {
            ChannelKind.Bridge => _bridgeTransport,
            ChannelKind.Uds => _udsTransport,
            _ => null,
        };
    }
}
