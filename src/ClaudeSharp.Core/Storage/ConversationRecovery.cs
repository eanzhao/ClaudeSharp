using ClaudeSharp.Core.Compaction;
using ClaudeSharp.Core.Messages;

namespace ClaudeSharp.Core.Storage;

public sealed record ResumeSource(bool ContinueLatest, string? SessionIdOrPath = null)
{
    public static ResumeSource Latest() => new(true);

    public static ResumeSource Session(string sessionIdOrPath) =>
        new(false, sessionIdOrPath);
}

public sealed class ResumeLoadResult
{
    public required TranscriptSession Session { get; init; }
    public required IReadOnlyList<ConversationMessage> Messages { get; init; }
    public required TokenUsage TotalUsage { get; init; }
    public required ConversationSessionMetadata Metadata { get; init; }
}

public interface ISessionResumeLoader
{
    Task<ResumeLoadResult?> LoadAsync(
        ResumeSource source,
        CancellationToken cancellationToken = default);
}

public interface IConversationRecovery
{
    ResumeLoadResult Recover(TranscriptProjection projection);
}

public sealed class SessionResumeLoader : ISessionResumeLoader
{
    private readonly ITranscriptStore _store;
    private readonly IConversationRecovery _recovery;

    public SessionResumeLoader(
        ITranscriptStore store,
        IConversationRecovery recovery)
    {
        _store = store;
        _recovery = recovery;
    }

    public async Task<ResumeLoadResult?> LoadAsync(
        ResumeSource source,
        CancellationToken cancellationToken = default)
    {
        var session = source.ContinueLatest
            ? await _store.GetLatestSessionAsync(cancellationToken)
            : await _store.FindSessionAsync(source.SessionIdOrPath ?? string.Empty, cancellationToken);

        if (session == null)
            return null;

        var projection = await _store.LoadProjectionAsync(
            session,
            new TranscriptLoadOptions(),
            cancellationToken);

        return _recovery.Recover(projection);
    }
}

public sealed class ConversationRecovery : IConversationRecovery
{
    public ResumeLoadResult Recover(TranscriptProjection projection)
    {
        var chain = BuildChain(projection);
        var cleaned = CleanupMessages(chain);

        return new ResumeLoadResult
        {
            Session = projection.Session,
            Messages = cleaned,
            TotalUsage = ComputeTotalUsage(cleaned),
            Metadata = projection.Session.Metadata.Clone(),
        };
    }

    private static IReadOnlyList<ConversationMessage> BuildChain(TranscriptProjection projection)
    {
        if (projection.MessagesById.Count == 0)
            return Array.Empty<ConversationMessage>();

        var checkpoint = FindLatestCheckpoint(projection.MetadataEntries);
        if (checkpoint != null)
        {
            var checkpointChain = BuildCheckpointChain(projection, checkpoint);
            if (checkpointChain.Count > 0)
                return checkpointChain;
        }

        var leafId = projection.Session.CurrentLeafMessageId;
        if (string.IsNullOrWhiteSpace(leafId))
        {
            if (projection.MetadataEntries.Any(entry =>
                    string.Equals(entry.EventType, "reset-head", StringComparison.OrdinalIgnoreCase)))
            {
                return Array.Empty<ConversationMessage>();
            }

            leafId = projection.MessagesById
                .OrderByDescending(pair => pair.Value.Sequence)
                .Select(pair => pair.Key)
                .FirstOrDefault();
        }
        else if (!projection.MessagesById.ContainsKey(leafId))
        {
            leafId = projection.MessagesById
                .OrderByDescending(pair => pair.Value.Sequence)
                .Select(pair => pair.Key)
                .FirstOrDefault();
        }

        if (string.IsNullOrWhiteSpace(leafId))
            return Array.Empty<ConversationMessage>();

        var chain = new List<ConversationMessage>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var currentId = leafId;

        while (!string.IsNullOrWhiteSpace(currentId) &&
               projection.MessagesById.TryGetValue(currentId, out var stored))
        {
            if (!visited.Add(currentId))
                break;

            chain.Add(stored.Message);
            currentId = stored.ParentMessageId;
        }

        chain.Reverse();
        return chain;
    }

    private static ConversationCheckpoint? FindLatestCheckpoint(
        IReadOnlyList<TranscriptMetadataEntry> metadataEntries)
    {
        ConversationCheckpoint? checkpoint = null;
        foreach (var entry in metadataEntries)
        {
            if (string.Equals(entry.EventType, "reset-head", StringComparison.OrdinalIgnoreCase))
            {
                checkpoint = null;
                continue;
            }

            if (ConversationCheckpoint.TryParse(entry, out var parsed))
                checkpoint = parsed;
        }

        return checkpoint;
    }

    private static IReadOnlyList<ConversationMessage> BuildCheckpointChain(
        TranscriptProjection projection,
        ConversationCheckpoint checkpoint)
    {
        var activeMessages = new List<ConversationMessage>(checkpoint.ActiveMessageIds.Count);
        foreach (var id in checkpoint.ActiveMessageIds)
        {
            if (!projection.MessagesById.TryGetValue(id, out var stored))
                return Array.Empty<ConversationMessage>();

            activeMessages.Add(stored.Message);
        }

        var leafId = checkpoint.LeafMessageId;
        if (string.IsNullOrWhiteSpace(leafId))
            return activeMessages;

        var currentLeafId = projection.Session.CurrentLeafMessageId;
        if (string.IsNullOrWhiteSpace(currentLeafId) ||
            string.Equals(currentLeafId, leafId, StringComparison.Ordinal))
        {
            return activeMessages;
        }

        var tail = new List<ConversationMessage>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var cursor = currentLeafId;

        while (!string.IsNullOrWhiteSpace(cursor) &&
               projection.MessagesById.TryGetValue(cursor, out var stored))
        {
            if (!visited.Add(cursor))
                return Array.Empty<ConversationMessage>();

            if (string.Equals(cursor, leafId, StringComparison.Ordinal))
            {
                tail.Reverse();
                return activeMessages.Concat(tail).ToList();
            }

            tail.Add(stored.Message);
            cursor = stored.ParentMessageId;
        }

        return Array.Empty<ConversationMessage>();
    }

    private static IReadOnlyList<ConversationMessage> CleanupMessages(
        IReadOnlyList<ConversationMessage> messages)
    {
        var cleaned = new List<ConversationMessage>(messages.Count);

        foreach (var message in messages)
        {
            switch (message)
            {
                case AssistantMessage assistant when !assistant.Content.Any():
                    continue;

                case AssistantMessage assistant when assistant.Content.All(block =>
                         block is TextBlock text && string.IsNullOrWhiteSpace(text.Text)):
                    continue;

                default:
                    cleaned.Add(message);
                    break;
            }
        }

        return cleaned;
    }

    private static TokenUsage ComputeTotalUsage(
        IReadOnlyList<ConversationMessage> messages)
    {
        var total = TokenUsage.Empty;
        foreach (var assistant in messages.OfType<AssistantMessage>())
        {
            if (assistant.Usage != null)
                total += assistant.Usage;
        }

        return total;
    }
}
