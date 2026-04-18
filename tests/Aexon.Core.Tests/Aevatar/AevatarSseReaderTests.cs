using System.Text;
using System.Text.Json;
using Aexon.Core.Aevatar;

namespace Aexon.Core.Tests.Aevatar;

public sealed class AevatarSseReaderTests
{
    [Fact]
    public async Task ParsesAllFrameTypesFromAStreamWithMixedBoundaries()
    {
        const string payload = """
            data: {"type":"RUN_STARTED","actorId":"a_1"}

            data: {"type":"TEXT_MESSAGE_START","textMessageStart":{"messageId":"m_1","role":"assistant"}}

            data: {"type":"TEXT_MESSAGE_CONTENT","textMessageContent":{"delta":"hi"}}

            : keep-alive ping we must ignore

            data: {"type":"TEXT_MESSAGE_END","textMessageEnd":{"messageId":"m_1"}}

            data: {"type":"TOOL_CALL_START","toolCallStart":{"toolName":"search","toolCallId":"c_1"}}

            data: {"type":"TOOL_CALL_END","toolCallEnd":{"toolCallId":"c_1","result":"done"}}

            data: {"type":"STEP_STARTED","stepStarted":{"stepName":"plan"}}

            data: {"type":"STEP_FINISHED","stepFinished":{"stepName":"plan"}}

            data: {"type":"RUN_FINISHED"}

            data: [DONE]

            """;

        var frames = await CollectFramesAsync(payload);

        Assert.Collection(
            frames,
            f => Assert.Equal(AevatarChatFrameType.RunStarted, f.Type),
            f => Assert.Equal(AevatarChatFrameType.TextMessageStart, f.Type),
            f => Assert.Equal(AevatarChatFrameType.TextMessageContent, f.Type),
            f => Assert.Equal(AevatarChatFrameType.TextMessageEnd, f.Type),
            f => Assert.Equal(AevatarChatFrameType.ToolCallStart, f.Type),
            f => Assert.Equal(AevatarChatFrameType.ToolCallEnd, f.Type),
            f => Assert.Equal(AevatarChatFrameType.StepStarted, f.Type),
            f => Assert.Equal(AevatarChatFrameType.StepFinished, f.Type),
            f => Assert.Equal(AevatarChatFrameType.RunFinished, f.Type));
    }

    [Fact]
    public async Task FrameTypeEnumMapsUnknownFramesGracefully()
    {
        var frames = await CollectFramesAsync("""
            data: {"type":"SOMETHING_FUTURE","payload":42}

            """);

        var frame = Assert.Single(frames);
        Assert.Equal(AevatarChatFrameType.Unknown, frame.Type);
        Assert.Equal("SOMETHING_FUTURE", frame.RawType);
    }

    [Fact]
    public async Task SkipsMalformedJsonWithoutFailing()
    {
        var frames = await CollectFramesAsync("""
            data: not json

            data: {"type":"RUN_FINISHED"}

            """);

        var frame = Assert.Single(frames);
        Assert.Equal(AevatarChatFrameType.RunFinished, frame.Type);
    }

    [Fact]
    public async Task HandlesMultilineDataFields()
    {
        var frames = await CollectFramesAsync("""
            data: {"type":"TEXT_MESSAGE_CONTENT",
            data: "textMessageContent":{"delta":"mul\nti"}}

            """);

        var frame = Assert.Single(frames);
        Assert.Equal(AevatarChatFrameType.TextMessageContent, frame.Type);
        Assert.Equal("mul\nti", frame.TryGetString("textMessageContent", "delta"));
    }

    [Fact]
    public void TryGetStringWalksNestedObjects()
    {
        using var doc = JsonDocument.Parse("""{"runError":{"message":"boom"}}""");
        var frame = new AevatarChatFrame(AevatarChatFrameType.RunError, "RUN_ERROR", doc.RootElement.Clone());

        Assert.Equal("boom", frame.TryGetString("runError", "message"));
        Assert.Null(frame.TryGetString("runError", "doesnotexist"));
        Assert.Null(frame.TryGetString("toolCallEnd", "anything"));
    }

    [Fact]
    public async Task TerminatesEarlyOnBareDoneMarker()
    {
        var frames = await CollectFramesAsync("""
            data: {"type":"RUN_STARTED","actorId":"a_1"}

            data: [DONE]

            data: {"type":"RUN_FINISHED"}

            """);

        var frame = Assert.Single(frames);
        Assert.Equal(AevatarChatFrameType.RunStarted, frame.Type);
    }

    private static async Task<IReadOnlyList<AevatarChatFrame>> CollectFramesAsync(string sseText)
    {
        var bytes = Encoding.UTF8.GetBytes(sseText.Replace("\r\n", "\n"));
        using var stream = new MemoryStream(bytes);
        var frames = new List<AevatarChatFrame>();
        await foreach (var frame in AevatarSseReader.ReadAsync(stream, CancellationToken.None))
            frames.Add(frame);
        return frames;
    }
}
