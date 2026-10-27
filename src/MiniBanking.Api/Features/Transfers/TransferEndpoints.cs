using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using MiniBanking.Api.Common;
using MiniBanking.Api.Features.Idempotency;
using MiniBanking.Api.Features.Transactions;
using MiniBanking.Domain.Accounts;
using MiniBanking.Infrastructure.Persistence;

namespace MiniBanking.Api.Features.Transfers;

/// <param name="SourceAccountId">One of your accounts.</param>
/// <param name="DestinationAccountNumber">Any account number in the same currency, e.g. MB1234567890123456.</param>
/// <param name="Amount">Positive, at most two decimals.</param>
/// <param name="Description">Optional note shown to both sides.</param>
internal sealed record TransferRequest(Guid SourceAccountId, string DestinationAccountNumber, decimal Amount, string? Description = null);

internal static class TransferEndpoints
{
    public static IEndpointRouteBuilder MapTransferEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/transfers", Transfer)
            .WithTags("Transfers")
            .WithSummary("Send money from one of your accounts to any account in the same currency")
            .RequireAuthorization()
            .RequireIdempotencyKey()
            .ProducesMoneyProblems();

        return app;
    }

    private static Task<Results<Created<TransactionResponse>, ValidationProblem>> Transfer(
        TransferRequest request, ClaimsPrincipal user, MoneyMovementService money, BankingDbContext db, CancellationToken ct)
    {
        var destination = AccountNumbers.Normalize(request.DestinationAccountNumber);
        var validation = new Validation()
            .Require(request.SourceAccountId != Guid.Empty, "sourceAccountId", "Source account id is required.")
            .Require(AccountNumbers.IsValid(destination), "destinationAccountNumber",
                "Destination must be an account number like MB1234567890123456 with a valid check digit.");

        return TransactionEndpoints.Move(
            new MoneyRequest(request.Amount, request.Description), db, user, ct,
            (customerId, amount) => money.TransferAsync(customerId, request.SourceAccountId, destination, amount, request.Description, ct),
            validation);
    }
}
