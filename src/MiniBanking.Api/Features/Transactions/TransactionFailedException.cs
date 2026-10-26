using MiniBanking.Domain;

namespace MiniBanking.Api.Features.Transactions;

/// <summary>A money movement was rejected and recorded as failed; the client gets its id for reference.</summary>
internal sealed class TransactionFailedException(Guid transactionId, DomainException reason)
    : DomainException(reason.Code, reason.Message)
{
    public Guid TransactionId { get; } = transactionId;
}
