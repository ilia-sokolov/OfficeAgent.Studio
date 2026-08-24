namespace OfficeAgent.Studio;

/// <summary>
/// A failure this sample understood well enough to explain.
/// </summary>
/// <remarks>
/// The distinction that matters is between a problem the program can describe - the model
/// is unreachable, the plan came back unusable, a file of that name already exists - and a
/// genuine defect. The first should reach the reader as a sentence and the second as a
/// stack trace. Throwing this type is how a call site says which one it is.
/// </remarks>
public sealed class StudioException : Exception
{
    public StudioException(string message, string? hint = null, Exception? inner = null)
        : base(message, inner) => Hint = hint;

    /// <summary>What to do about it, when there is something specific to suggest.</summary>
    public string? Hint { get; }
}
