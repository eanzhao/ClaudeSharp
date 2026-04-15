using Aexon.Core.Messages;
using Aexon.Core.Storage;

namespace Aexon.Core.Tests.Storage;

public sealed class AttachmentPipelineTests
{
    [Fact]
    public void Registry_Register_AssignsStableId()
    {
        var registry = new AttachmentRegistry();
        var attachment = registry.Register("readme.md", "text/markdown", 1024, AttachmentSource.User);

        Assert.NotNull(attachment.Id);
        Assert.Equal("readme.md", attachment.FileName);
        Assert.Equal("text/markdown", attachment.MimeType);
        Assert.Equal(1024, attachment.SizeBytes);
        Assert.Equal(AttachmentSource.User, attachment.Source);
    }

    [Fact]
    public void Registry_Get_ReturnsRegisteredAttachment()
    {
        var registry = new AttachmentRegistry();
        var attachment = registry.Register("file.txt", "text/plain", 512, AttachmentSource.Tool, "/tmp/file.txt");

        var retrieved = registry.Get(attachment.Id);

        Assert.NotNull(retrieved);
        Assert.Equal(attachment.Id, retrieved!.Id);
        Assert.Equal("/tmp/file.txt", retrieved.SourcePath);
    }

    [Fact]
    public void Registry_Get_ReturnsNullForUnknownId()
    {
        var registry = new AttachmentRegistry();
        Assert.Null(registry.Get("nonexistent"));
    }

    [Fact]
    public void Registry_Remove_ReturnsTrueAndRemovesEntry()
    {
        var registry = new AttachmentRegistry();
        var attachment = registry.Register("file.txt", "text/plain", 100, AttachmentSource.User);

        Assert.True(registry.Remove(attachment.Id));
        Assert.Null(registry.Get(attachment.Id));
    }

    [Fact]
    public void Registry_Remove_ReturnsFalseForUnknownId()
    {
        var registry = new AttachmentRegistry();
        Assert.False(registry.Remove("nonexistent"));
    }

    [Fact]
    public void Registry_GetAll_ReturnsAllInOrder()
    {
        var registry = new AttachmentRegistry();
        var a1 = registry.Register("first.txt", "text/plain", 10, AttachmentSource.User);
        var a2 = registry.Register("second.txt", "text/plain", 20, AttachmentSource.Tool);

        var all = registry.GetAll();
        Assert.Equal(2, all.Count);
        Assert.Equal(a1.Id, all[0].Id);
        Assert.Equal(a2.Id, all[1].Id);
    }

    [Fact]
    public void Registry_RegisterExisting_OverwritesPrevious()
    {
        var registry = new AttachmentRegistry();
        var original = new Attachment
        {
            Id = "fixed-id",
            FileName = "old.txt",
            MimeType = "text/plain",
            SizeBytes = 100,
            Source = AttachmentSource.User,
        };
        registry.Register(original);

        var updated = original with { FileName = "new.txt" };
        registry.Register(updated);

        var retrieved = registry.Get("fixed-id");
        Assert.Equal("new.txt", retrieved!.FileName);
        Assert.Single(registry.GetAll());
    }

    [Fact]
    public void AttachmentBlock_SerializesAsContentBlock()
    {
        var block = new AttachmentBlock
        {
            AttachmentId = "abc123",
            FileName = "photo.png",
            MimeType = "image/png",
            SizeBytes = 2048,
        };

        Assert.Equal("attachment", block.Type);
        Assert.Equal("abc123", block.AttachmentId);
    }

    [Fact]
    public async Task Transcript_RoundTrips_AttachmentBlock()
    {
        using var temp = new TempDirectoryScope(nameof(Transcript_RoundTrips_AttachmentBlock));
        var store = new JsonlTranscriptStore(temp.RootPath);
        var session = await store.CreateSessionAsync("/work", "sonnet");
        var journal = new ConversationJournal(store, session);

        var userMsg = new UserMessage
        {
            Id = "user-attach-1",
            Content =
            [
                new TextBlock("Here is an attachment"),
                new AttachmentBlock
                {
                    AttachmentId = "att-001",
                    FileName = "doc.pdf",
                    MimeType = "application/pdf",
                    SizeBytes = 4096,
                },
            ],
        };

        await journal.AppendMessageAsync(userMsg, "/work", "sonnet");

        var reloaded = await store.FindSessionAsync(session.SessionDirectory);
        Assert.NotNull(reloaded);

        var projection = await store.LoadProjectionAsync(reloaded!, new TranscriptLoadOptions());
        var storedMsg = projection.MessagesById["user-attach-1"];
        var restored = Assert.IsType<UserMessage>(storedMsg.Message);

        Assert.Equal(2, restored.Content.Count);
        var attachBlock = Assert.IsType<AttachmentBlock>(restored.Content[1]);
        Assert.Equal("att-001", attachBlock.AttachmentId);
        Assert.Equal("doc.pdf", attachBlock.FileName);
        Assert.Equal("application/pdf", attachBlock.MimeType);
        Assert.Equal(4096, attachBlock.SizeBytes);
    }

    [Fact]
    public async Task Metadata_RoundTrips_AttachmentAddRemove()
    {
        using var temp = new TempDirectoryScope(nameof(Metadata_RoundTrips_AttachmentAddRemove));
        var store = new JsonlTranscriptStore(temp.RootPath);
        var session = await store.CreateSessionAsync("/work", "sonnet");
        var journal = new ConversationJournal(store, session);

        var attachment = new Attachment
        {
            Id = "att-meta-001",
            FileName = "report.csv",
            MimeType = "text/csv",
            SizeBytes = 8192,
            Source = AttachmentSource.Tool,
            SourcePath = "/tmp/report.csv",
        };

        await journal.UpdateMetadataAsync(metadata =>
        {
            metadata.Attachments[attachment.Id] = attachment;
        });

        var reloaded = await store.FindSessionAsync(session.SessionDirectory);
        var projection = await store.LoadProjectionAsync(reloaded!, new TranscriptLoadOptions());

        Assert.True(projection.Session.Metadata.Attachments.ContainsKey("att-meta-001"));
        var restored = projection.Session.Metadata.Attachments["att-meta-001"];
        Assert.Equal("report.csv", restored.FileName);
        Assert.Equal("text/csv", restored.MimeType);
        Assert.Equal(8192, restored.SizeBytes);
        Assert.Equal(AttachmentSource.Tool, restored.Source);

        await journal.UpdateMetadataAsync(metadata =>
        {
            metadata.Attachments.Remove("att-meta-001");
        });

        var reloaded2 = await store.FindSessionAsync(session.SessionDirectory);
        var projection2 = await store.LoadProjectionAsync(reloaded2!, new TranscriptLoadOptions());
        Assert.Empty(projection2.Session.Metadata.Attachments);
    }

    [Fact]
    public void SessionMetadata_Clone_CopiesAttachments()
    {
        var metadata = new ConversationSessionMetadata();
        metadata.Attachments["id-1"] = new Attachment
        {
            Id = "id-1",
            FileName = "test.txt",
            MimeType = "text/plain",
            SizeBytes = 100,
            Source = AttachmentSource.System,
        };

        var clone = metadata.Clone();

        Assert.Single(clone.Attachments);
        Assert.Equal("test.txt", clone.Attachments["id-1"].FileName);

        clone.Attachments.Remove("id-1");
        Assert.Single(metadata.Attachments);
    }
}
