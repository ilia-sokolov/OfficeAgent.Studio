using Azure;
using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Extensions.AI;
using System.Runtime.CompilerServices;

namespace OfficeAgent.Studio;

/// <summary>Creates the configured model client without coupling the studio agent to a provider.</summary>
internal static class ModelClientFactory
{
    internal const string ProviderVariable = "OFFICEAGENT_STUDIO_MODEL_PROVIDER";

    internal static IChatClient CreateFromEnvironment() =>
        Create(Environment.GetEnvironmentVariable);

    internal static IChatClient Create(Func<string, string?> setting)
    {
        ArgumentNullException.ThrowIfNull(setting);

        var provider = Optional(setting, ProviderVariable)?.ToLowerInvariant() ?? "claude";
        var (name, client) = provider switch
        {
            "claude" => ("claude", CreateClaude(setting)),
            "azure-foundry" or "foundry" or "azure-openai" =>
                ("azure-foundry", CreateAzureFoundry(setting)),
            _ => throw new ArgumentException(
                $"Unknown model provider '{provider}'. Expected claude or azure-foundry.")
        };

        return new ProviderChatClient(name, client);
    }

    private static IChatClient CreateClaude(Func<string, string?> setting)
    {
        var executable = Optional(setting, "OFFICEAGENT_STUDIO_CLAUDE_EXECUTABLE") ?? "claude";
        var timeoutText = Optional(setting, "OFFICEAGENT_STUDIO_CLAUDE_TIMEOUT_SECONDS");
        TimeSpan? timeout = null;
        if (timeoutText is not null)
        {
            if (!double.TryParse(
                    timeoutText,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var seconds)
                || !double.IsFinite(seconds)
                || seconds <= 0
                || seconds > TimeSpan.MaxValue.TotalSeconds)
            {
                throw new ArgumentException(
                    "OFFICEAGENT_STUDIO_CLAUDE_TIMEOUT_SECONDS must be a positive number.");
            }

            timeout = TimeSpan.FromSeconds(seconds);
        }

        return new ClaudeCodeChatClient(executable, timeout);
    }

    private static IChatClient CreateAzureFoundry(Func<string, string?> setting)
    {
        var endpoint = AbsoluteUri(
            Required(setting, "AZURE_OPENAI_ENDPOINT"),
            "AZURE_OPENAI_ENDPOINT");
        var deployment = Optional(setting, "AZURE_OPENAI_DEPLOYMENT")
            ?? Optional(setting, "AZURE_OPENAI_MODEL")
            ?? throw new ArgumentException(
                "AZURE_OPENAI_DEPLOYMENT (or AZURE_OPENAI_MODEL) is required when " +
                $"{ProviderVariable}=azure-foundry.");
        var apiKey = Optional(setting, "AZURE_OPENAI_API_KEY");

        var client = apiKey is null
            ? new AzureOpenAIClient(endpoint, new DefaultAzureCredential())
            : new AzureOpenAIClient(endpoint, new AzureKeyCredential(apiKey));

        return client.GetChatClient(deployment).AsIChatClient();
    }

    private static string Required(Func<string, string?> setting, string name) =>
        Optional(setting, name) ?? throw new ArgumentException(
            $"{name} is required when {ProviderVariable}=" +
            $"{Optional(setting, ProviderVariable) ?? "claude"}.");

    private static string? Optional(Func<string, string?> setting, string name)
    {
        var value = setting(name);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static Uri AbsoluteUri(string value, string name)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https"))
        {
            throw new ArgumentException($"{name} must be an absolute HTTP or HTTPS URL.");
        }

        return uri;
    }
}

/// <summary>Turns provider-specific transport failures into one actionable studio error.</summary>
internal sealed class ProviderChatClient : IChatClient
{
    private readonly IChatClient _inner;

    internal ProviderChatClient(string provider, IChatClient inner)
    {
        Provider = provider;
        _inner = inner;
    }

    internal string Provider { get; }

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await _inner.GetResponseAsync(messages, options, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ModelUnavailableException)
        {
            throw;
        }
        catch (Exception error)
        {
            throw Unavailable(error);
        }
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await using var updates = _inner
            .GetStreamingResponseAsync(messages, options, cancellationToken)
            .GetAsyncEnumerator(cancellationToken);

        while (true)
        {
            bool hasNext;
            try
            {
                hasNext = await updates.MoveNextAsync();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (ModelUnavailableException)
            {
                throw;
            }
            catch (Exception error)
            {
                throw Unavailable(error);
            }

            if (!hasNext) yield break;
            yield return updates.Current;
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is null && serviceType.IsInstanceOfType(this)
            ? this
            : _inner.GetService(serviceType, serviceKey);

    public void Dispose() => _inner.Dispose();

    private ModelUnavailableException Unavailable(Exception error) => new(
        $"The configured '{Provider}' model client failed: {error.Message}", error);
}
