namespace MiniBanking.Domain.Audit;

/// <summary>
/// Who did what to which resource, and how it ended. Rows are never updated or deleted; the database
/// enforces that with a trigger. Details hold identifiers and outcomes only, never secrets.
/// </summary>
public sealed class AuditEntry
{
    public const int ActionMaxLength = 64;

    private AuditEntry()
    {
        Action = string.Empty;
        TargetType = string.Empty;
    }

    public long Id { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    /// <summary>The customer who acted; <c>null</c> when unknown (a login attempt for a missing email).</summary>
    public Guid? ActorId { get; private set; }

    /// <summary>Dotted name such as <c>transfer.completed</c> or <c>auth.login_failed</c>.</summary>
    public string Action { get; private set; }

    public string TargetType { get; private set; }

    public string? TargetId { get; private set; }

    public AuditOutcome Outcome { get; private set; }

    /// <summary>JSON object with extra facts.</summary>
    public string? Details { get; private set; }

    public string? CorrelationId { get; private set; }

    public static AuditEntry Create(
        DateTimeOffset occurredAt, Guid? actorId, string action, string targetType, string? targetId,
        AuditOutcome outcome, string? details, string? correlationId) => new()
    {
        OccurredAt = occurredAt,
        ActorId = actorId,
        Action = action,
        TargetType = targetType,
        TargetId = targetId,
        Outcome = outcome,
        Details = details,
        CorrelationId = correlationId,
    };
}
