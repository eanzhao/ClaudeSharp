using ClaudeSharp.Core.Storage;
using ClaudeSharp.Core.Tests.Runtime;
using ClaudeSharp.Core.Tests.Storage;
using ClaudeSharp.Core.Todos;

namespace ClaudeSharp.Core.Tests.Todos;

/// <summary>
/// Contains tests for the persistent todo runtime.
/// </summary>
public sealed class PersistentTodoRuntimeTests
{
    [Fact]
    public async Task CreateAsync_PersistsAndRestoresTodos()
    {
        var journal = new RecordingJournal();
        var runtime = await PersistentTodoRuntime.CreateAsync(journal);

        runtime.CreateTodo("plan", "Plan work", TodoStatus.Pending, "Break the task down");
        runtime.UpdateTodo("plan", todo =>
        {
            todo.Status = TodoStatus.InProgress;
            todo.Description = "Implementing now";
        });
        runtime.CreateTodo("verify", "Verify result", TodoStatus.Pending);
        runtime.DeleteTodo("verify");

        Assert.Contains(journal.MetadataEntries, entry => entry.EventType == TodoPersistence.TodoEventType);
        Assert.Contains(journal.MetadataEntries, entry => entry.EventType == TodoPersistence.TodoDeletedEventType);

        var restored = await PersistentTodoRuntime.CreateAsync(
            new RecordingJournal(),
            journal.MetadataEntries);

        var todo = Assert.Single(restored.ListTodos());
        Assert.Equal("plan", todo.Id);
        Assert.Equal("Plan work", todo.Title);
        Assert.Equal(TodoStatus.InProgress, todo.Status);
        Assert.Equal("Implementing now", todo.Description);
    }

    [Fact]
    public async Task CreateAsync_RestoresTodosFromTranscriptStoreProjection()
    {
        using var temp = new TempDirectoryScope(nameof(CreateAsync_RestoresTodosFromTranscriptStoreProjection));
        var store = new JsonlTranscriptStore(temp.RootPath);
        var session = await store.CreateSessionAsync("/work/project", "claude-sonnet-4-6");
        var journal = new ConversationJournal(store, session);

        var runtime = await PersistentTodoRuntime.CreateAsync(journal);
        runtime.CreateTodo("issue-4", "Implement TodoWrite", TodoStatus.InProgress, "Wire runtime and prompt");
        runtime.CreateTodo("cleanup", "Clean up", TodoStatus.Pending);
        runtime.DeleteTodo("cleanup");

        var reloadedSession = await store.FindSessionAsync(session.SessionId);
        Assert.NotNull(reloadedSession);

        var projection = await store.LoadProjectionAsync(
            reloadedSession!,
            new TranscriptLoadOptions());
        var restored = await PersistentTodoRuntime.CreateAsync(
            new RecordingJournal(),
            projection.MetadataEntries);

        var todo = Assert.Single(restored.ListTodos());
        Assert.Equal("issue-4", todo.Id);
        Assert.Equal(TodoStatus.InProgress, todo.Status);
        Assert.Equal("Wire runtime and prompt", todo.Description);
    }
}
