using System.Text.Json;
using MiniBanking.Api.Common;
using MiniBanking.Domain.Audit;
using MiniBanking.Infrastructure.Persistence;

namespace MiniBanking.Api.Features.Audit;

/// <summary>
/// Adds audit entries to the current unit of work, so an entry is saved together with the change it
/// describes and disappears with it if that change is rolled back.
/// </summary>
internal sealed class AuditTrail(BankingDbContext db, IHttpContextAccessor http, TimeProvider clock)
{
    public void Record(string action, string targetType, object? targetId, AuditOutcome outcome, object? details = null, Guid? actorId = null)
    {
        var context = http.HttpContext;
        actorId ??= context?.User.Identity?.IsAuthenticated == true ? context.User.CustomerId() : null;

        db.AuditEntries.Add(AuditEntry.Create(
            clock.GetUtcNow(),
            actorId,
            action,
            targetType,
            targetId?.ToString(),
            outcome,
            details is null ? null : JsonSerializer.Serialize(details, JsonSerializerOptions.Web),
            context?.TraceIdentifier));
    }
}
