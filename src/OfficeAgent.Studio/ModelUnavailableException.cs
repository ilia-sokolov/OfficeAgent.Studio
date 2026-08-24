namespace OfficeAgent.Studio;

/// <summary>
/// The model could not be reached, or could not answer at all.
/// </summary>
/// <remarks>
/// Distinct from a reply that arrived and was unusable. That one is worth retrying - models
/// occasionally wrap JSON in a sentence, and a second attempt usually lands. This one is
/// not: a CLI that is missing, unauthenticated, out of quota or timing out will fail the
/// same way three times and then be reported as though the model had written something bad.
/// Separating the two is what lets the run say "check your setup" instead.
/// </remarks>
public sealed class ModelUnavailableException : InvalidOperationException
{
    public ModelUnavailableException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}
