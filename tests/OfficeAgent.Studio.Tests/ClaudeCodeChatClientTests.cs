using System.Diagnostics;
using Microsoft.Extensions.AI;
using OfficeAgent.Studio;
using Xunit;

namespace OfficeAgent.Studio.Tests;

public class ClaudeCodeChatClientTests
{
    [Fact]
    public async Task Caller_cancellation_terminates_the_CLI_process_tree()
    {
        var root = Path.Combine(Path.GetTempPath(), "officeagent-studio-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        int? startedProcessId = null;

        try
        {
            var executable = CreateLongRunningCommand(root);
            using var client = new ClaudeCodeChatClient(
                executable,
                TimeSpan.FromMinutes(1),
                process => startedProcessId = process.Id);
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetResponseAsync(
                new[] { new ChatMessage(ChatRole.User, "Return JSON.") },
                cancellationToken: cancellation.Token));

            Assert.NotNull(startedProcessId);
            var deadline = Stopwatch.StartNew();
            while (IsRunning(startedProcessId!.Value) && deadline.Elapsed < TimeSpan.FromSeconds(5))
                await Task.Delay(50);

            Assert.False(IsRunning(startedProcessId.Value), "The cancelled model CLI was still running.");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static bool IsRunning(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static string CreateLongRunningCommand(string root)
    {
        if (OperatingSystem.IsWindows())
        {
            var path = Path.Combine(root, "wait.cmd");
            File.WriteAllText(path, "@echo off\r\nping 127.0.0.1 -n 30 >nul\r\n");
            return path;
        }

        var script = Path.Combine(root, "wait.sh");
        File.WriteAllText(script, "#!/bin/sh\nsleep 30\n");
        File.SetUnixFileMode(
            script,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return script;
    }
}
