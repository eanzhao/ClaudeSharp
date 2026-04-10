using System.Text.Json;
using ClaudeSharp.Core.Agents;
using ClaudeSharp.Core.Hooks;
using ClaudeSharp.Core.Messages;
using ClaudeSharp.Core.Permissions;
using ClaudeSharp.Core.Providers;
using ClaudeSharp.Core.Query;
using ClaudeSharp.Core.Tests.Runtime;
using ClaudeSharp.Core.Tools;
using ClaudeSharp.Tools;

namespace ClaudeSharp.Core.Tests.Agents;

/// <summary>
/// Contains tests for the Agent tool.
/// </summary>
public sealed class AgentToolTests
{
    [Fact]
    public async Task ExecuteAsync_RunsReadOnlySubagentAndMarksWorkItemCompleted()
    {
        var runtime = new InMemoryAgentTaskRuntime();
        var runner = new RecordingRunner(new AgentExecutionResult(
            Summary: "child summary",
            Success: true,
            Usage: new TokenUsage
            {
                InputTokens = 5,
                OutputTokens = 7,
            },
            TurnCount: 2));

        var tool = new AgentTool(
            runner,
            new DefaultProviderCapabilityRouter(),
            runtime,
            hooks: HookRuntime.Empty);

        var result = await tool.ExecuteAsync(
            JsonSerializer.SerializeToElement(new
            {
                prompt = "Inspect the query pipeline",
                subagent_type = "research",
            }),
            CreateContext());

        Assert.False(result.IsError);
        Assert.Contains("Subagent work-item-1 completed.", result.Data, StringComparison.Ordinal);
        Assert.Contains("child summary", result.Data, StringComparison.Ordinal);
        Assert.Contains("Turns: 2", result.Data, StringComparison.Ordinal);
        Assert.Contains("Usage: in=5, out=7", result.Data, StringComparison.Ordinal);

        var request = Assert.Single(runner.Requests);
        Assert.Equal("Inspect the query pipeline", request.Prompt);
        Assert.Equal("claude-sonnet-4-6", request.Model);
        Assert.True(request.UseIsolatedWorkspace);
        Assert.Equal(
            ["Glob", "Grep", "Read", "WebFetch", "WebSearch"],
            request.Tools.GetAllTools().Select(tool => tool.Name).OrderBy(name => name, StringComparer.Ordinal));
        Assert.Contains("Subagent type hint: research", request.SystemPromptAppendix, StringComparison.Ordinal);

        var workItem = Assert.Single(runtime.ListWorkItems());
        Assert.Equal(AgentWorkItemStatus.Completed, workItem.Status);
        Assert.Equal("Inspect the query pipeline", workItem.Title);
    }

    [Fact]
    public async Task ExecuteAsync_CanRunAsNamedTeammate()
    {
        var taskRuntime = new InMemoryAgentTaskRuntime();
        var teamRuntime = new InMemoryAgentTeamRuntime();
        var messageRuntime = new InMemoryAgentMessageRuntime();
        var team = teamRuntime.CreateTeam("Platform", leadName: "Ada");
        teamRuntime.AddMember(team.Id, "Bob");
        var unread = messageRuntime.SendMessage("main", "Platform/Ada", "Please inspect the team runtime");

        var runner = new RecordingRunner(new AgentExecutionResult(
            Summary: "teammate summary",
            Success: true,
            Usage: TokenUsage.Empty,
            TurnCount: 1));

        var tool = new AgentTool(
            runner,
            new DefaultProviderCapabilityRouter(),
            taskRuntime,
            teamRuntime,
            messageRuntime,
            hooks: HookRuntime.Empty);

        var result = await tool.ExecuteAsync(
            JsonSerializer.SerializeToElement(new
            {
                prompt = "Inspect the team runtime",
                teammate = new
                {
                    team_name = "Platform",
                    member_name = "Ada",
                },
            }),
            CreateContext());

        Assert.False(result.IsError);
        Assert.Contains("Teammate Ada work-item-1 completed.", result.Data, StringComparison.Ordinal);

        var request = Assert.Single(runner.Requests);
        Assert.Contains("Team assignment:", request.SystemPromptAppendix, StringComparison.Ordinal);
        Assert.Contains("Platform", request.SystemPromptAppendix, StringComparison.Ordinal);
        Assert.Contains("Ada (Lead)", request.SystemPromptAppendix, StringComparison.Ordinal);
        Assert.Contains("Unread mailbox messages:", request.SystemPromptAppendix, StringComparison.Ordinal);
        Assert.Contains(unread.Id, request.SystemPromptAppendix, StringComparison.Ordinal);
        Assert.Contains(
            request.Tools.GetAllTools().Select(tool => tool.Name),
            name => string.Equals(name, "TeamStatus", StringComparison.Ordinal));
        Assert.Contains(
            request.Tools.GetAllTools().Select(tool => tool.Name),
            name => string.Equals(name, "SendMessage", StringComparison.Ordinal));
        Assert.Contains(
            request.Tools.GetAllTools().Select(tool => tool.Name),
            name => string.Equals(name, "MailboxStatus", StringComparison.Ordinal));
        Assert.Contains(
            request.Tools.GetAllTools().Select(tool => tool.Name),
            name => string.Equals(name, "MailboxRespond", StringComparison.Ordinal));

        var workItem = Assert.Single(taskRuntime.ListWorkItems());
        Assert.Equal("Platform/Ada", workItem.Owner);
        Assert.Equal(AgentMessageStatus.Read, messageRuntime.GetMessage(unread.Id)?.Status);
    }

    [Fact]
    public async Task ExecuteAsync_BackgroundTeammateCanBeReactivatedFromMailboxMessage()
    {
        var taskRuntime = new InMemoryAgentTaskRuntime();
        var teamRuntime = new InMemoryAgentTeamRuntime();
        var messageRuntime = new InMemoryAgentMessageRuntime();
        var activationRuntime = new InMemoryAgentMessageActivationRuntime();
        var team = teamRuntime.CreateTeam("Platform", leadName: "Ada");
        teamRuntime.AddMember(team.Id, "Bob");
        var runner = new AsyncRecordingRunner(async (_, cancellationToken) =>
        {
            await Task.Delay(10, cancellationToken);
            return new AgentExecutionResult(
                Summary: "teammate summary",
                Success: true,
                Usage: TokenUsage.Empty,
                TurnCount: 1);
        });

        var agentTool = new AgentTool(
            runner,
            new DefaultProviderCapabilityRouter(),
            taskRuntime,
            teamRuntime,
            messageRuntime,
            hooks: HookRuntime.Empty,
            messageActivationRuntime: activationRuntime);

        var launch = await agentTool.ExecuteAsync(
            JsonSerializer.SerializeToElement(new
            {
                prompt = "Inspect the teammate runtime",
                run_in_background = true,
                teammate = new
                {
                    team_name = "Platform",
                    member_name = "Ada",
                },
            }),
            CreateContext());

        Assert.False(launch.IsError);
        await WaitForAsync(() =>
            taskRuntime.GetBackgroundRun("background-run-1")?.Status == AgentBackgroundRunStatus.Stopped);

        var sendTool = new SendMessageTool(messageRuntime, teamRuntime, activationRuntime);
        var send = await sendTool.ExecuteAsync(
            JsonSerializer.SerializeToElement(new
            {
                request = new
                {
                    to = "Ada",
                    team_name = "Platform",
                    from = "main",
                    message = new
                    {
                        kind = "Note",
                        body = "Please pick this back up",
                        action = "resume-review",
                        requires_response = true,
                        resume_reason = "The review thread has new work",
                    },
                },
            }),
            CreateContext());

        Assert.False(send.IsError);
        Assert.Contains("Reactivated Platform/Ada as background-run-2", send.Data, StringComparison.Ordinal);
        Assert.Contains("Triggered by agent-message-1 in thread-1", send.Data, StringComparison.Ordinal);

        await WaitForAsync(() =>
            taskRuntime.GetBackgroundRun("background-run-2")?.Status == AgentBackgroundRunStatus.Stopped);

        Assert.Equal(2, runner.Requests.Count);
        Assert.All(runner.Requests, request =>
            Assert.Equal("Inspect the teammate runtime", request.Prompt));
        Assert.Contains("Resume trigger:", runner.Requests[1].SystemPromptAppendix, StringComparison.Ordinal);
        Assert.Contains("Action: resume-review", runner.Requests[1].SystemPromptAppendix, StringComparison.Ordinal);
        Assert.Contains("Resume reason: The review thread has new work", runner.Requests[1].SystemPromptAppendix, StringComparison.Ordinal);
        Assert.Contains("Unread mailbox messages:", runner.Requests[1].SystemPromptAppendix, StringComparison.Ordinal);
        Assert.Contains("Please pick this back up", runner.Requests[1].SystemPromptAppendix, StringComparison.Ordinal);
        Assert.Equal(["work-item-1"], taskRuntime.ListWorkItems().Select(item => item.Id));
        Assert.Equal(AgentWorkItemStatus.Completed, taskRuntime.GetWorkItem("work-item-1")?.Status);
        var reactivatedRun = taskRuntime.GetBackgroundRun("background-run-2");
        Assert.NotNull(reactivatedRun);
        Assert.Equal("work-item-1", reactivatedRun!.WorkItemId);
        Assert.Contains(reactivatedRun!.Output, chunk =>
            chunk.Contains("[resume] Triggered by agent-message-1 in thread-1 from main.", StringComparison.Ordinal));
        Assert.Contains(reactivatedRun.Output, chunk =>
            chunk.Contains("action=resume-review", StringComparison.Ordinal));
        Assert.Equal(AgentMessageStatus.Read, messageRuntime.GetMessage("agent-message-1")?.Status);
    }

    [Fact]
    public async Task ExecuteAsync_ApprovalResponseReusesOriginalBackgroundWorkItem()
    {
        var taskRuntime = new InMemoryAgentTaskRuntime();
        var teamRuntime = new InMemoryAgentTeamRuntime();
        var messageRuntime = new InMemoryAgentMessageRuntime();
        var activationRuntime = new InMemoryAgentMessageActivationRuntime();
        var team = teamRuntime.CreateTeam("Platform", leadName: "Ada");
        teamRuntime.AddMember(team.Id, "Bob");
        var runner = new AsyncRecordingRunner(async (_, cancellationToken) =>
        {
            await Task.Delay(10, cancellationToken);
            return new AgentExecutionResult(
                Summary: "teammate summary",
                Success: true,
                Usage: TokenUsage.Empty,
                TurnCount: 1);
        });

        var agentTool = new AgentTool(
            runner,
            new DefaultProviderCapabilityRouter(),
            taskRuntime,
            teamRuntime,
            messageRuntime,
            hooks: HookRuntime.Empty,
            messageActivationRuntime: activationRuntime);

        var launch = await agentTool.ExecuteAsync(
            JsonSerializer.SerializeToElement(new
            {
                prompt = "Inspect the teammate runtime",
                run_in_background = true,
                teammate = new
                {
                    team_name = "Platform",
                    member_name = "Bob",
                },
            }),
            CreateContext());

        Assert.False(launch.IsError);
        await WaitForAsync(() =>
            taskRuntime.GetBackgroundRun("background-run-1")?.Status == AgentBackgroundRunStatus.Stopped);

        var sendTool = new SendMessageTool(messageRuntime, teamRuntime, activationRuntime, taskRuntime);
        var request = await sendTool.ExecuteAsync(
            JsonSerializer.SerializeToElement(new
            {
                request = new
                {
                    to = "Ada",
                    from = "Bob",
                    team_name = "Platform",
                    message = new
                    {
                        kind = "PlanApprovalRequest",
                        body = "Approve this runtime plan",
                        subject = "Runtime plan",
                    },
                },
            }),
            CreateContext());

        Assert.False(request.IsError);
        Assert.Equal(2, taskRuntime.ListWorkItems().Count);
        Assert.Equal(
            AgentWorkItemStatus.Pending,
            taskRuntime.ListWorkItems().Single(item => item.SourceKind == AgentWorkItemSourceKinds.MailboxPlanApproval).Status);

        var approvalRequest = messageRuntime.ListMessages().Single(message => message.Kind == AgentMessageKind.PlanApprovalRequest);
        var respondTool = new MailboxRespondTool(messageRuntime, activationRuntime, taskRuntime);
        var response = await respondTool.ExecuteAsync(
            JsonSerializer.SerializeToElement(new
            {
                request = new
                {
                    message_id = approvalRequest.Id,
                    decision = "approve",
                    responder = "Platform/Ada",
                    note = "Approved. Please continue.",
                },
            }),
            CreateContext());

        Assert.False(response.IsError);
        Assert.Contains("Reactivated Platform/Bob as background-run-2", response.Data, StringComparison.Ordinal);

        await WaitForAsync(() =>
            taskRuntime.GetBackgroundRun("background-run-2")?.Status == AgentBackgroundRunStatus.Stopped);

        Assert.Equal(["work-item-1", "work-item-2"], taskRuntime.ListWorkItems().Select(item => item.Id));
        Assert.Equal(AgentWorkItemStatus.Completed, taskRuntime.GetWorkItem("work-item-1")?.Status);
        Assert.Equal(AgentWorkItemStatus.Completed, taskRuntime.GetWorkItem("work-item-2")?.Status);
        Assert.Equal("work-item-1", taskRuntime.GetBackgroundRun("background-run-2")?.WorkItemId);
    }

    [Fact]
    public async Task ExecuteAsync_BackgroundPlanApprovalRequestLeavesOriginalWorkItemAwaitingApproval()
    {
        var taskRuntime = new InMemoryAgentTaskRuntime();
        var messageRuntime = new InMemoryAgentMessageRuntime();
        var runner = new AsyncRecordingRunner(async (request, cancellationToken) =>
        {
            var sendTool = request.Tools.Get("SendMessage");
            Assert.NotNull(sendTool);

            var sendResult = await sendTool!.ExecuteAsync(
                JsonSerializer.SerializeToElement(new
                {
                    request = new
                    {
                        to = "main",
                        from = "subagent",
                        message = new
                        {
                            kind = "PlanApprovalRequest",
                            body = "Approve this runtime plan",
                            subject = "Runtime plan",
                        },
                    },
                }),
                CreateToolContext(request, cancellationToken),
                cancellationToken: cancellationToken);

            Assert.False(sendResult.IsError);
            return new AgentExecutionResult(
                Summary: "waiting for approval",
                Success: true,
                Usage: TokenUsage.Empty,
                TurnCount: 1);
        });

        var agentTool = new AgentTool(
            runner,
            new DefaultProviderCapabilityRouter(),
            taskRuntime,
            messageRuntime: messageRuntime,
            hooks: HookRuntime.Empty);

        var launch = await agentTool.ExecuteAsync(
            JsonSerializer.SerializeToElement(new
            {
                prompt = "Inspect the runtime",
                run_in_background = true,
            }),
            CreateContext());

        Assert.False(launch.IsError);
        await WaitForAsync(() =>
            taskRuntime.GetBackgroundRun("background-run-1")?.Status == AgentBackgroundRunStatus.Stopped);

        var original = taskRuntime.GetWorkItem("work-item-1");
        Assert.NotNull(original);
        Assert.Equal(AgentWorkItemStatus.AwaitingApproval, original!.Status);
        Assert.Equal("agent-message-1", original.ApprovalRequestId);
        Assert.Equal("thread-1", original.ApprovalThreadId);
        Assert.Equal(AgentWorkItemStatus.Pending, taskRuntime.GetWorkItem("work-item-2")?.Status);
    }

    [Fact]
    public async Task ExecuteAsync_ApprovedPlanResponseResumesOriginalWorkItemAndCompletesIt()
    {
        var taskRuntime = new InMemoryAgentTaskRuntime();
        var messageRuntime = new InMemoryAgentMessageRuntime();
        var activationRuntime = new InMemoryAgentMessageActivationRuntime();
        var runner = new AsyncRecordingRunner(async (request, cancellationToken) =>
        {
            if (request.Tools.Get("SendMessage") is { } sendTool &&
                request.Prompt.Contains("Inspect the runtime", StringComparison.Ordinal) &&
                request.SystemPromptAppendix?.Contains("Resume trigger:", StringComparison.Ordinal) != true)
            {
                var sendResult = await sendTool.ExecuteAsync(
                    JsonSerializer.SerializeToElement(new
                    {
                        request = new
                        {
                            to = "main",
                            from = "subagent",
                            message = new
                            {
                                kind = "PlanApprovalRequest",
                                body = "Approve this runtime plan",
                                subject = "Runtime plan",
                            },
                        },
                    }),
                    CreateToolContext(request, cancellationToken),
                    cancellationToken: cancellationToken);

                Assert.False(sendResult.IsError);
                return new AgentExecutionResult(
                    Summary: "waiting for approval",
                    Success: true,
                    Usage: TokenUsage.Empty,
                    TurnCount: 1);
            }

            return new AgentExecutionResult(
                Summary: "continued after approval",
                Success: true,
                Usage: TokenUsage.Empty,
                TurnCount: 1);
        });

        var agentTool = new AgentTool(
            runner,
            new DefaultProviderCapabilityRouter(),
            taskRuntime,
            messageRuntime: messageRuntime,
            hooks: HookRuntime.Empty,
            messageActivationRuntime: activationRuntime);

        var launch = await agentTool.ExecuteAsync(
            JsonSerializer.SerializeToElement(new
            {
                prompt = "Inspect the runtime",
                run_in_background = true,
            }),
            CreateContext());

        Assert.False(launch.IsError);
        await WaitForAsync(() =>
            taskRuntime.GetBackgroundRun("background-run-1")?.Status == AgentBackgroundRunStatus.Stopped);
        Assert.Equal(AgentWorkItemStatus.AwaitingApproval, taskRuntime.GetWorkItem("work-item-1")?.Status);

        var approvalRequest = Assert.Single(
            messageRuntime.ListMessages(),
            message => message.Kind == AgentMessageKind.PlanApprovalRequest);
        var respondTool = new MailboxRespondTool(messageRuntime, activationRuntime, taskRuntime);
        var response = await respondTool.ExecuteAsync(
            JsonSerializer.SerializeToElement(new
            {
                request = new
                {
                    message_id = approvalRequest.Id,
                    decision = "approve",
                    responder = "main",
                    note = "Approved. Continue.",
                },
            }),
            CreateContext());

        Assert.False(response.IsError);
        Assert.Contains("Reactivated subagent as background-run-2", response.Data, StringComparison.Ordinal);

        await WaitForAsync(() =>
            taskRuntime.GetBackgroundRun("background-run-2")?.Status == AgentBackgroundRunStatus.Stopped);

        var original = taskRuntime.GetWorkItem("work-item-1");
        Assert.NotNull(original);
        Assert.Equal(AgentWorkItemStatus.Completed, original!.Status);
        Assert.Null(original.ApprovalRequestId);
        Assert.Null(original.ApprovalThreadId);
        Assert.Equal("work-item-1", taskRuntime.GetBackgroundRun("background-run-2")?.WorkItemId);
        Assert.Equal(2, runner.Requests.Count);
        Assert.Contains("Resume trigger:", runner.Requests[1].SystemPromptAppendix, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_ApprovedPlanResponseWithoutActivationLeavesOriginalWorkItemAwaitingResume()
    {
        var taskRuntime = new InMemoryAgentTaskRuntime();
        var messageRuntime = new InMemoryAgentMessageRuntime();
        var runner = new AsyncRecordingRunner(async (request, cancellationToken) =>
        {
            if (request.Tools.Get("SendMessage") is { } sendTool)
            {
                var sendResult = await sendTool.ExecuteAsync(
                    JsonSerializer.SerializeToElement(new
                    {
                        request = new
                        {
                            to = "main",
                            from = "subagent",
                            message = new
                            {
                                kind = "PlanApprovalRequest",
                                body = "Approve this runtime plan",
                                subject = "Runtime plan",
                            },
                        },
                    }),
                    CreateToolContext(request, cancellationToken),
                    cancellationToken: cancellationToken);

                Assert.False(sendResult.IsError);
            }

            return new AgentExecutionResult(
                Summary: "waiting for approval",
                Success: true,
                Usage: TokenUsage.Empty,
                TurnCount: 1);
        });

        var agentTool = new AgentTool(
            runner,
            new DefaultProviderCapabilityRouter(),
            taskRuntime,
            messageRuntime: messageRuntime,
            hooks: HookRuntime.Empty);

        var launch = await agentTool.ExecuteAsync(
            JsonSerializer.SerializeToElement(new
            {
                prompt = "Inspect the runtime",
                run_in_background = true,
            }),
            CreateContext());

        Assert.False(launch.IsError);
        await WaitForAsync(() =>
            taskRuntime.GetBackgroundRun("background-run-1")?.Status == AgentBackgroundRunStatus.Stopped);

        var approvalRequest = Assert.Single(
            messageRuntime.ListMessages(),
            message => message.Kind == AgentMessageKind.PlanApprovalRequest);
        var respondTool = new MailboxRespondTool(messageRuntime, taskRuntime: taskRuntime);
        var response = await respondTool.ExecuteAsync(
            JsonSerializer.SerializeToElement(new
            {
                request = new
                {
                    message_id = approvalRequest.Id,
                    decision = "approve",
                    responder = "main",
                    note = "Approved. Continue.",
                },
            }),
            CreateContext());

        Assert.False(response.IsError);
        Assert.Contains("Original work item: work-item-1 [AwaitingResume]", response.Data, StringComparison.Ordinal);
        var original = taskRuntime.GetWorkItem("work-item-1");
        Assert.NotNull(original);
        Assert.Equal(AgentWorkItemStatus.AwaitingResume, original!.Status);
        Assert.Single(runner.Requests);
    }

    [Fact]
    public async Task ExecuteAsync_ReportsRunnerFailuresAndMarksWorkItemBlocked()
    {
        var runtime = new InMemoryAgentTaskRuntime();
        var runner = new RecordingRunner(new AgentExecutionResult(
            Summary: string.Empty,
            Success: false,
            Usage: TokenUsage.Empty,
            TurnCount: 1,
            ErrorMessage: "child failed"));

        var tool = new AgentTool(
            runner,
            new DefaultProviderCapabilityRouter(),
            runtime,
            hooks: HookRuntime.Empty);

        var result = await tool.ExecuteAsync(
            JsonSerializer.SerializeToElement(new { prompt = "Trace the bug" }),
            CreateContext());

        Assert.True(result.IsError);
        Assert.Contains("child failed", result.Data, StringComparison.Ordinal);
        Assert.Equal(AgentWorkItemStatus.Blocked, Assert.Single(runtime.ListWorkItems()).Status);
    }

    [Fact]
    public async Task ExecuteAsync_CanLaunchBackgroundSubagent()
    {
        var runtime = new InMemoryAgentTaskRuntime();
        var runner = new AsyncRecordingRunner(async (_, cancellationToken) =>
        {
            await Task.Delay(25, cancellationToken);
            return new AgentExecutionResult(
                Summary: "background summary",
                Success: true,
                Usage: new TokenUsage
                {
                    InputTokens = 11,
                    OutputTokens = 13,
                },
                TurnCount: 3);
        });

        var tool = new AgentTool(
            runner,
            new DefaultProviderCapabilityRouter(),
            runtime,
            hooks: HookRuntime.Empty);

        var result = await tool.ExecuteAsync(
            JsonSerializer.SerializeToElement(new
            {
                prompt = "Inspect the runtime",
                run_in_background = true,
            }),
            CreateContext());

        Assert.False(result.IsError);
        Assert.Contains("background-run-1", result.Data, StringComparison.Ordinal);

        await WaitForAsync(() =>
            runtime.GetBackgroundRun("background-run-1")?.Status == AgentBackgroundRunStatus.Stopped);

        var workItem = Assert.Single(runtime.ListWorkItems());
        Assert.Equal(AgentWorkItemStatus.Completed, workItem.Status);

        var backgroundRun = Assert.Single(runtime.ListBackgroundRuns());
        Assert.Equal(AgentBackgroundRunStatus.Stopped, backgroundRun.Status);
        Assert.Contains(backgroundRun.Output, chunk =>
            chunk.Contains("background summary", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_BackgroundSubagentStreamsProgressToRuntime()
    {
        var runtime = new InMemoryAgentTaskRuntime();
        var runner = new AsyncRecordingRunner(async (request, cancellationToken) =>
        {
            request.Progress?.Report(new AgentExecutionProgress("status", "Child agent started"));
            request.Progress?.Report(new AgentExecutionProgress("text", "First line"));
            request.Progress?.Report(new AgentExecutionProgress("text", "\nSecond line"));
            request.Progress?.Report(new AgentExecutionProgress(
                "tool_start",
                "{\"file_path\":\"/tmp/example.txt\"}",
                ToolName: "Read",
                ToolUseId: "tool-1"));
            request.Progress?.Report(new AgentExecutionProgress(
                "tool_result",
                string.Empty,
                ToolName: "Read",
                ToolUseId: "tool-1"));
            await Task.Delay(25, cancellationToken);
            return new AgentExecutionResult(
                Summary: "background summary",
                Success: true,
                Usage: TokenUsage.Empty,
                TurnCount: 2);
        });

        var tool = new AgentTool(
            runner,
            new DefaultProviderCapabilityRouter(),
            runtime,
            hooks: HookRuntime.Empty);

        var result = await tool.ExecuteAsync(
            JsonSerializer.SerializeToElement(new
            {
                prompt = "Inspect the runtime",
                run_in_background = true,
            }),
            CreateContext());

        Assert.False(result.IsError);

        await WaitForAsync(() =>
            runtime.GetBackgroundRun("background-run-1")?.Status == AgentBackgroundRunStatus.Stopped);

        var backgroundRun = Assert.Single(runtime.ListBackgroundRuns());
        Assert.Contains(backgroundRun.Output, chunk =>
            chunk.Contains("[status] Child agent started", StringComparison.Ordinal));
        Assert.Contains(backgroundRun.Output, chunk =>
            chunk.Contains("First line", StringComparison.Ordinal));
        Assert.Contains(backgroundRun.Output, chunk =>
            chunk.Contains("Second line", StringComparison.Ordinal));
        Assert.Contains(backgroundRun.Output, chunk =>
            chunk.Contains("[Read] {\"file_path\":\"/tmp/example.txt\"}", StringComparison.Ordinal));
        Assert.Contains(backgroundRun.Output, chunk =>
            chunk.Contains("[Read] done", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_BackgroundSubagentCanBeCancelled()
    {
        var runtime = new InMemoryAgentTaskRuntime();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runner = new AsyncRecordingRunner(async (_, cancellationToken) =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new AgentExecutionResult(
                Summary: string.Empty,
                Success: true,
                Usage: TokenUsage.Empty,
                TurnCount: 1);
        });

        var tool = new AgentTool(
            runner,
            new DefaultProviderCapabilityRouter(),
            runtime,
            hooks: HookRuntime.Empty);

        var launch = await tool.ExecuteAsync(
            JsonSerializer.SerializeToElement(new
            {
                prompt = "Inspect the runtime",
                run_in_background = true,
            }),
            CreateContext());
        Assert.False(launch.IsError);

        await started.Task;

        var stopTool = new AgentStopTool(runtime);
        var stop = await stopTool.ExecuteAsync(
            JsonSerializer.SerializeToElement(new
            {
                id = "background-run-1",
                reason = "user requested",
            }),
            CreateContext());

        Assert.False(stop.IsError);
        Assert.Contains("Cancellation requested", stop.Data, StringComparison.Ordinal);

        await WaitForAsync(() =>
            runtime.GetBackgroundRun("background-run-1")?.Status == AgentBackgroundRunStatus.Cancelled);

        var workItem = Assert.Single(runtime.ListWorkItems());
        Assert.Equal(AgentWorkItemStatus.Cancelled, workItem.Status);

        var backgroundRun = Assert.Single(runtime.ListBackgroundRuns());
        Assert.Equal(AgentBackgroundRunStatus.Cancelled, backgroundRun.Status);
        Assert.Equal("user requested", backgroundRun.StopReason);
        Assert.Contains(backgroundRun.Output, chunk =>
            chunk.Contains("Cancellation requested for background-run-1.", StringComparison.Ordinal));
        Assert.Contains(backgroundRun.Output, chunk =>
            chunk.Contains("was cancelled", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_BackgroundSubagentsQueueWhenConcurrencyIsFull()
    {
        var runtime = new InMemoryAgentTaskRuntime();
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requestOrder = new List<string>();
        var gate = new object();
        var runner = new AsyncRecordingRunner(async (request, cancellationToken) =>
        {
            lock (gate)
                requestOrder.Add(request.Prompt);

            if (string.Equals(request.Prompt, "first task", StringComparison.Ordinal))
            {
                firstStarted.TrySetResult();
                await releaseFirst.Task.WaitAsync(cancellationToken);
            }

            return new AgentExecutionResult(
                Summary: $"summary for {request.Prompt}",
                Success: true,
                Usage: TokenUsage.Empty,
                TurnCount: 1);
        });

        var tool = new AgentTool(
            runner,
            new DefaultProviderCapabilityRouter(),
            runtime,
            hooks: HookRuntime.Empty,
            backgroundRunScheduler: new BackgroundAgentRunScheduler(maxConcurrency: 1));

        var firstLaunch = await tool.ExecuteAsync(
            JsonSerializer.SerializeToElement(new
            {
                prompt = "first task",
                run_in_background = true,
            }),
            CreateContext());
        var secondLaunch = await tool.ExecuteAsync(
            JsonSerializer.SerializeToElement(new
            {
                prompt = "second task",
                run_in_background = true,
            }),
            CreateContext());

        Assert.False(firstLaunch.IsError);
        Assert.False(secondLaunch.IsError);

        await firstStarted.Task;
        await WaitForAsync(() =>
            runtime.GetBackgroundRun("background-run-1")?.Status == AgentBackgroundRunStatus.Running &&
            runtime.GetBackgroundRun("background-run-2")?.Status == AgentBackgroundRunStatus.Queued);

        Assert.Equal(["first task"], runner.Requests.Select(request => request.Prompt));

        releaseFirst.TrySetResult();

        await WaitForAsync(() =>
            runtime.GetBackgroundRun("background-run-1")?.Status == AgentBackgroundRunStatus.Stopped &&
            runtime.GetBackgroundRun("background-run-2")?.Status == AgentBackgroundRunStatus.Stopped);

        Assert.Equal(["first task", "second task"], runner.Requests.Select(request => request.Prompt));
        Assert.Contains(
            runtime.GetBackgroundRun("background-run-2")!.Output,
            chunk => chunk.Contains("Queued prompt: second task", StringComparison.Ordinal));
        Assert.Contains(
            runtime.GetBackgroundRun("background-run-2")!.Output,
            chunk => chunk.Contains("Background run started", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_CancellingQueuedBackgroundSubagentPreventsExecution()
    {
        var runtime = new InMemoryAgentTaskRuntime();
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runner = new AsyncRecordingRunner(async (request, cancellationToken) =>
        {
            if (string.Equals(request.Prompt, "first task", StringComparison.Ordinal))
            {
                firstStarted.TrySetResult();
                await releaseFirst.Task.WaitAsync(cancellationToken);
            }

            return new AgentExecutionResult(
                Summary: $"summary for {request.Prompt}",
                Success: true,
                Usage: TokenUsage.Empty,
                TurnCount: 1);
        });

        var tool = new AgentTool(
            runner,
            new DefaultProviderCapabilityRouter(),
            runtime,
            hooks: HookRuntime.Empty,
            backgroundRunScheduler: new BackgroundAgentRunScheduler(maxConcurrency: 1));

        await tool.ExecuteAsync(
            JsonSerializer.SerializeToElement(new
            {
                prompt = "first task",
                run_in_background = true,
            }),
            CreateContext());
        await tool.ExecuteAsync(
            JsonSerializer.SerializeToElement(new
            {
                prompt = "second task",
                run_in_background = true,
            }),
            CreateContext());

        await firstStarted.Task;
        await WaitForAsync(() =>
            runtime.GetBackgroundRun("background-run-2")?.Status == AgentBackgroundRunStatus.Queued);

        var stopTool = new AgentStopTool(runtime);
        var stop = await stopTool.ExecuteAsync(
            JsonSerializer.SerializeToElement(new
            {
                id = "background-run-2",
                reason = "queue no longer needed",
            }),
            CreateContext());

        Assert.False(stop.IsError);

        await WaitForAsync(() =>
            runtime.GetBackgroundRun("background-run-2")?.Status == AgentBackgroundRunStatus.Cancelled);

        releaseFirst.TrySetResult();
        await WaitForAsync(() =>
            runtime.GetBackgroundRun("background-run-1")?.Status == AgentBackgroundRunStatus.Stopped);

        Assert.Equal(["first task"], runner.Requests.Select(request => request.Prompt));
        Assert.Equal(
            AgentWorkItemStatus.Cancelled,
            runtime.GetWorkItem("work-item-2")?.Status);
        Assert.Contains(
            runtime.GetBackgroundRun("background-run-2")!.Output,
            chunk => chunk.Contains("before execution started", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AgentStatusTool_ReturnsOverviewAndDetails()
    {
        var runtime = new InMemoryAgentTaskRuntime();
        var workItem = runtime.CreateWorkItem("Inspect tools", owner: "subagent");
        runtime.UpdateWorkItem(workItem.Id, item => item.Status = AgentWorkItemStatus.Completed);

        var backgroundRun = runtime.StartBackgroundRun("Inspect tools", "subagent");
        runtime.AppendBackgroundRunOutput(backgroundRun.Id, "Summary: all good");
        runtime.StopBackgroundRun(backgroundRun.Id, "completed");

        var tool = new AgentStatusTool(runtime);

        var overview = await tool.ExecuteAsync(
            JsonSerializer.SerializeToElement(new { }),
            CreateContext());
        var details = await tool.ExecuteAsync(
            JsonSerializer.SerializeToElement(new
            {
                id = backgroundRun.Id,
                include_output = true,
            }),
            CreateContext());

        Assert.False(overview.IsError);
        Assert.Contains(workItem.Id, overview.Data, StringComparison.Ordinal);
        Assert.Contains(backgroundRun.Id, overview.Data, StringComparison.Ordinal);

        Assert.False(details.IsError);
        Assert.Contains("Background run:", details.Data, StringComparison.Ordinal);
        Assert.Contains("Summary: all good", details.Data, StringComparison.Ordinal);
        Assert.Contains("completed", details.Data, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AgentStatusTool_ReturnsErrorForMissingId()
    {
        var tool = new AgentStatusTool(new InMemoryAgentTaskRuntime());

        var result = await tool.ExecuteAsync(
            JsonSerializer.SerializeToElement(new { id = "missing" }),
            CreateContext());

        Assert.True(result.IsError);
        Assert.Contains("missing", result.Data, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AgentStopTool_ReturnsErrorForMissingId()
    {
        var tool = new AgentStopTool(new InMemoryAgentTaskRuntime());

        var result = await tool.ExecuteAsync(
            JsonSerializer.SerializeToElement(new { id = "missing" }),
            CreateContext());

        Assert.True(result.IsError);
        Assert.Contains("missing", result.Data, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AgentWaitTool_WaitsForBackgroundRunToFinish()
    {
        var runtime = new InMemoryAgentTaskRuntime();
        var run = runtime.StartBackgroundRun("Inspect tools", "subagent");
        runtime.AppendBackgroundRunOutput(run.Id, "line 1");

        _ = Task.Run(async () =>
        {
            await Task.Delay(25);
            runtime.AppendBackgroundRunOutput(run.Id, "line 2");
            runtime.StopBackgroundRun(run.Id, "completed");
        });

        var tool = new AgentWaitTool(runtime);
        var result = await tool.ExecuteAsync(
            JsonSerializer.SerializeToElement(new
            {
                id = run.Id,
                poll_ms = 10,
                include_output = true,
            }),
            CreateContext());

        Assert.False(result.IsError);
        Assert.Contains("Wait finished after", result.Data, StringComparison.Ordinal);
        Assert.Contains("Background run:", result.Data, StringComparison.Ordinal);
        Assert.Contains("Stopped", result.Data, StringComparison.Ordinal);
        Assert.Contains("line 1", result.Data, StringComparison.Ordinal);
        Assert.Contains("line 2", result.Data, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AgentWaitTool_CanTimeOut()
    {
        var runtime = new InMemoryAgentTaskRuntime();
        var run = runtime.StartBackgroundRun("Inspect tools", "subagent");

        var tool = new AgentWaitTool(runtime);
        var result = await tool.ExecuteAsync(
            JsonSerializer.SerializeToElement(new
            {
                id = run.Id,
                poll_ms = 5,
                timeout_ms = 10,
            }),
            CreateContext());

        Assert.True(result.IsError);
        Assert.Contains("Timed out after", result.Data, StringComparison.Ordinal);
        Assert.Contains(run.Id, result.Data, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AgentWaitTool_CanWaitForAnyOfMultipleRuns()
    {
        var runtime = new InMemoryAgentTaskRuntime();
        var firstRun = runtime.StartBackgroundRun("Inspect tools A", "subagent");
        var secondRun = runtime.StartBackgroundRun("Inspect tools B", "subagent");
        runtime.AppendBackgroundRunOutput(secondRun.Id, "second run output");

        _ = Task.Run(async () =>
        {
            await Task.Delay(25);
            runtime.StopBackgroundRun(secondRun.Id, "completed");
        });

        var tool = new AgentWaitTool(runtime);
        var result = await tool.ExecuteAsync(
            JsonSerializer.SerializeToElement(new
            {
                ids = new[] { firstRun.Id, secondRun.Id },
                wait_mode = "any",
                poll_ms = 10,
                include_output = true,
            }),
            CreateContext());

        Assert.False(result.IsError);
        Assert.Contains("Wait finished after", result.Data, StringComparison.Ordinal);
        Assert.Contains("Completed runs:", result.Data, StringComparison.Ordinal);
        Assert.Contains($"{secondRun.Id}: Stopped", result.Data, StringComparison.Ordinal);
        Assert.Contains("Still running:", result.Data, StringComparison.Ordinal);
        Assert.Contains($"{firstRun.Id}: Running", result.Data, StringComparison.Ordinal);
        Assert.Contains("second run output", result.Data, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AgentWaitTool_CanWaitForAllOfMultipleRuns()
    {
        var runtime = new InMemoryAgentTaskRuntime();
        var firstRun = runtime.StartBackgroundRun("Inspect tools A", "subagent");
        var secondRun = runtime.StartBackgroundRun("Inspect tools B", "subagent");

        _ = Task.Run(async () =>
        {
            await Task.Delay(10);
            runtime.StopBackgroundRun(firstRun.Id, "completed");
            await Task.Delay(20);
            runtime.StopBackgroundRun(secondRun.Id, "completed");
        });

        var tool = new AgentWaitTool(runtime);
        var result = await tool.ExecuteAsync(
            JsonSerializer.SerializeToElement(new
            {
                ids = new[] { firstRun.Id, secondRun.Id },
                wait_mode = "all",
                poll_ms = 10,
            }),
            CreateContext());

        Assert.False(result.IsError);
        Assert.Contains("All 2 background run(s) reached terminal states.", result.Data, StringComparison.Ordinal);
        Assert.Contains($"{firstRun.Id}: Stopped", result.Data, StringComparison.Ordinal);
        Assert.Contains($"{secondRun.Id}: Stopped", result.Data, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AgentStatusTool_CanReturnOutputWindow()
    {
        var runtime = new InMemoryAgentTaskRuntime();
        var backgroundRun = runtime.StartBackgroundRun("Inspect tools", "subagent");
        runtime.AppendBackgroundRunOutput(backgroundRun.Id, "line 1");
        runtime.AppendBackgroundRunOutput(backgroundRun.Id, "line 2");
        runtime.AppendBackgroundRunOutput(backgroundRun.Id, "line 3");
        runtime.StopBackgroundRun(backgroundRun.Id, "completed");

        var tool = new AgentStatusTool(runtime);
        var result = await tool.ExecuteAsync(
            JsonSerializer.SerializeToElement(new
            {
                id = backgroundRun.Id,
                include_output = true,
                output_offset = 1,
                output_limit = 1,
            }),
            CreateContext());

        Assert.False(result.IsError);
        Assert.Contains("Showing output entries 2-2 of 3.", result.Data, StringComparison.Ordinal);
        Assert.DoesNotContain("line 1", result.Data, StringComparison.Ordinal);
        Assert.Contains("line 2", result.Data, StringComparison.Ordinal);
        Assert.DoesNotContain("line 3", result.Data, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AgentStatusTool_CanFilterAndPaginateOverview()
    {
        var runtime = new InMemoryAgentTaskRuntime();
        runtime.CreateWorkItem("Inspect tools", owner: "alice");
        runtime.StartBackgroundRun(
            "Queued run one",
            owner: "subagent",
            initialStatus: AgentBackgroundRunStatus.Queued);
        runtime.StartBackgroundRun(
            "Queued run two",
            owner: "subagent",
            initialStatus: AgentBackgroundRunStatus.Queued);
        runtime.UpdateBackgroundRun("background-run-2", _ => { });

        var tool = new AgentStatusTool(runtime);
        var result = await tool.ExecuteAsync(
            JsonSerializer.SerializeToElement(new
            {
                kind = "background_runs",
                status = "queued",
                owner = "subagent",
                offset = 1,
                limit = 1,
            }),
            CreateContext());

        Assert.False(result.IsError);
        Assert.Contains("Background runs:", result.Data, StringComparison.Ordinal);
        Assert.Contains("Showing background runs 2-2 of 2.", result.Data, StringComparison.Ordinal);
        Assert.Contains("Queued run one", result.Data, StringComparison.Ordinal);
        Assert.DoesNotContain("Queued run two", result.Data, StringComparison.Ordinal);
        Assert.DoesNotContain("Work items:", result.Data, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AgentStatusTool_CanRenderSummaryView()
    {
        var runtime = new InMemoryAgentTaskRuntime();
        var completed = runtime.CreateWorkItem("Completed task", owner: "subagent");
        runtime.UpdateWorkItem(completed.Id, item => item.Status = AgentWorkItemStatus.Completed);
        var queued = runtime.StartBackgroundRun(
            "Queued run",
            owner: "subagent",
            workItemId: completed.Id,
            initialStatus: AgentBackgroundRunStatus.Queued);
        var failed = runtime.StartBackgroundRun(
            "Failed run",
            owner: "subagent",
            initialStatus: AgentBackgroundRunStatus.Failed);
        runtime.UpdateBackgroundRun(failed.Id, run => run.StopReason = "network timeout");

        var tool = new AgentStatusTool(runtime);
        var result = await tool.ExecuteAsync(
            JsonSerializer.SerializeToElement(new
            {
                view = "summary",
                owner = "subagent",
                recent_limit = 2,
            }),
            CreateContext());

        Assert.False(result.IsError);
        Assert.Contains("Agent summary (owner: subagent):", result.Data, StringComparison.Ordinal);
        Assert.Contains("Work items: 1", result.Data, StringComparison.Ordinal);
        Assert.Contains("- Completed: 1", result.Data, StringComparison.Ordinal);
        Assert.Contains("Background runs: 2", result.Data, StringComparison.Ordinal);
        Assert.Contains("- Queued: 1", result.Data, StringComparison.Ordinal);
        Assert.Contains("- Failed: 1", result.Data, StringComparison.Ordinal);
        Assert.Contains("Active background runs:", result.Data, StringComparison.Ordinal);
        Assert.Contains(queued.Id, result.Data, StringComparison.Ordinal);
        Assert.Contains("Recent finished background runs:", result.Data, StringComparison.Ordinal);
        Assert.Contains(failed.Id, result.Data, StringComparison.Ordinal);
        Assert.Contains("network timeout", result.Data, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_CanDisableWorkspaceIsolation()
    {
        var runtime = new InMemoryAgentTaskRuntime();
        var runner = new RecordingRunner(new AgentExecutionResult(
            Summary: "child summary",
            Success: true,
            Usage: TokenUsage.Empty,
            TurnCount: 1));

        var tool = new AgentTool(
            runner,
            new DefaultProviderCapabilityRouter(),
            runtime,
            hooks: HookRuntime.Empty);

        var result = await tool.ExecuteAsync(
            JsonSerializer.SerializeToElement(new
            {
                prompt = "Inspect the query pipeline",
                use_isolated_workspace = false,
            }),
            CreateContext());

        Assert.False(result.IsError);
        Assert.False(Assert.Single(runner.Requests).UseIsolatedWorkspace);
    }

    [Fact]
    public async Task QueryEngineAgentRunner_UsesChildQueryEngineAndReturnsSummary()
    {
        using var temp = new TempDirectory();
        var handler = new FakeAnthropicHandler();
        handler.EnqueueResponse(FakeAnthropicHandler.CreateMessageResponse("child answer", inputTokens: 3, outputTokens: 4));
        var client = TestSupport.CreateAnthropicClient(handler);

        var registry = new ToolRegistry();
        registry.Register(new FakeTool { Name = "search" });

        var runner = new QueryEngineAgentRunner(client);
        var result = await runner.RunAsync(
            new AgentExecutionRequest
            {
                Prompt = "Summarize the subsystem",
                WorkingDirectory = temp.Root,
                Model = "claude-sonnet-4-6",
                Tools = registry,
                PermissionContext = new PermissionContext(),
                SystemPromptAppendix = "child appendix",
            });

        Assert.True(result.Success);
        Assert.Equal("child answer", result.Summary);
        Assert.Equal(1, result.TurnCount);
        Assert.Equal(3, result.Usage.InputTokens);
        Assert.Equal(4, result.Usage.OutputTokens);

        var request = JsonDocument.Parse(handler.Bodies[0]).RootElement;
        Assert.Equal("claude-sonnet-4-6", request.GetProperty("model").GetString());
        Assert.Equal("Summarize the subsystem", request.GetProperty("messages")[0].GetProperty("content")[0].GetProperty("text").GetString());
        Assert.Equal("search", request.GetProperty("tools")[0].GetProperty("name").GetString());
        Assert.Contains("child appendix", request.GetProperty("system").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task QueryEngineAgentRunner_UsesWorkspaceManagerWorkingDirectory()
    {
        using var temp = new TempDirectory();
        var source = temp.CreateDirectory("source");
        var isolated = temp.CreateDirectory("isolated");
        var handler = new FakeAnthropicHandler();
        handler.EnqueueResponse(FakeAnthropicHandler.CreateMessageResponse("child answer", inputTokens: 3, outputTokens: 4));
        var client = TestSupport.CreateAnthropicClient(handler);

        var workspaceManager = new RecordingWorkspaceManager(isolated);
        var runner = new QueryEngineAgentRunner(client, workspaceManager: workspaceManager);

        var result = await runner.RunAsync(
            new AgentExecutionRequest
            {
                Prompt = "Summarize the subsystem",
                WorkingDirectory = source,
                Model = "claude-sonnet-4-6",
                Tools = new ToolRegistry(),
                PermissionContext = new PermissionContext(),
                SystemPromptAppendix = "child appendix",
            });

        Assert.True(result.Success);
        Assert.True(workspaceManager.DisposeCalled);

        var request = JsonDocument.Parse(handler.Bodies[0]).RootElement;
        Assert.Contains(
            $"Working Directory: {isolated}",
            request.GetProperty("system").GetString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task QueryEngineAgentRunner_ReportsTextProgress()
    {
        using var temp = new TempDirectory();
        var handler = new FakeAnthropicHandler();
        handler.EnqueueResponse(FakeAnthropicHandler.CreateMessageResponse("child answer", inputTokens: 3, outputTokens: 4));
        var client = TestSupport.CreateAnthropicClient(handler);

        var progressEvents = new List<AgentExecutionProgress>();
        var runner = new QueryEngineAgentRunner(client);

        var result = await runner.RunAsync(
            new AgentExecutionRequest
            {
                Prompt = "Summarize the subsystem",
                WorkingDirectory = temp.Root,
                Model = "claude-sonnet-4-6",
                Tools = new ToolRegistry(),
                PermissionContext = new PermissionContext(),
                Progress = new RecordingProgress(progressEvents),
            });

        Assert.True(result.Success);
        Assert.Contains(progressEvents, evt =>
            evt.Type == "text" &&
            evt.Message.Contains("child answer", StringComparison.Ordinal));
    }

    private static ToolExecutionContext CreateContext() =>
        new()
        {
            WorkingDirectory = "/tmp",
            PermissionContext = new PermissionContext(),
            Tools = [],
            Messages = [],
            CancellationToken = CancellationToken.None,
            MainLoopModel = "claude-sonnet-4-6",
        };

    private static ToolExecutionContext CreateToolContext(
        AgentExecutionRequest request,
        CancellationToken cancellationToken) =>
        new()
        {
            WorkingDirectory = request.WorkingDirectory,
            PermissionContext = request.PermissionContext,
            Tools = request.Tools.GetAllTools(),
            Messages = [],
            CancellationToken = cancellationToken,
            MainLoopModel = request.Model,
        };

    private sealed class RecordingRunner : IAgentExecutionRunner
    {
        private readonly AgentExecutionResult _result;

        public RecordingRunner(AgentExecutionResult result)
        {
            _result = result;
        }

        public List<AgentExecutionRequest> Requests { get; } = [];

        public Task<AgentExecutionResult> RunAsync(
            AgentExecutionRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(_result);
        }
    }

    private sealed class AsyncRecordingRunner : IAgentExecutionRunner
    {
        private readonly Func<AgentExecutionRequest, CancellationToken, Task<AgentExecutionResult>> _handler;

        public AsyncRecordingRunner(
            Func<AgentExecutionRequest, CancellationToken, Task<AgentExecutionResult>> handler)
        {
            _handler = handler;
        }

        public List<AgentExecutionRequest> Requests { get; } = [];

        public async Task<AgentExecutionResult> RunAsync(
            AgentExecutionRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return await _handler(request, cancellationToken);
        }
    }

    private static async Task WaitForAsync(Func<bool> predicate)
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            if (predicate())
                return;

            await Task.Delay(25);
        }

        Assert.True(predicate(), "Condition was not met before timeout.");
    }

    private sealed class RecordingWorkspaceManager : IAgentWorkspaceManager
    {
        private readonly string _workingDirectory;

        public RecordingWorkspaceManager(string workingDirectory)
        {
            _workingDirectory = workingDirectory;
        }

        public bool DisposeCalled { get; private set; }

        public Task<AgentWorkspaceLease> AcquireAsync(
            string workingDirectory,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(
                new AgentWorkspaceLease(
                    _workingDirectory,
                    _workingDirectory,
                    isIsolated: true,
                    disposeAsync: () =>
                    {
                        DisposeCalled = true;
                        return ValueTask.CompletedTask;
                    }));
        }
    }

    private sealed class RecordingProgress : IProgress<AgentExecutionProgress>
    {
        private readonly List<AgentExecutionProgress> _events;

        public RecordingProgress(List<AgentExecutionProgress> events)
        {
            _events = events;
        }

        public void Report(AgentExecutionProgress value) => _events.Add(value);
    }
}
