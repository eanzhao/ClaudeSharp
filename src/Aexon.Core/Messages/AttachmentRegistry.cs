using System.Collections.Concurrent;

namespace Aexon.Core.Messages;

/// <summary>
/// Manages session-scoped attachment registration and lookup.
/// </summary>
public interface IAttachmentRegistry
{
    Attachment Register(string fileName, string mimeType, long sizeBytes, AttachmentSource source, string? sourcePath = null);
    Attachment Register(Attachment attachment);
    Attachment? Get(string attachmentId);
    bool Remove(string attachmentId);
    IReadOnlyList<Attachment> GetAll();
}

/// <summary>
/// Thread-safe, in-memory attachment registry scoped to a single session.
/// </summary>
public sealed class AttachmentRegistry : IAttachmentRegistry
{
    private readonly ConcurrentDictionary<string, Attachment> _attachments = new(StringComparer.Ordinal);

    public Attachment Register(string fileName, string mimeType, long sizeBytes, AttachmentSource source, string? sourcePath = null)
    {
        var attachment = new Attachment
        {
            Id = Attachment.NewId(),
            FileName = fileName,
            MimeType = mimeType,
            SizeBytes = sizeBytes,
            Source = source,
            SourcePath = sourcePath,
        };

        return Register(attachment);
    }

    public Attachment Register(Attachment attachment)
    {
        _attachments[attachment.Id] = attachment;
        return attachment;
    }

    public Attachment? Get(string attachmentId)
    {
        return _attachments.TryGetValue(attachmentId, out var attachment) ? attachment : null;
    }

    public bool Remove(string attachmentId)
    {
        return _attachments.TryRemove(attachmentId, out _);
    }

    public IReadOnlyList<Attachment> GetAll()
    {
        return _attachments.Values.OrderBy(a => a.RegisteredAt).ToList();
    }
}
