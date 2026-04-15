using Aexon.Core.Agents;

namespace Aexon.Core.Tests.Agents;

/// <summary>
/// Contains tests for agent attention analysis.
/// </summary>
public sealed class AgentAttentionAnalyzerTests
{
    [Fact]
    public void ListAttentionItems_DescribesApprovalAndResumeActions()
    {
        var runtime = new InMemoryAgentTaskRuntime();
        var awaitingApproval = runtime.CreateWorkItem("Need approval", owner: "agent-a");
        runtime.UpdateWorkItem(awaitingApproval.Id, item =>
        {
            item.Status = AgentWorkItemStatus.AwaitingApproval;
            item.ApprovalRequestId = "agent-message-1";
        });
        var awaitingResume = runtime.CreateWorkItem("Need resume", owner: "agent-b");
        runtime.UpdateWorkItem(awaitingResume.Id, item =>
        {
            item.Status = AgentWorkItemStatus.AwaitingResume;
            item.Owner = "agent-b";
        });

        var items = AgentAttentionAnalyzer.ListAttentionItems(runtime);

        Assert.Equal(2, items.Count);
        Assert.Contains(items, item =>
            item.WorkItem.Id == awaitingApproval.Id &&
            item.Summary == "Waiting for approval response." &&
            item.NextAction.Contains("/mailbox respond agent-message-1 approve|reject", StringComparison.Ordinal));
        Assert.Contains(items, item =>
            item.WorkItem.Id == awaitingResume.Id &&
            item.Summary == "Approved and ready to resume." &&
            item.NextAction.Contains($"/agents resume {awaitingResume.Id}", StringComparison.Ordinal));
    }

    [Fact]
    public void ListAttentionItems_NotesWhenOwnerAlreadyHasActiveRun()
    {
        var runtime = new InMemoryAgentTaskRuntime();
        var awaitingResume = runtime.CreateWorkItem("Need resume", owner: "agent-b");
        runtime.UpdateWorkItem(awaitingResume.Id, item =>
        {
            item.Status = AgentWorkItemStatus.AwaitingResume;
            item.Owner = "agent-b";
        });
        runtime.StartBackgroundRun(
            "Current run",
            owner: "agent-b",
            workItemId: awaitingResume.Id,
            initialStatus: AgentBackgroundRunStatus.Running);

        var item = Assert.Single(AgentAttentionAnalyzer.ListAttentionItems(runtime));

        Assert.Equal(awaitingResume.Id, item.WorkItem.Id);
        Assert.Equal("background-run-1", item.ActiveBackgroundRunId);
        Assert.Contains("already busy with background-run-1", item.Summary, StringComparison.Ordinal);
        Assert.Contains("Wait for agent-b to finish background-run-1", item.NextAction, StringComparison.Ordinal);
    }
}
