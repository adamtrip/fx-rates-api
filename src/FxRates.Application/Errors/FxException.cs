namespace FxRates.Application.Errors;

/// <summary>Messages must be safe for API callers and omit connection strings, request URLs, and secrets.</summary>
public sealed class FxException(ErrorKind kind, string message) : Exception(message)
{
    public ErrorKind Kind { get; } = kind;
}
