using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Collections.Generic;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

class Program
{
    static async Task Main()
    {
        Console.WriteLine("Starting Peerfluence...");
        var process = Process.Start(new ProcessStartInfo
        {
            FileName = @"Peerfluence\bin\Debug\net10.0\Peerfluence.exe",
            UseShellExecute = false,
        });

        await Task.Delay(5000);

        using var pipe = new NamedPipeClientStream(".", "PeerfluenceMcpPipe", PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            await pipe.ConnectAsync(5000);
            Console.WriteLine("Connected to MCP Pipe.");
        }
        catch (Exception ex)
        {
            Console.WriteLine("Failed to connect to MCP: " + ex.Message);
            // If we can't connect, maybe it crashed? Check logs.
            ReadLogs();
            process?.Kill();
            return;
        }

        var transport = new StreamClientTransport(pipe, pipe, null);
        var options = new McpClientOptions { ClientInfo = new Implementation { Name = "Debugger", Version = "1.0.0" } };
        await using var client = await McpClient.CreateAsync(transport, options);

        Console.WriteLine("Taking screenshot...");
        try
        {
            var res = await client.CallToolAsync(new CallToolRequestParams { Name = "take_screenshot", Arguments = new Dictionary<string, System.Text.Json.JsonElement>() });
            if (res.Content != null)
            {
                foreach (var b in res.Content)
                {
                    if (b is TextContentBlock txt && txt.Text.StartsWith("[IMAGE_BASE64]"))
                    {
                        var base64 = txt.Text.Substring("[IMAGE_BASE64] ".Length);
                        File.WriteAllBytes("debug_screenshot.png", Convert.FromBase64String(base64));
                        Console.WriteLine("Saved debug_screenshot.png");
                    }
                    else if (b is TextContentBlock t)
                    {
                        Console.WriteLine("Tool Error: " + t.Text);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("Screenshot failed: " + ex.Message);
        }

        Console.WriteLine("Getting logs via MCP...");
        try
        {
            var logRes = await client.ReadResourceAsync(new ReadResourceRequestParams { Uri = "peerfluence://logs/latest" });
            if (logRes.Contents != null && logRes.Contents.Count > 0 && logRes.Contents[0] is TextResourceContents text)
            {
                File.WriteAllText("debug_logs.txt", text.Text);
                Console.WriteLine("Saved debug_logs.txt");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("Log fetch failed: " + ex.Message);
        }

        await client.CallToolAsync(new CallToolRequestParams { Name = "shutdown_application", Arguments = new Dictionary<string, System.Text.Json.JsonElement>() });
        if (process != null && !process.HasExited)
        {
            process.WaitForExit(3000);
            if (!process.HasExited) process.Kill();
        }
    }

    static void ReadLogs()
    {
        var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Peerfluence");
        var logPath = Path.Combine(appData, "peerfluence.log");
        if (File.Exists(logPath))
        {
            Console.WriteLine("\n--- DISK LOGS ---");
            Console.WriteLine(File.ReadAllText(logPath));
        }
        else
        {
            Console.WriteLine("No logs found on disk.");
        }
    }
}
