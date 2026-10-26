# Roadmap

A small banking API whose whole point is correctness: money never appears or disappears, the same
request is never applied twice, and two transfers racing on the same account never corrupt its balance.

## Scope

- Customer registration and login (JWT).
- Accounts: open (one currency per account), list, get, freeze, unfreeze, close (balance must be zero).
- Simulated deposits and withdrawals.
- Transfers between accounts of the same currency, by destination account number.
- Transaction history with filters and keyset pagination.
- Idempotency keys on every money-moving request.
- Audit log for every operation, including rejected ones.
- Reconciliation check: cached balances against the ledger.

Out of scope: currency conversion, interest, fees, cards, real payment rails.

## Data model

| Table | Columns | Notes |
|---|---|---|
| `customers` | id, email (unique, normalised), full_name, password_hash, created_at | |
| `accounts` | id, customer_id, number (unique), currency, kind, balance_minor, status, opened_at, closed_at, xmin | `kind` is Customer or System; balance in minor units (`bigint`); check constraint keeps customer balances non-negative |
| `bank_transactions` | id, type, status, currency, amount_minor, source_account_id, destination_account_id, initiated_by, description, failure_reason, created_at | Failed attempts are stored too |
| `ledger_entries` | id, transaction_id, account_id, amount_minor (signed), balance_after_minor, created_at | Entries of one transaction always sum to zero |
| `idempotency_keys` | customer_id + key (unique), request_hash, status, response_status, response_body, created_at | |
| `audit_log` | id, occurred_at, actor_id, action, target_type, target_id, outcome, details, correlation_id | Append-only, no secrets |

Deposits and withdrawals are balanced against a per-currency system account. System accounts only
receive ledger entries; their cached balance is not maintained, so they never become a hot row.

## Architecture

```
src/MiniBanking.Domain          Money, currencies, account rules, domain errors (no dependencies)
src/MiniBanking.Infrastructure  EF Core + Npgsql, configurations, migrations
src/MiniBanking.Api             Minimal API endpoints, auth, application services, ProblemDetails
tests/MiniBanking.UnitTests     Domain rules
tests/MiniBanking.IntegrationTests  Real PostgreSQL via Testcontainers, HTTP-level tests
```

## Key decisions

- **Money as `long` minor units.** No floating point, no rounding drift, exact sums in SQL.
- **Currency per account.** Transfers between different currencies are rejected; no FX.
- **Pessimistic locking for transfers.** Both accounts are locked with `SELECT … FOR UPDATE` in id
  order inside one transaction, so A→B and B→A at the same time cannot deadlock. `xmin` is mapped as
  a concurrency token as a second line of defence.
- **Balance cached and derivable.** The account row holds the balance for fast reads; the ledger is
  the source of truth and reconciliation compares the two.
- **Idempotency.** `Idempotency-Key` header is required on deposits, withdrawals and transfers. Same
  key + same body replays the stored response; same key + different body returns 422; concurrent
  duplicates are resolved by a unique constraint.
- **Failed transfers are visible.** The money movement is rolled back, then a `Failed` transaction
  and an audit entry are written in a separate transaction.
- **Not-found over forbidden.** Someone else's account answers 404, so account ids cannot be probed.

## Stages

| # | Branch | Content |
|---|---|---|
| 01 | `stage/01-skeleton` | Solution layout, warnings as errors, central package versions, Docker Compose, health checks, Scalar, ProblemDetails, CI (build, tests, compose smoke test, pending migration check) |
| 02 | `stage/02-accounts` | Register/login with JWT, open/list/get/freeze/unfreeze/close accounts, ownership checks, migration, tests |
| 03 | `stage/03-ledger` | Deposits and withdrawals on a double-entry ledger, insufficient funds, history with filters and keyset pagination |
| 04 | `stage/04-transfers` | Locked transfers, rollback, failed transfer records, concurrency tests (parallel transfers from one account, A↔B in both directions) |
| 05 | `stage/05-idempotency` | Idempotency keys: replay, mismatched body, concurrent duplicates |
| 06 | `stage/06-audit` | Audit log, reconciliation endpoint, correlation ids, log masking |
| 07 | `stage/07-docs` | README (EN + TR), architecture diagram, design decisions, Scalar and terminal GIFs |

## Later

- Daily withdrawal limits.
- Outbox for transaction events (Notification Service integration).
- Load test script (k6) with published numbers.
