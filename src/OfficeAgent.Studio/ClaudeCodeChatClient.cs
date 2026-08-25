using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.AI;

namespace OfficeAgent.Studio;

/// <summary>
/// An <see cref="IChatClient"/> that runs the Claude Code CLI, so the demo needs a Claude
/// subscription rather than an API key.
/// </summary>
/// <remarks>
/// This exists so the sample runs with nothing configured beyond a CLI the developer has
/// already signed in to. It is deliberately the <em>only</em> Claude-specific file: swap it
/// for <c>AzureOpenAIClient(...).AsIChatClient()</c> or any other
/// <see cref="IChatClient"/> and nothing else in the project changes. It does not implement
/// streaming or tool calling - the composers call OfficeAgent directly, and the model's job
/// here is to return one structured plan.
/// </remarks>
public sealed class ClaudeCodeChatClient : IChatClient
{
    private readonly string _executable;
    private readonly TimeSpan _timeout;
    private readonly Action<Process>? _processStarted;

    public ClaudeCodeChatClient(string executable = "claude", TimeSpan? timeout = null)
        : this(executable, timeout, processStarted: null)
    {
    }

    internal ClaudeCodeChatClient(
        string executable, TimeSpan? timeout, Action<Process>? processStarted)
    {
        if (string.IsNullOrWhiteSpace(executable))
            throw new ArgumentException("The model executable cannot be empty.", nameof(executable));
        if (timeout is { } supplied && supplied <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout), "The model timeout must be positive.");

        _executable = executable;
        _timeout = timeout ?? TimeSpan.FromMinutes(5);
        _processStarted = processStarted;
    }

    public ChatClientMetadata Metadata { get; } = new("claude-code");

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        // An agent's instructions arrive on ChatOptions rather than as a system message,
        // so a client that only reads the message list silently drops the entire contract
        // and the model answers the bare user turn - asking clarifying questions instead
        // of returning the JSON it was told to return.
        var system = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(options?.Instructions))
            system.AppendLine(options!.Instructions);

        var prompt = new StringBuilder();
        foreach (var message in messages)
        {
            var text = message.Text;
            if (string.IsNullOrWhiteSpace(text)) continue;

            if (message.Role == ChatRole.System) system.AppendLine(text);
            else prompt.AppendLine(text).AppendLine();
        }

        var output = await RunAsync(system.ToString(), prompt.ToString(), cancellationToken);
        return new ChatResponse(new ChatMessage(ChatRole.Assistant, output));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await GetResponseAsync(messages, options, cancellationToken);
        foreach (var message in response.Messages)
            yield return new ChatResponseUpdate(message.Role, message.Text);
    }

    private async Task<string> RunAsync(string system, string prompt, CancellationToken cancellationToken)
    {
        // The prompt goes in on stdin: a brief runs to several kilobytes, which is past
        // what a command line reliably takes on Windows.
        var info = new ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? "cmd.exe" : _executable,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (OperatingSystem.IsWindows())
        {
            info.ArgumentList.Add("/c");
            info.ArgumentList.Add(_executable);
        }

        info.ArgumentList.Add("-p");

        // This call wants text back, nothing else. Without these the CLI brings its whole
        // agent along: it reads the CLAUDE.md of whatever directory it started in, picks up
        // MCP servers, and - having tools - may answer by trying to *do* the brief rather
        // than write it. Left unset it will reply with things like "the sandbox is blocking
        // dotnet run", which is a coherent answer to the wrong question.
        info.ArgumentList.Add("--allowed-tools");
        info.ArgumentList.Add(string.Empty);
        info.ArgumentList.Add("--strict-mcp-config");

        if (!string.IsNullOrWhiteSpace(system))
        {
            info.ArgumentList.Add("--append-system-prompt");
            info.ArgumentList.Add(system);
        }

        // Somewhere with no project context to inherit.
        info.WorkingDirectory = Path.GetTempPath();

        Process process;
        try
        {
            process = Process.Start(info)
                ?? throw new ModelUnavailableException($"Could not start '{_executable}'.");
        }
        catch (System.ComponentModel.Win32Exception error)
        {
            throw new ModelUnavailableException(
                $"'{_executable}' could not be run: {error.Message}", error);
        }

        using var _ = process;
        _processStarted?.Invoke(process);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);

        try
        {
            // The contract goes in the turn as well as the system prompt.
            if (!string.IsNullOrWhiteSpace(system))
                await process.StandardInput.WriteAsync(
                    (system + Environment.NewLine + Environment.NewLine).AsMemory(), timeout.Token);

            await process.StandardInput.WriteAsync(prompt.AsMemory(), timeout.Token);
            process.StandardInput.Close();

            var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var stderr = process.StandardError.ReadToEndAsync(timeout.Token);

            await process.WaitForExitAsync(timeout.Token);

            var text = await stdout;
            if (process.ExitCode != 0)
            {
                var message = (await stderr).Trim();

                // A CLI that is present but not usable - not signed in, out of quota, wrong
                // version - exits non-zero without producing a reply. That is a setup problem
                // and is worth naming as one rather than retrying it three times.
                throw new ModelUnavailableException(
                    $"'{_executable}' exited {process.ExitCode}" +
                    (message.Length > 0 ? $": {message}" : " without a message."));
            }

            return text.Trim();
        }
        catch (OperationCanceledException)
        {
            // Disposing Process only releases the local handle; it does not terminate the
            // CLI. Kill on both timeout and caller cancellation so Ctrl+C cannot leave a
            // model call and its descendants running in the background.
            TryKill(process);
            await WaitForExitAfterKillAsync(process);

            if (cancellationToken.IsCancellationRequested)
                cancellationToken.ThrowIfCancellationRequested();

            throw new ModelUnavailableException(
                $"'{_executable}' did not answer within {_timeout.TotalMinutes:0.#} minutes.");
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (Exception)
        {
            // Already gone, or not ours to kill. Either way there is nothing useful to do.
        }
    }

    private static async Task WaitForExitAfterKillAsync(Process process)
    {
        try
        {
            if (!process.HasExited) await process.WaitForExitAsync(CancellationToken.None);
        }
        catch (Exception)
        {
            // The process was already reaped or the platform cannot wait on it any longer.
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose() { }
}
