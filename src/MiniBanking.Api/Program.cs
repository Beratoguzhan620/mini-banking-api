using System.Text.Encodings.Web;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using MiniBanking.Api.Common;
using MiniBanking.Api.Features.Accounts;
using MiniBanking.Api.Features.Audit;
using MiniBanking.Api.Features.Auth;
using MiniBanking.Api.Features.Idempotency;
using MiniBanking.Api.Features.Operations;
using MiniBanking.Api.Features.Transactions;
using MiniBanking.Api.Features.Transfers;
using MiniBanking.Api.Logging;
using MiniBanking.Infrastructure;
using MiniBanking.Infrastructure.Persistence;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Core;

var builder = WebApplication.CreateBuilder(args);

// Picked up by ReadFrom.Services below, so every log event is masked before any sink sees it.
builder.Services.AddSingleton<ILogEventEnricher, SensitiveDataEnricher>();

builder.Services.AddSerilog((services, logger) => logger
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext());

var connectionString = builder.Configuration.GetConnectionString("Banking");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "ConnectionStrings:Banking is not configured. Use 'dotnet user-secrets' or the ConnectionStrings__Banking environment variable.");
}

builder.Services.AddBankingPersistence(connectionString);
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    // Keep Turkish characters readable in responses instead of \u escapes.
    options.SerializerOptions.Encoder = JavaScriptEncoder.Create(UnicodeRanges.All);
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    // Amounts are JSON numbers; "10" as a string is refused rather than guessed.
    options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
});

builder.Services.AddBankingAuth(builder.Configuration);
builder.Services.AddScoped<MoneyMovementService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<AuditTrail>();
builder.Services.Configure<OperationsOptions>(builder.Configuration.GetSection(OperationsOptions.Section));

builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
    context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier);
builder.Services.AddExceptionHandler<DomainExceptionHandler>();

builder.Services.AddOpenApi(options => options
    .AddDocumentTransformer<DocumentInfoTransformer>()
    .AddDocumentTransformer<BearerSecuritySchemeTransformer>()
    .AddOperationTransformer<IdempotencyHeaderTransformer>());
builder.Services.AddHealthChecks()
    .AddDbContextCheck<BankingDbContext>("database", tags: ["ready"]);

var app = builder.Build();

// First, so the id is on every log line, response and audit entry of the request.
app.UseMiddleware<CorrelationIdMiddleware>();
// Request logging sits outside the exception handler so it records the final status code.
app.UseSerilogRequestLogging();
app.UseAuthentication();
// Outside the exception handler, so error responses are stored and replayed too.
app.UseMiddleware<IdempotencyMiddleware>();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthorization();

app.MapOpenApi();
app.MapScalarApiReference(options => options
    .WithTitle("Mini Banking API")
    .WithTheme(ScalarTheme.Default)
    .ForceLightMode()
    .HideDarkModeToggle()
    .DisableDefaultFonts()
    .WithCustomCss(ScalarStyle.Css)
    .HideDeveloperTools()
    .HideClientButton()
    .DisableAgent()
    .DisableMcp()
    .DisableTelemetry());
app.MapGet("/", () => Results.Redirect("/scalar")).ExcludeFromDescription();

app.MapAuthEndpoints();
app.MapAccountEndpoints();
app.MapTransactionEndpoints();
app.MapTransferEndpoints();
app.MapOperationsEndpoints();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

if (app.Configuration.GetValue("Database:MigrateOnStartup", false))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<BankingDbContext>().Database.MigrateAsync();
}

await app.RunAsync();

public partial class Program;
