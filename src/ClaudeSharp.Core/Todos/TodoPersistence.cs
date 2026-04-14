using System.Text.Json;
using ClaudeSharp.Core.Storage;

namespace ClaudeSharp.Core.Todos;

/// <summary>
/// Represents a restored snapshot of todo state.
/// </summary>
public sealed class TodoStateSnapshot
{
    public IReadOnlyList<TodoItem> Todos { get; init; } = [];
}

/// <summary>
/// Serializes todo runtime state into transcript metadata and restores it on resume.
/// </summary>
public static class TodoPersistence
{
    public const string TodoEventType = "todo-item";
    public const string TodoDeletedEventType = "todo-item-deleted";

    public static TranscriptMetadataEntry CreateTodoEntry(
        TodoItem todo,
        DateTimeOffset? recordedAt = null) =>
        new(
            TodoEventType,
            JsonSerializer.SerializeToElement(new TodoPayload
            {
                Id = todo.Id,
                Title = todo.Title,
                Status = todo.Status,
                Description = todo.Description,
                CreatedAt = todo.CreatedAt,
                UpdatedAt = todo.UpdatedAt,
            }),
            recordedAt ?? todo.UpdatedAt);

    public static TranscriptMetadataEntry CreateTodoDeletedEntry(
        string todoId,
        DateTimeOffset? recordedAt = null) =>
        new(
            TodoDeletedEventType,
            JsonSerializer.SerializeToElement(new DeletedTodoPayload
            {
                Id = todoId,
            }),
            recordedAt ?? DateTimeOffset.UtcNow);

    public static TodoStateSnapshot Restore(
        IReadOnlyList<TranscriptMetadataEntry> metadataEntries)
    {
        var todos = new Dictionary<string, TodoItem>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in metadataEntries)
        {
            switch (entry.EventType)
            {
                case TodoEventType:
                    if (TryReadPayload(entry.Payload, out TodoPayload? todoPayload) &&
                        todoPayload != null &&
                        !string.IsNullOrWhiteSpace(todoPayload.Id))
                    {
                        todos[todoPayload.Id] = new TodoItem
                        {
                            Id = todoPayload.Id,
                            Title = todoPayload.Title,
                            Status = todoPayload.Status,
                            Description = todoPayload.Description,
                            CreatedAt = todoPayload.CreatedAt,
                            UpdatedAt = todoPayload.UpdatedAt,
                        };
                    }

                    break;

                case TodoDeletedEventType:
                    if (TryReadPayload(entry.Payload, out DeletedTodoPayload? deletedTodo) &&
                        deletedTodo != null &&
                        !string.IsNullOrWhiteSpace(deletedTodo.Id))
                    {
                        todos.Remove(deletedTodo.Id);
                    }

                    break;
            }
        }

        return new TodoStateSnapshot
        {
            Todos = todos.Values
                .OrderBy(todo => todo.CreatedAt)
                .ThenBy(todo => todo.Id, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
        };
    }

    private static bool TryReadPayload<T>(
        JsonElement? payload,
        out T? value)
    {
        value = default;
        if (payload is not JsonElement element ||
            element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return false;
        }

        try
        {
            value = element.Deserialize<T>();
            return value != null;
        }
        catch
        {
            return false;
        }
    }

    private sealed class TodoPayload
    {
        public required string Id { get; init; }
        public required string Title { get; init; }
        public required TodoStatus Status { get; init; }
        public string? Description { get; init; }
        public required DateTimeOffset CreatedAt { get; init; }
        public required DateTimeOffset UpdatedAt { get; init; }
    }

    private sealed class DeletedTodoPayload
    {
        public required string Id { get; init; }
    }
}
