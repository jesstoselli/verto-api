# Verto API

Backend do Verto: auth + sync offline-first. Design em `docs/superpowers/specs/2026-10-15-verto-design.md`.

## Rodar local

```bash
docker compose -f docker-compose.dev.yml up -d
dotnet run --project src/Verto.Api --launch-profile http
curl http://localhost:5080/v1/health
```

## Testes

Precisam do Docker rodando (Testcontainers sobe um Postgres por classe de teste).

```bash
dotnet test
```
