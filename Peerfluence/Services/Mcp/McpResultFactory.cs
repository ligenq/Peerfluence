using System.Collections.Generic;
using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace Peerfluence.Services.Mcp;

internal static class McpResultFactory
{
    public static CallToolResult Success(string message)
    {
        return Text(JsonSerializer.Serialize(
            new McpConstants.McpOperationResult(true, message),
            McpJsonContext.Default.McpOperationResult));
    }

    public static CallToolResult Error(string message, string code)
    {
        return Text(JsonSerializer.Serialize(
            new McpConstants.McpOperationResult(false, message, code),
            McpJsonContext.Default.McpOperationResult), isError: true);
    }

    public static CallToolResult Text(string text, bool isError = false)
    {
        return new CallToolResult
        {
            IsError = isError,
            Content = new List<ContentBlock> { new TextContentBlock { Text = text } }
        };
    }
}
