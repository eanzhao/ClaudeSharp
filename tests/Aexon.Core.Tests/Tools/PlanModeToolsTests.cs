using System.Text.Json;
using Aexon.Core.Messages;
using Aexon.Core.Permissions;
using Aexon.Core.Query;
using Aexon.Core.Tools;
using Aexon.Tools;

namespace Aexon.Core.Tests.Tools;

public sealed class PlanModeToolsTests
{
    [Fact]
    public async Task EnterAndExitTools_TogglePlanModeState()
    {
        var controller = new FakePlanModeController(PermissionMode.Auto);
        var enterTool = new EnterPlanModeTool(controller);
        var exitTool = new ExitPlanModeTool(controller);
        var context = CreateContext(PermissionMode.Auto);

        Assert.True(enterTool.IsEnabled());
        Assert.False(exitTool.IsEnabled());

        var enterResult = await enterTool.ExecuteAsync(JsonSerializer.SerializeToElement(new { }), context);

        Assert.True(controller.IsPlanModeActive);
        Assert.False(enterTool.IsEnabled());
        Assert.True(exitTool.IsEnabled());
        Assert.Contains("Plan mode enabled.", enterResult.Data, StringComparison.Ordinal);

        var exitResult = await exitTool.ExecuteAsync(JsonSerializer.SerializeToElement(new { }), context);

        Assert.False(controller.IsPlanModeActive);
        Assert.Equal(PermissionMode.Auto, controller.CurrentMode);
        Assert.Contains("Restored permission mode: Auto", exitResult.Data, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExitTool_ReturnsErrorWhenPlanModeIsInactive()
    {
        var controller = new FakePlanModeController(PermissionMode.Default);
        var exitTool = new ExitPlanModeTool(controller);

        var result = await exitTool.ExecuteAsync(
            JsonSerializer.SerializeToElement(new { }),
            CreateContext(PermissionMode.Default));

        Assert.True(result.IsError);
        Assert.Equal("Plan mode is not active.", result.Data);
    }

    private static ToolExecutionContext CreateContext(PermissionMode mode) =>
        new()
        {
            WorkingDirectory = Environment.CurrentDirectory,
            PermissionContext = new PermissionContext
            {
                Mode = mode,
            },
            Tools = [],
            Messages = [],
            CancellationToken = CancellationToken.None,
            MainLoopModel = ClaudeModels.DefaultMainModel,
        };

    private sealed class FakePlanModeController : IPlanModeController
    {
        private PermissionMode _resumeMode;

        public FakePlanModeController(PermissionMode currentMode)
        {
            CurrentMode = currentMode;
            _resumeMode = currentMode == PermissionMode.Plan
                ? PermissionMode.Default
                : currentMode;
        }

        public PermissionMode CurrentMode { get; private set; }

        public bool IsPlanModeActive => CurrentMode == PermissionMode.Plan;

        public PermissionMode PlanModeResumeMode => _resumeMode;

        public Task<bool> EnterPlanModeAsync(CancellationToken ct = default)
        {
            if (IsPlanModeActive)
                return Task.FromResult(false);

            _resumeMode = CurrentMode;
            CurrentMode = PermissionMode.Plan;
            return Task.FromResult(true);
        }

        public Task<PermissionMode> ExitPlanModeAsync(CancellationToken ct = default)
        {
            CurrentMode = _resumeMode;
            return Task.FromResult(CurrentMode);
        }
    }
}
