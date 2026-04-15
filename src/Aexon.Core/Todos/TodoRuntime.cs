namespace Aexon.Core.Todos;

/// <summary>
/// Defines todo status values.
/// </summary>
public enum TodoStatus
{
    Pending = 0,
    InProgress = 1,
    Completed = 2,
}

/// <summary>
/// Converts todo status values to and from tool-facing strings.
/// </summary>
public static class TodoStatusNames
{
    public static string ToValue(TodoStatus status) =>
        status switch
        {
            TodoStatus.Pending => "pending",
            TodoStatus.InProgress => "in_progress",
            TodoStatus.Completed => "completed",
            _ => "pending",
        };

    public static bool TryParse(string? value, out TodoStatus status)
    {
        status = TodoStatus.Pending;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        switch (value.Trim().ToLowerInvariant())
        {
            case "pending":
                status = TodoStatus.Pending;
                return true;

            case "in_progress":
            case "in-progress":
            case "inprogress":
                status = TodoStatus.InProgress;
                return true;

            case "completed":
            case "done":
                status = TodoStatus.Completed;
                return true;

            default:
                return false;
        }
    }
}

/// <summary>
/// Represents a session todo item.
/// </summary>
public sealed class TodoItem
{
    public required string Id { get; init; }
    public required string Title { get; set; }
    public TodoStatus Status { get; set; } = TodoStatus.Pending;
    public string? Description { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public TodoItem Clone() =>
        new()
        {
            Id = Id,
            Title = Title,
            Status = Status,
            Description = Description,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt,
        };
}

/// <summary>
/// Defines the contract for todo runtime state.
/// </summary>
public interface ITodoRuntime
{
    TodoItem CreateTodo(
        string id,
        string title,
        TodoStatus status = TodoStatus.Pending,
        string? description = null);

    TodoItem? GetTodo(string id);

    IReadOnlyList<TodoItem> ListTodos();

    bool UpdateTodo(string id, Action<TodoItem> update);

    bool DeleteTodo(string id);
}

/// <summary>
/// Provides in-memory todo runtime storage.
/// </summary>
public sealed class InMemoryTodoRuntime : ITodoRuntime
{
    private readonly object _gate = new();
    private readonly Dictionary<string, TodoItem> _todos =
        new(StringComparer.OrdinalIgnoreCase);

    public InMemoryTodoRuntime(IEnumerable<TodoItem>? todos = null)
    {
        if (todos == null)
            return;

        foreach (var todo in todos)
            _todos[todo.Id] = todo.Clone();
    }

    public TodoItem CreateTodo(
        string id,
        string title,
        TodoStatus status = TodoStatus.Pending,
        string? description = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        var now = DateTimeOffset.UtcNow;
        var todo = new TodoItem
        {
            Id = id.Trim(),
            Title = title.Trim(),
            Status = status,
            Description = NormalizeDescription(description),
            CreatedAt = now,
            UpdatedAt = now,
        };

        lock (_gate)
        {
            if (_todos.ContainsKey(todo.Id))
                throw new InvalidOperationException($"Todo '{todo.Id}' already exists.");

            _todos[todo.Id] = todo;
            return todo.Clone();
        }
    }

    public TodoItem? GetTodo(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return null;

        lock (_gate)
        {
            return _todos.TryGetValue(id.Trim(), out var todo)
                ? todo.Clone()
                : null;
        }
    }

    public IReadOnlyList<TodoItem> ListTodos()
    {
        lock (_gate)
        {
            return _todos.Values
                .OrderBy(todo => todo.CreatedAt)
                .ThenBy(todo => todo.Id, StringComparer.OrdinalIgnoreCase)
                .Select(todo => todo.Clone())
                .ToArray();
        }
    }

    public bool UpdateTodo(string id, Action<TodoItem> update)
    {
        if (string.IsNullOrWhiteSpace(id))
            return false;

        lock (_gate)
        {
            if (!_todos.TryGetValue(id.Trim(), out var todo))
                return false;

            update(todo);

            if (string.IsNullOrWhiteSpace(todo.Title))
                throw new InvalidOperationException("Todo title cannot be empty.");

            todo.Title = todo.Title.Trim();
            todo.Description = NormalizeDescription(todo.Description);
            todo.UpdatedAt = DateTimeOffset.UtcNow;
            return true;
        }
    }

    public bool DeleteTodo(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return false;

        lock (_gate)
            return _todos.Remove(id.Trim());
    }

    private static string? NormalizeDescription(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
}
