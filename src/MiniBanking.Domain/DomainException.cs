namespace MiniBanking.Domain;

/// <summary>
/// A business rule was broken. <see cref="Code"/> is a stable, machine-readable value that the API
/// returns to clients; the message is for humans and may change.
/// </summary>
public class DomainException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
