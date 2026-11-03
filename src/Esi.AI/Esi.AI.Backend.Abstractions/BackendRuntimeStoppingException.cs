namespace Esi.AI.Backend.Abstractions;

/// <summary>Indicates that inference was interrupted because its backend runtime is stopping or being replaced.</summary>
public sealed class BackendRuntimeStoppingException : InvalidOperationException
{
    /// <summary>Initializes a new instance of the <see cref="BackendRuntimeStoppingException"/> class.</summary>
    /// <param name="message">A message describing the runtime lifecycle interruption.</param>
    /// <param name="innerException">The transport or cancellation exception that caused the interruption.</param>
    public BackendRuntimeStoppingException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
    }
}
