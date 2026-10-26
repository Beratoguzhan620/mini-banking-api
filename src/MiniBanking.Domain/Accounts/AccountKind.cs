namespace MiniBanking.Domain.Accounts;

public enum AccountKind
{
    /// <summary>Belongs to a customer; its balance can never go below zero.</summary>
    Customer,

    /// <summary>The bank's own side of deposits and withdrawals, one per currency.</summary>
    System,
}
