using ModelContextProtocol.Protocol;
using WindowsMcpNet.Server;
using Xunit;

namespace WindowsMcpNet.Tests.Server;

public class ErrorFlagFilterTests
{
    [Fact]
    public void Apply_ErrorPrefix_SetsIsErrorTrue()
    {
        var input = new CallToolResult
        {
            Content = [new TextContentBlock { Text = "[ERROR] FileNotFoundException: file not found" }],
            IsError = false,
        };

        var output = ErrorFlagFilter.Apply(input);

        Assert.True(output.IsError);
        Assert.Equal(input.Content, output.Content);
    }

    [Fact]
    public void Apply_NoErrorPrefix_LeavesIsErrorUnchanged()
    {
        var input = new CallToolResult
        {
            Content = [new TextContentBlock { Text = "Read 42 bytes from foo.txt" }],
            IsError = false,
        };

        var output = ErrorFlagFilter.Apply(input);

        Assert.NotEqual(true, output.IsError);
    }

    [Fact]
    public void Apply_AlreadyMarkedError_PassesThrough()
    {
        var input = new CallToolResult
        {
            Content = [new TextContentBlock { Text = "anything" }],
            IsError = true,
        };

        var output = ErrorFlagFilter.Apply(input);

        Assert.True(output.IsError);
        Assert.Same(input, output);
    }

    [Fact]
    public void Apply_EmptyContent_NoOp()
    {
        var input = new CallToolResult { Content = [], IsError = false };
        var output = ErrorFlagFilter.Apply(input);
        Assert.NotEqual(true, output.IsError);
    }

    [Fact]
    public void Apply_MixedBlocks_ErrorInOne_FlagsAsError()
    {
        // Tools like Snapshot return image + text. If the text reports an error,
        // the whole response should be flagged as error.
        var input = new CallToolResult
        {
            Content =
            [
                ImageContentBlock.FromBytes(new byte[] { 0xDE, 0xAD }, "image/png"),
                new TextContentBlock { Text = "[ERROR] InvalidOperationException: tree build failed" },
            ],
            IsError = false,
        };

        var output = ErrorFlagFilter.Apply(input);

        Assert.True(output.IsError);
    }

    [Fact]
    public void Apply_NonErrorTextBlock_LeavesUnflagged()
    {
        var input = new CallToolResult
        {
            Content = [new TextContentBlock { Text = "Tool output that mentions the word ERROR somewhere in the middle" }],
            IsError = false,
        };

        var output = ErrorFlagFilter.Apply(input);

        Assert.NotEqual(true, output.IsError);
    }
}
