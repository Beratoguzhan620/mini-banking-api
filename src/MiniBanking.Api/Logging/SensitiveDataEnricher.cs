using Serilog.Core;
using Serilog.Events;

namespace MiniBanking.Api.Logging;

/// <summary>
/// Last line of defence for log output: any property whose name says it holds a secret or personal data
/// is masked before it reaches a sink, whoever wrote the log call.
/// </summary>
internal sealed class SensitiveDataEnricher : ILogEventEnricher
{
    private static readonly HashSet<string> Secrets = new(StringComparer.OrdinalIgnoreCase)
    {
        "Password", "Token", "AccessToken", "Authorization", "SigningKey", "ConnectionString", "IdempotencyKey",
    };

    private static readonly HashSet<string> Emails = new(StringComparer.OrdinalIgnoreCase) { "Email" };

    private static readonly HashSet<string> AccountNumbers = new(StringComparer.OrdinalIgnoreCase)
    {
        "AccountNumber", "SourceAccountNumber", "DestinationAccountNumber",
    };

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        foreach (var (name, value) in logEvent.Properties.ToList())
        {
            var masked = name switch
            {
                _ when Secrets.Contains(name) => Masking.Hidden,
                _ when Emails.Contains(name) => Masking.Email(AsString(value)),
                _ when AccountNumbers.Contains(name) => Masking.AccountNumber(AsString(value)),
                _ => null,
            };

            if (masked is not null)
            {
                logEvent.AddOrUpdateProperty(new LogEventProperty(name, new ScalarValue(masked)));
            }
        }
    }

    private static string? AsString(LogEventPropertyValue value) =>
        value is ScalarValue { Value: { } scalar } ? scalar.ToString() : null;
}
