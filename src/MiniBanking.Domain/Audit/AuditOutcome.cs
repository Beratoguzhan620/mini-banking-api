namespace MiniBanking.Domain.Audit;

public enum AuditOutcome
{
    Succeeded,

    /// <summary>A business rule said no (insufficient funds, wrong password).</summary>
    Rejected,

    /// <summary>Something broke; the operation was rolled back.</summary>
    Failed,
}
