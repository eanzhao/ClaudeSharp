using System.Text.Json;
using System.Text.Json.Serialization;
using Aexon.Core.Permissions;
using Aexon.Core.Tools;

namespace Aexon.Tools;

/// <summary>
/// Represents the input payload for the AskUserQuestion tool.
/// </summary>
public sealed class AskUserQuestionToolInput
{
    [JsonPropertyName("question")]
    public string Question { get; set; } = "";

    [JsonPropertyName("options")]
    public string[]? Options { get; set; }
}

/// <summary>
/// Prompts the user for clarification during an interactive session.
/// </summary>
public sealed class AskUserQuestionTool : ITool
{
    public string Name => "AskUserQuestion";

    public Task<string> GetDescriptionAsync() =>
        Task.FromResult(
            "Ask the user a clarifying question and wait for their answer before continuing.");

    public JsonElement GetInputSchema()
    {
        return JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "question": {
              "type": "string",
              "description": "The question to ask the user"
            },
            "options": {
              "type": "array",
              "items": {
                "type": "string"
              },
              "description": "Optional list of choices for the user"
            }
          },
          "required": ["question"],
          "additionalProperties": false
        }
        """).RootElement.Clone();
    }

    public Task<string> GetPromptAsync(ToolPromptContext context)
    {
        return Task.FromResult("""
            Ask the user a direct clarification question when you cannot proceed safely on your own.

            Usage:
            - Pass question with the exact thing you need from the user
            - Use options when the user can pick from a short list
            - Prefer this tool over guessing when the missing detail changes the implementation
            - Do not use this tool in non-interactive subagent or batch-only flows
            """);
    }

    public Task<ValidationResult> ValidateInputAsync(JsonElement input, ToolExecutionContext context)
    {
        try
        {
            var parsed = JsonSerializer.Deserialize<AskUserQuestionToolInput>(input);
            return Task.FromResult(GetValidationResult(parsed));
        }
        catch (JsonException ex)
        {
            return Task.FromResult(ValidationResult.Invalid(ex.Message));
        }
    }

    public Task<PermissionResult> CheckPermissionsAsync(JsonElement input, ToolExecutionContext context) =>
        Task.FromResult(PermissionResult.Allow());

    public bool IsReadOnly(JsonElement input) => true;

    public string GetUserFacingName(JsonElement? input = null)
    {
        if (input?.TryGetProperty("question", out var question) == true)
        {
            var text = question.GetString() ?? "";
            if (text.Length > 60)
                text = $"{text[..57]}...";
            return $"Ask user: {text}";
        }

        return "Ask user a question";
    }

    public string? GetActivityDescription(JsonElement? input) => "Waiting for user input";

    public async Task<ToolResult> ExecuteAsync(
        JsonElement input,
        ToolExecutionContext context,
        IProgress<ToolProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        AskUserQuestionToolInput? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<AskUserQuestionToolInput>(input);
        }
        catch (JsonException ex)
        {
            return ToolResult.Error(ex.Message);
        }

        var validation = GetValidationResult(parsed);
        if (!validation.IsValid)
            return ToolResult.Error(validation.Message ?? "Invalid input.");

        if (context.IsNonInteractiveSession || context.AskUserQuestionAsync == null)
            return ToolResult.Error("当前会话不是交互式模式，无法向用户提问。");

        var options = NormalizeOptions(parsed!.Options);
        var response = await context.AskUserQuestionAsync(
            new UserQuestionRequest(parsed.Question.Trim(), options),
            cancellationToken);

        var answer = response.Answer?.Trim();
        if (string.IsNullOrWhiteSpace(answer))
            return ToolResult.Error("用户没有提供有效回答。");

        return ToolResult.Success(answer);
    }

    private static ValidationResult GetValidationResult(AskUserQuestionToolInput? input)
    {
        if (input == null)
            return ValidationResult.Invalid("question is required.");

        if (string.IsNullOrWhiteSpace(input.Question))
            return ValidationResult.Invalid("question is required.");

        if (input.Options == null)
            return ValidationResult.Valid();

        if (input.Options.Length == 0)
            return ValidationResult.Invalid("options must contain at least one entry.");

        var normalized = NormalizeOptions(input.Options);
        if (normalized.Any(string.IsNullOrWhiteSpace))
            return ValidationResult.Invalid("options must not contain blank values.");

        if (normalized.Distinct(StringComparer.OrdinalIgnoreCase).Count() != normalized.Length)
            return ValidationResult.Invalid("options must be unique.");

        return ValidationResult.Valid();
    }

    private static string[] NormalizeOptions(IEnumerable<string>? options) =>
        options?
            .Select(option => option?.Trim() ?? string.Empty)
            .ToArray() ?? [];
}
