using OfficeAgent.Abstractions;
using OfficeAgent.Core;
using OfficeAgent.Core.DocumentProviders;

namespace OfficeAgent.Studio;

/// <summary>Shared create/cleanup behavior for every composer.</summary>
internal static class ComposerSession
{
    internal static async Task<string> RunAsync(
        OfficeAgentClient client,
        string connection,
        string fileName,
        Func<string, Task> compose,
        CancellationToken cancellationToken)
    {
        ProviderApplyResult created;
        try
        {
            created = await client.CreateAsync(connection, fileName, cancellationToken: cancellationToken);
        }
        catch (DocumentProviderException error)
        {
            throw ProviderFailure("create", fileName, error);
        }

        if (!created.Committed || created.Document is null)
            throw ReportFailure("create", created.Report);

        var id = created.Document.ItemId;
        try
        {
            await compose(id);
            return id;
        }
        catch
        {
            // Remove the provider registration for a failed composition. The output
            // transaction owns the temporary file itself and removes that separately.
            try
            {
                await client.RemoveAsync(connection, id, CancellationToken.None);
            }
            catch (Exception)
            {
                // Preserve the composition failure; cleanup is best effort.
            }

            throw;
        }
    }

    internal static StudioException ReportFailure(string operation, ChangeReport report) => new(
        $"Document composition failed during '{operation}': " +
        string.Join("; ", report.Errors.Select(error => $"{error.Code}: {error.Message}")),
        "No completed output was published. Retry the run; if it repeats, inspect the plan and OfficeAgent version.");

    internal static StudioException ProviderFailure(
        string operation, string fileName, DocumentProviderException error) => new(
        $"Could not {operation} '{fileName}': {error.Message}",
        error.Code == ProviderErrorCode.AlreadyExists
            ? "Choose another output name or remove the existing file."
            : "Check OFFICEAGENT_STUDIO_OUTPUT and the directory's permissions.",
        error);
}

/// <summary>
/// Publishes a composed file only after every OfficeAgent operation has succeeded.
/// </summary>
internal sealed class OutputTransaction
{
    private readonly OfficeAgentClient _client;
    private readonly string _connection;
    private readonly string _outputRoot;

    internal OutputTransaction(OfficeAgentClient client, string outputRoot, string connection = "output")
    {
        _client = client;
        _connection = connection;
        _outputRoot = Path.GetFullPath(outputRoot);
    }

    internal async Task<string> ComposeAsync(
        string finalName,
        Func<string, CancellationToken, Task<string>> compose,
        CancellationToken cancellationToken = default)
    {
        var extension = Path.GetExtension(finalName);
        if (extension.Length == 0 || !string.Equals(Path.GetFileName(finalName), finalName, StringComparison.Ordinal))
            throw new StudioException($"Output name '{finalName}' must be a bare filename with an extension.");

        var finalPath = Path.Combine(_outputRoot, finalName);
        if (File.Exists(finalPath) || Directory.Exists(finalPath))
            throw new StudioException(
                $"A document named '{finalName}' already exists in the output directory.",
                "Choose another output name or remove the existing file.");

        var partialName = $"partial-{Guid.NewGuid():N}{extension}";
        var partialPath = Path.Combine(_outputRoot, partialName);
        string? partialId = null;

        try
        {
            partialId = await compose(partialName, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            ProviderApplyResult published;
            try
            {
                published = await _client.CommitAsync(
                    _connection,
                    partialId,
                    new DocumentPlan { Operations = Array.Empty<PlanOperation>() },
                    new SaveDocumentOptions { Mode = SaveMode.NewDocument, NewName = finalName },
                    CancellationToken.None);
            }
            catch (DocumentProviderException error)
            {
                throw ComposerSession.ProviderFailure("publish", finalName, error);
            }

            if (!published.Committed || published.Document is null)
                throw ComposerSession.ReportFailure("publish", published.Report);

            return published.Document.ItemId;
        }
        finally
        {
            if (partialId is not null)
            {
                try
                {
                    await _client.RemoveAsync(_connection, partialId, CancellationToken.None);
                }
                catch (Exception)
                {
                    // A stale temporary registration is harmless and must not hide whether
                    // publishing the completed output succeeded.
                }
            }

            TryDeleteOwnedPartial(partialPath);
        }
    }

    private void TryDeleteOwnedPartial(string path)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            var root = _outputRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return;
            if (!Path.GetFileName(fullPath).StartsWith("partial-", StringComparison.Ordinal)) return;
            File.Delete(fullPath);
        }
        catch (Exception)
        {
            // The completed output, when there is one, is already published. Cleanup does
            // not change that outcome and should not turn success into a reported failure.
        }
    }
}

internal static class OutputNames
{
    internal static string NewStamp()
    {
        var suffix = Guid.NewGuid().ToString("N")[..10];
        return $"{DateTimeOffset.Now:yyyyMMdd-HHmmssfff}-{suffix}";
    }
}
