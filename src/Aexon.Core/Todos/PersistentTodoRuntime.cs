using Aexon.Core.Storage;

namespace Aexon.Core.Todos;

/// <summary>
/// Persists todo runtime state into the conversation transcript.
/// </summary>
public sealed class PersistentTodoRuntime : ITodoRuntime
{
    private readonly InMemoryTodoRuntime _inner;
    private readonly IConversationJournal _journal;

    private PersistentTodoRuntime(
        InMemoryTodoRuntime inner,
        IConversationJournal journal)
    {
        _inner = inner;
        _journal = journal;
    }

    public static Task<PersistentTodoRuntime> CreateAsync(
        IConversationJournal journal,
        IReadOnlyList<TranscriptMetadataEntry>? metadataEntries = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var restored = TodoPersistence.Restore(metadataEntries ?? []);
        var runtime = new PersistentTodoRuntime(
            new InMemoryTodoRuntime(restored.Todos),
            journal);
        return Task.FromResult(runtime);
    }

    public TodoItem CreateTodo(
        string id,
        string title,
        TodoStatus status = TodoStatus.Pending,
        string? description = null)
    {
        var todo = _inner.CreateTodo(id, title, status, description);
        Persist(TodoPersistence.CreateTodoEntry(todo));
        return todo;
    }

    public TodoItem? GetTodo(string id) => _inner.GetTodo(id);

    public IReadOnlyList<TodoItem> ListTodos() => _inner.ListTodos();

    public bool UpdateTodo(string id, Action<TodoItem> update)
    {
        var updated = _inner.UpdateTodo(id, update);
        if (updated && _inner.GetTodo(id) is { } todo)
            Persist(TodoPersistence.CreateTodoEntry(todo));

        return updated;
    }

    public bool DeleteTodo(string id)
    {
        var deleted = _inner.DeleteTodo(id);
        if (deleted)
            Persist(TodoPersistence.CreateTodoDeletedEntry(id.Trim()));

        return deleted;
    }

    private void Persist(TranscriptMetadataEntry entry)
    {
        try
        {
            _journal.AppendMetadataEntryAsync(entry).GetAwaiter().GetResult();
        }
        catch
        {
            // Todo persistence is best-effort; in-memory state still updates.
        }
    }
}
