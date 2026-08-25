using Microsoft.Extensions.AI;
using OfficeAgent.Studio;
using Xunit;

namespace OfficeAgent.Studio.Tests;

public class ModelClientFactoryTests
{
    [Fact]
    public void DefaultsToClaude()
    {
        using var client = ModelClientFactory.Create(_ => null);

        var configured = Assert.IsType<ProviderChatClient>(client);
        Assert.Equal("claude", configured.Provider);
        Assert.NotNull(client.GetService(typeof(ClaudeCodeChatClient)));
    }

    [Theory]
    [InlineData("azure-foundry")]
    [InlineData("foundry")]
    [InlineData("azure-openai")]
    [InlineData(" AZURE-FOUNDRY ")]
    public void CreatesAzureFoundryClientFromConfiguration(string provider)
    {
        using var client = ModelClientFactory.Create(Settings(
            (ModelClientFactory.ProviderVariable, provider),
            ("AZURE_OPENAI_ENDPOINT", "https://example.openai.azure.com/"),
            ("AZURE_OPENAI_DEPLOYMENT", "studio-model"),
            ("AZURE_OPENAI_API_KEY", "not-a-real-key")));

        var configured = Assert.IsType<ProviderChatClient>(client);
        Assert.Equal("azure-foundry", configured.Provider);
    }

    [Fact]
    public void AcceptsModelAsDeploymentAliasAndEntraAuthentication()
    {
        using var client = ModelClientFactory.Create(Settings(
            (ModelClientFactory.ProviderVariable, "azure-foundry"),
            ("AZURE_OPENAI_ENDPOINT", "https://example.openai.azure.com/"),
            ("AZURE_OPENAI_MODEL", "studio-model")));

        Assert.IsType<ProviderChatClient>(client);
    }

    [Theory]
    [InlineData("AZURE_OPENAI_ENDPOINT")]
    [InlineData("AZURE_OPENAI_DEPLOYMENT")]
    public void ReportsMissingFoundryConfiguration(string missing)
    {
        var values = new Dictionary<string, string?>
        {
            [ModelClientFactory.ProviderVariable] = "azure-foundry",
            ["AZURE_OPENAI_ENDPOINT"] = "https://example.openai.azure.com/",
            ["AZURE_OPENAI_DEPLOYMENT"] = "studio-model"
        };
        values.Remove(missing);

        var error = Assert.Throws<ArgumentException>(() =>
            ModelClientFactory.Create(name => values.GetValueOrDefault(name)));

        Assert.Contains(missing, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsInvalidFoundryEndpoint()
    {
        var error = Assert.Throws<ArgumentException>(() =>
            ModelClientFactory.Create(Settings(
                (ModelClientFactory.ProviderVariable, "azure-foundry"),
                ("AZURE_OPENAI_ENDPOINT", "not-a-url"),
                ("AZURE_OPENAI_DEPLOYMENT", "studio-model"))));

        Assert.Contains("absolute HTTP or HTTPS", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsUnknownProvider()
    {
        var error = Assert.Throws<ArgumentException>(() =>
            ModelClientFactory.Create(Settings((ModelClientFactory.ProviderVariable, "mystery"))));

        Assert.Contains("claude or azure-foundry", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsInvalidClaudeTimeout()
    {
        var error = Assert.Throws<ArgumentException>(() =>
            ModelClientFactory.Create(Settings(
                ("OFFICEAGENT_STUDIO_CLAUDE_TIMEOUT_SECONDS", "not-a-number"))));

        Assert.Contains("positive number", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TranslatesProviderSpecificFailures()
    {
        using var client = new ProviderChatClient("azure-foundry", new ThrowingChatClient());

        var error = await Assert.ThrowsAsync<ModelUnavailableException>(() =>
            client.GetResponseAsync([new ChatMessage(ChatRole.User, "brief")]));

        Assert.Contains("azure-foundry", error.Message, StringComparison.Ordinal);
        Assert.IsType<HttpRequestException>(error.InnerException);
    }

    private static Func<string, string?> Settings(params (string Name, string Value)[] values)
    {
        var settings = values.ToDictionary(pair => pair.Name, pair => (string?)pair.Value);
        return name => settings.GetValueOrDefault(name);
    }

    private sealed class ThrowingChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw new HttpRequestException("connection failed");

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }
}
