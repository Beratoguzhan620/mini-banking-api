using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using MiniBanking.Api.Common;
using MiniBanking.Api.Features.Idempotency;
using MiniBanking.Api.Features.Transactions;
using MiniBanking.Domain.Accounts;
using MiniBanking.Infrastructure.Persistence;

namespace MiniBanking.Api.Features.Transfers;

internal sealed record TransferRequest(Guid? SourceAccountId, string? DestinationAccountNumber, decimal? Amount, string? Description);

internal static class TransferEndpoints
{
    public static IEndpointRouteBuilder MapTransferEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/transfers", Transfer)
            .WithTags("Transfers")
            .WithSummary("Send money from one of your accounts to any account in the same currency")
            .RequireAuthorization()
            .RequireIdempotencyKey();

        return app;
    }

    private static Task<Results<Created<TransactionResponse>, ValidationProblem>> Transfer(
        TransferRequest request, ClaimsPrincipal user, MoneyMovementService money, BankingDbContext db, CancellationToken ct)
    {
        var destination = AccountNumbers.Normalize(request.DestinationAccountNumber);
        var validation = new Validation()
            .Require(request.SourceAccountId is { } id && id != Guid.Empty, "sourceAccountId", "Source account id is required.")
            .Require(AccountNumbers.IsValid(destination), "destinationAccountNumber",
                "Destination must be an account number like MB1234567890123456 with a valid check digit.");

        return TransactionEndpoints.Move(
            new MoneyRequest(request.Amount, request.Description), db, user, ct,
            (customerId, amount) => money.TransferAsync(customerId, request.SourceAccountId!.Value, destination, amount, request.Description, ct),
            validation);
    }
}
