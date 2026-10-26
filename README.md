# Mini Banking API

A small banking API built for correctness: double-entry ledger, idempotent requests and transfers
that stay consistent under concurrency. ASP.NET Core, EF Core and PostgreSQL.

> Work in progress — see [docs/ROADMAP.md](docs/ROADMAP.md) for the plan and current stage.

## Run it

```bash
docker compose up --build
```

Then open http://localhost:8080/scalar for the API reference.

## Development

```bash
dotnet user-secrets --project src/MiniBanking.Api set "ConnectionStrings:Banking" "Host=localhost;Database=mini_banking;Username=...;Password=..."
dotnet run --project src/MiniBanking.Api
dotnet test   # integration tests need Docker (Testcontainers)
```

## License

[MIT](LICENSE) · Berat Oğuzhan Turğut · [GitHub](https://github.com/Beratoguzhan620)
