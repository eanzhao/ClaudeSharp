using Aexon.Core.Commands;
using Aexon.Core.Extensions;
using Aexon.Core.Hooks;
using Aexon.Core.Tools;

namespace Aexon.Core.Tests.Extensions;

public class ExtensionRegistryTests
{
    [Fact]
    public async Task RunAsync_EmptyRegistry_ReturnsWithoutTouchingBuilder()
    {
        var registry = new ExtensionRegistry();
        var builder = new FakeSessionBuilder();

        await registry.RunAsync(builder, CancellationToken.None);

        Assert.Empty(builder.Fragments);
        Assert.Empty(builder.RegisteredToolNames);
    }

    [Fact]
    public async Task RunAsync_CallsEveryExtensionInRegistrationOrder()
    {
        var ext1 = new RecordingExtension("ext-1");
        var ext2 = new RecordingExtension("ext-2");
        var registry = new ExtensionRegistry();
        registry.Add(ext1);
        registry.Add(ext2);
        var builder = new FakeSessionBuilder();

        await registry.RunAsync(builder, CancellationToken.None);

        Assert.True(ext1.Ran);
        Assert.True(ext2.Ran);
        Assert.Equal(["fragment from ext-1", "fragment from ext-2"], builder.Fragments);
    }

    [Fact]
    public async Task RunAsync_CancelledBeforeRun_ThrowsOperationCanceled()
    {
        var ext = new RecordingExtension("ext");
        var registry = new ExtensionRegistry();
        registry.Add(ext);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => registry.RunAsync(new FakeSessionBuilder(), cts.Token));

        Assert.False(ext.Ran);
    }

    [Fact]
    public void Add_NullExtension_Throws()
    {
        var registry = new ExtensionRegistry();
        Assert.Throws<ArgumentNullException>(() => registry.Add(null!));
    }

    private sealed class RecordingExtension(string name) : IAexonExtension
    {
        public string Name => name;
        public bool Ran { get; private set; }

        public Task ConfigureAsync(IAexonSessionBuilder builder, CancellationToken cancellationToken)
        {
            Ran = true;
            builder.AppendSystemPromptFragment($"fragment from {Name}");
            return Task.CompletedTask;
        }
    }

    private sealed class FakeSessionBuilder : IAexonSessionBuilder
    {
        public List<string> Fragments { get; } = [];
        public List<string> RegisteredToolNames { get; } = [];

        public string WorkingDirectory => "/tmp";
        public string Model => "claude-sonnet-4-6";

        public void RegisterTool(ITool tool) => RegisteredToolNames.Add(tool.Name);
        public void RegisterCommand(ICommand command) { }
        public void RegisterHookObserver(HookObserver observer) { }
        public void AppendSystemPromptFragment(string fragment) => Fragments.Add(fragment);
    }
}
