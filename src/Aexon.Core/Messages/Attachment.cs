namespace Aexon.Core.Messages;

/// <summary>
/// Represents a registered attachment with stable identity and metadata.
/// </summary>
public sealed record Attachment
{
    public required string Id { get; init; }
    public required string FileName { get; init; }
    public required string MimeType { get; init; }
    public required long SizeBytes { get; init; }
    public required AttachmentSource Source { get; init; }
    public string? SourcePath { get; init; }
    public DateTimeOffset RegisteredAt { get; init; } = DateTimeOffset.UtcNow;

    public static string NewId() => Guid.NewGuid().ToString("N");
}

/// <summary>
/// Describes where an attachment originated.
/// </summary>
public enum AttachmentSource
{
    User,
    Tool,
    System,
}
