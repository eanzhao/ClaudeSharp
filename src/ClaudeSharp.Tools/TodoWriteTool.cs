using System.Text;
using System.Text.Json;
using ClaudeSharp.Core.Todos;
using ClaudeSharp.Core.Tools;

namespace ClaudeSharp.Tools;

/// <summary>
/// Provides a session-scoped todo management tool.
/// </summary>
public sealed class TodoWriteTool : ITool
{
    private readonly ITodoRuntime _runtime;

    public TodoWriteTool(ITodoRuntime runtime)
    {
        _runtime = runtime;
    }

    public string Name => "TodoWrite";

    public string[] Aliases => ["TodoWriteTool"];

    public Task<string> GetDescriptionAsync() =>
        Task.FromResult("Create, update, delete, and list the session todo list.");

    public JsonElement GetInputSchema()
    {
        var schema = """
        {
            "type": "object",
            "properties": {
                "operation": {
                    "type": "string",
                    "enum": ["create", "update", "delete", "list"],
                    "description": "Todo operation to perform"
                },
                "todos": {
                    "type": "array",
                    "description": "Todo items to create, update, or delete. Use an empty array for list.",
                    "items": {
                        "type": "object",
                        "properties": {
                            "id": {
                                "type": "string",
                                "description": "Stable todo id"
                            },
                            "title": {
                                "type": "string",
                                "description": "Short todo title"
                            },
                            "status": {
                                "type": "string",
                                "enum": ["pending", "in_progress", "completed"],
                                "description": "Todo status"
                            },
                            "description": {
                                "type": "string",
                                "description": "Optional todo details"
                            }
                        },
                        "required": ["id"],
                        "additionalProperties": false
                    }
                }
            },
            "required": ["operation", "todos"],
            "additionalProperties": false
        }
        """;

        return JsonDocument.Parse(schema).RootElement.Clone();
    }

    public Task<string> GetPromptAsync(ToolPromptContext context)
    {
        var currentTodos = RenderTodos(_runtime.ListTodos());
        return Task.FromResult($"""
            Manage the session todo list used for planning and progress tracking.

            Use this tool when the task has multiple meaningful steps, when progress needs to stay visible, or when you need to revise the plan mid-task.

            Supported operations:
            - create: add one or more todo items
            - update: change fields on existing todo items
            - delete: remove todo items by id
            - list: inspect the current todo list

            Todo rules:
            - Each todo has id, title, status, and optional description
            - Keep titles short, concrete, and action-oriented
            - Prefer only one todo in `in_progress` at a time
            - Mark items `completed` as soon as they are done

            Current todo list:
            {currentTodos}
            """);
    }

    public Task<ValidationResult> ValidateInputAsync(
        JsonElement input,
        ToolExecutionContext context)
    {
        return Task.FromResult(ValidateAndParseInput(input).Validation);
    }

    public Task<ToolResult> ExecuteAsync(
        JsonElement input,
        ToolExecutionContext context,
        IProgress<ToolProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var parsed = ValidateAndParseInput(input);
        if (!parsed.Validation.IsValid || parsed.Value == null)
            return Task.FromResult(ToolResult.Error(parsed.Validation.Message ?? "Invalid todo input."));

        cancellationToken.ThrowIfCancellationRequested();

        switch (parsed.Value.Operation)
        {
            case TodoOperation.List:
                return Task.FromResult(ToolResult.Success(BuildResult("Current todos", _runtime.ListTodos())));

            case TodoOperation.Create:
                foreach (var todo in parsed.Value.Todos)
                {
                    _runtime.CreateTodo(
                        todo.Id,
                        todo.Title!,
                        todo.Status ?? TodoStatus.Pending,
                        todo.Description);
                }

                return Task.FromResult(ToolResult.Success(
                    BuildResult(
                        $"Created {parsed.Value.Todos.Count} todo(s)",
                        _runtime.ListTodos())));

            case TodoOperation.Update:
                foreach (var todo in parsed.Value.Todos)
                {
                    _runtime.UpdateTodo(todo.Id, existing =>
                    {
                        if (todo.HasTitle)
                            existing.Title = todo.Title!;
                        if (todo.HasStatus && todo.Status is { } status)
                            existing.Status = status;
                        if (todo.HasDescription)
                            existing.Description = todo.Description;
                    });
                }

                return Task.FromResult(ToolResult.Success(
                    BuildResult(
                        $"Updated {parsed.Value.Todos.Count} todo(s)",
                        _runtime.ListTodos())));

            case TodoOperation.Delete:
                foreach (var todo in parsed.Value.Todos)
                    _runtime.DeleteTodo(todo.Id);

                return Task.FromResult(ToolResult.Success(
                    BuildResult(
                        $"Deleted {parsed.Value.Todos.Count} todo(s)",
                        _runtime.ListTodos())));

            default:
                return Task.FromResult(ToolResult.Error("Unsupported todo operation."));
        }
    }

    public bool IsReadOnly(JsonElement input) =>
        TryGetOperation(input, out var operation) &&
        operation == TodoOperation.List;

    public bool IsConcurrencySafe(JsonElement input) => IsReadOnly(input);

    public string GetUserFacingName(JsonElement? input = null) => "Todo list";

    public string? GetActivityDescription(JsonElement? input)
    {
        if (input == null || !TryGetOperation(input.Value, out var operation))
            return "Updating todo list";

        return operation switch
        {
            TodoOperation.Create => "Creating todos",
            TodoOperation.Update => "Updating todos",
            TodoOperation.Delete => "Deleting todos",
            TodoOperation.List => "Reading todos",
            _ => "Updating todo list",
        };
    }

    private static ParseResult ParseInputShape(
        JsonElement input)
    {
        if (input.ValueKind != JsonValueKind.Object)
            return ParseResult.Invalid("TodoWrite input must be an object.");

        if (!TryGetOperation(input, out var operation))
            return ParseResult.Invalid("operation must be one of create, update, delete, list.");

        if (!input.TryGetProperty("todos", out var todos) ||
            todos.ValueKind != JsonValueKind.Array)
        {
            return ParseResult.Invalid("todos must be an array.");
        }

        if (operation == TodoOperation.List)
        {
            return ParseResult.Valid(new ParsedInput(operation, []));
        }

        var parsedTodos = new List<ParsedTodo>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var index = 0;

        foreach (var item in todos.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                return ParseResult.Invalid($"todos[{index}] must be an object.");

            if (!TryGetOptionalString(item, "id", out var id, out var idError))
                return ParseResult.Invalid($"todos[{index}].{idError}");
            if (string.IsNullOrWhiteSpace(id))
                return ParseResult.Invalid($"todos[{index}].id is required.");

            if (!ids.Add(id))
                return ParseResult.Invalid($"Duplicate todo id '{id}' in a single call is not allowed.");

            var hasTitle = item.TryGetProperty("title", out _);
            if (!TryGetOptionalString(item, "title", out var title, out var titleError))
                return ParseResult.Invalid($"todos[{index}].{titleError}");

            var hasDescription = item.TryGetProperty("description", out _);
            if (!TryGetOptionalString(item, "description", out var description, out var descriptionError))
                return ParseResult.Invalid($"todos[{index}].{descriptionError}");

            var hasStatus = item.TryGetProperty("status", out _);
            TodoStatus? status = null;

            if (hasStatus)
            {
                if (!TryGetOptionalString(item, "status", out var statusText, out var statusError))
                    return ParseResult.Invalid($"todos[{index}].{statusError}");

                if (!TodoStatusNames.TryParse(statusText, out var parsedStatus))
                {
                    return ParseResult.Invalid(
                        $"todos[{index}].status must be one of pending, in_progress, completed.");
                }

                status = parsedStatus;
            }

            parsedTodos.Add(new ParsedTodo(
                id,
                hasTitle ? title : null,
                hasTitle,
                status,
                hasStatus,
                hasDescription ? NormalizeDescription(description) : null,
                hasDescription));

            index++;
        }

        if (parsedTodos.Count == 0)
            return ParseResult.Invalid("todos must be a non-empty array.");

        return ParseResult.Valid(new ParsedInput(operation, parsedTodos));
    }

    private ParseResult ValidateAndParseInput(
        JsonElement input)
    {
        var parsed = ParseInputShape(input);
        if (!parsed.Validation.IsValid || parsed.Value == null)
            return parsed;

        foreach (var todo in parsed.Value.Todos.Select((item, index) => (item, index)))
        {
            switch (parsed.Value.Operation)
            {
                case TodoOperation.Create:
                    if (!todo.item.HasTitle || string.IsNullOrWhiteSpace(todo.item.Title))
                    {
                        return ParseResult.Invalid($"todos[{todo.index}].title is required for create.");
                    }

                    if (_runtime.GetTodo(todo.item.Id) != null)
                        return ParseResult.Invalid($"Todo '{todo.item.Id}' already exists.");

                    break;

                case TodoOperation.Update:
                    if (_runtime.GetTodo(todo.item.Id) == null)
                        return ParseResult.Invalid($"Todo '{todo.item.Id}' was not found.");

                    if (!todo.item.HasTitle &&
                        !todo.item.HasStatus &&
                        !todo.item.HasDescription)
                    {
                        return ParseResult.Invalid(
                            $"todos[{todo.index}] must include at least one field to update.");
                    }

                    if (todo.item.HasTitle && string.IsNullOrWhiteSpace(todo.item.Title))
                    {
                        return ParseResult.Invalid($"todos[{todo.index}].title must not be empty.");
                    }

                    break;

                case TodoOperation.Delete:
                    if (_runtime.GetTodo(todo.item.Id) == null)
                        return ParseResult.Invalid($"Todo '{todo.item.Id}' was not found.");

                    break;
            }
        }

        return parsed;
    }

    private static bool TryGetOptionalString(
        JsonElement item,
        string propertyName,
        out string? value,
        out string? error)
    {
        value = null;
        error = null;

        if (!item.TryGetProperty(propertyName, out var property))
            return true;

        if (property.ValueKind == JsonValueKind.Null)
            return true;

        if (property.ValueKind != JsonValueKind.String)
        {
            error = $"{propertyName} must be a string.";
            return false;
        }

        value = property.GetString();
        return true;
    }

    private static bool TryGetOperation(
        JsonElement input,
        out TodoOperation operation)
    {
        operation = default;
        if (!input.TryGetProperty("operation", out var operationElement) ||
            operationElement.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        switch (operationElement.GetString()?.Trim().ToLowerInvariant())
        {
            case "create":
                operation = TodoOperation.Create;
                return true;

            case "update":
                operation = TodoOperation.Update;
                return true;

            case "delete":
                operation = TodoOperation.Delete;
                return true;

            case "list":
                operation = TodoOperation.List;
                return true;

            default:
                return false;
        }
    }

    private static string BuildResult(
        string heading,
        IReadOnlyList<TodoItem> todos)
    {
        var builder = new StringBuilder();
        builder.AppendLine(heading);
        builder.AppendLine();
        builder.Append(RenderTodos(todos));
        return builder.ToString().TrimEnd();
    }

    private static string RenderTodos(
        IReadOnlyList<TodoItem> todos)
    {
        if (todos.Count == 0)
            return "(none)";

        var builder = new StringBuilder();
        foreach (var todo in todos)
        {
            builder.Append("- ")
                .Append(todo.Id)
                .Append(" [")
                .Append(TodoStatusNames.ToValue(todo.Status))
                .Append("] ")
                .AppendLine(todo.Title);

            if (!string.IsNullOrWhiteSpace(todo.Description))
            {
                builder.Append("  description: ")
                    .AppendLine(todo.Description);
            }
        }

        return builder.ToString().TrimEnd();
    }

    private static string? NormalizeDescription(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();

    private enum TodoOperation
    {
        Create,
        Update,
        Delete,
        List,
    }

    private sealed record ParsedInput(
        TodoOperation Operation,
        IReadOnlyList<ParsedTodo> Todos);

    private sealed record ParsedTodo(
        string Id,
        string? Title,
        bool HasTitle,
        TodoStatus? Status,
        bool HasStatus,
        string? Description,
        bool HasDescription);

    private sealed record ParseResult(
        ValidationResult Validation,
        ParsedInput? Value)
    {
        public static ParseResult Valid(ParsedInput value) =>
            new(ValidationResult.Valid(), value);

        public static ParseResult Invalid(string message) =>
            new(ValidationResult.Invalid(message), null);
    }
}
