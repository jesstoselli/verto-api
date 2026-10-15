# Verto API — deploy na VPS (runbook)

Executar na ordem. A VPS e o Postgres são os mesmos do BillFolder, então cada passo tem uma verificação.

**Antes de colar qualquer bloco, olhe o prompt.** `>` (zsh) é o seu Mac; `deploy@billfolder-prod` é a VPS. Os passos dizem onde cada comando roda.

Valores fixos desta instalação:

| O quê | Valor |
|---|---|
| IP da VPS | `178.104.160.8` |
| Usuário SSH | `deploy` (tem `sudo` completo, com senha) |
| Chave SSH | `~/.ssh/billfolder_deploy_key` |
| Pasta do compose | `/opt/billfolder` (arquivo `compose.yml`, rede `billfolder`) |
| Host da API | `verto-api.billfolder.app` |
| Imagem | `ghcr.io/jesstoselli/verto-api:latest` |

Entrar na VPS (do Mac):

```bash
ssh -i ~/.ssh/billfolder_deploy_key deploy@178.104.160.8
```

## 1. Repo no GitHub e secrets (Mac)

Cadastre os secrets **antes** do primeiro push: o `gh repo create ... --push` já dispara o deploy, e sem secrets o job "Deploy to VPS" falha no `ssh-keyscan`. Se isso acontecer, é só rodar de novo o job depois (passo 6).

```bash
cd ~/Repos/Verto
gh repo create verto-api --private --source=. --remote=origin
gh secret set VPS_HOST --body "178.104.160.8"
gh secret set VPS_USER --body "deploy"
gh secret set VPS_SSH_KEY < ~/.ssh/billfolder_deploy_key
```

Os secrets do GitHub não podem ser lidos de volta (nem os do BillFolder): os valores vêm da tabela acima. Para conferir a chave antes: `ssh -i ~/.ssh/billfolder_deploy_key -o BatchMode=yes deploy@178.104.160.8 whoami` deve responder `deploy`.

## 2. DNS na Cloudflare

Painel → `billfolder.app` → **DNS → Records → Add record**:

| Campo | Valor |
|---|---|
| Type | `A` |
| Name | `verto-api` |
| IPv4 | `178.104.160.8` |
| Proxy status | **DNS only** (nuvem cinza, igual ao registro `api`) |

DNS only é o que deixa o `certbot` (passo 5) emitir o certificado direto na VPS.

Verificar (Mac): `dig +short verto-api.billfolder.app` → `178.104.160.8`.

## 3. Banco e role (VPS)

Rode **uma vez só**: repetir duplica as linhas do `.env` e gera uma senha que não bate com a do banco.

```bash
cd /opt/billfolder
docker compose ps postgres
VERTO_DB_PASSWORD=$(openssl rand -hex 24)
echo "VERTO_DB_PASSWORD=$VERTO_DB_PASSWORD" >> .env
echo "VERTO_JWT_KEY=$(openssl rand -hex 48)" >> .env
echo "VERTO_ALLOW_SIGNUP=true" >> .env
docker compose exec -T postgres psql -U billfolder -d postgres <<SQL
CREATE ROLE verto LOGIN PASSWORD '$VERTO_DB_PASSWORD';
CREATE DATABASE verto OWNER verto;
REVOKE CONNECT ON DATABASE billfolder FROM PUBLIC;
SQL
```

Verificar:
- `docker compose exec -T postgres psql -U verto -d billfolder -c 'select 1'` **falha** com `permission denied for database "billfolder"`.
- `curl -sS -o /dev/null -w '%{http_code}\n' https://api.billfolder.app/v1/health` → `200`.

Efeito colateral conhecido: `postgres` e `api` do BillFolder usam `env_file: .env`, então o Compose passa a ver a config deles como alterada. Nada acontece sozinho, mas o próximo `docker compose up` sem `--no-deps` recria o container do Postgres (segundos fora do ar; os dados ficam no volume). Para escolher o momento: `docker compose up -d postgres api`.

## 4. Serviço no compose (VPS)

```bash
cd /opt/billfolder
cp compose.yml compose.yml.bak-$(date +%F)
nano compose.yml
```

Cole **só** o bloco abaixo dentro de `services:`, logo depois do fim do serviço `api:` (a linha `retries: 3` do healthcheck dele), com uma linha em branco antes e outra depois. `verto-api:` fica com 2 espaços, alinhado com `api:` e `postgres:`. Não copie nenhuma linha que já existe no arquivo.

```yaml
  verto-api:
    image: ghcr.io/jesstoselli/verto-api:latest
    container_name: verto-api
    restart: unless-stopped
    # Caps Verto's memory so an Argon2 login burst can't push the host into OOM-killing BillFolder.
    mem_limit: 512m
    depends_on:
      postgres:
        condition: service_healthy
    ports:
      - "127.0.0.1:8081:8080"
    environment:
      ConnectionStrings__Postgres: "Host=postgres;Database=verto;Username=verto;Password=${VERTO_DB_PASSWORD}"
      Jwt__Key: "${VERTO_JWT_KEY}"
      Jwt__Issuer: "verto"
      Jwt__Audience: "verto-app"
      Jwt__RefreshTokenDays: "90"
      Auth__AllowSignup: "${VERTO_ALLOW_SIGNUP:-false}"
      Database__MigrateOnStartup: "true"
    networks:
      - billfolder
```

Sem `networks: - billfolder` o Verto não enxerga o `postgres`.

Verificar:

```bash
docker compose config --quiet && echo OK
docker compose config --services
```

`OK` e a lista `postgres`, `api`, `verto-api`. Erro `mapping key "..." already defined at line N` = linha colada em duplicidade: veja com `cat -n compose.yml | sed -n '30,80p'` e apague a cópia (`sed -i 'N,Md' compose.yml`). Se embolar: `cp compose.yml.bak-$(date +%F) compose.yml`.

Não rode `docker compose up` aqui: a imagem só existe no GHCR depois do passo 6.

## 5. nginx + TLS (VPS)

Precisa de `sudo` com a senha do `deploy`. Confira com `sudo -v && echo SUDO_OK`.

```bash
ls /etc/nginx/sites-enabled && certbot --version
```

Esperado: `api.billfolder.app` (e `default`) e `certbot 2.x`.

```bash
sudo tee /etc/nginx/sites-available/verto-api.billfolder.app > /dev/null <<'NGINX'
server {
    listen 80;
    server_name verto-api.billfolder.app;

    # A full sync push (500 records x 64 KB) is ~32 MB; nginx's 1 MB default would 413 it.
    client_max_body_size 40m;

    location / {
        proxy_pass http://127.0.0.1:8081;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }
}
NGINX
sudo ln -s /etc/nginx/sites-available/verto-api.billfolder.app /etc/nginx/sites-enabled/
sudo nginx -t && sudo systemctl reload nginx
sudo certbot --nginx -d verto-api.billfolder.app
```

Esperado: `syntax is ok`, `test is successful` e `Successfully deployed certificate`. Até o passo 6, o endereço responde 502 (o container ainda não existe).

### Se a senha do `sudo` do `deploy` não funcionar

O root só aceita chave por SSH e o console web da Hetzner costuma errar caracteres (teclado US, sem colar). O caminho que funciona:

1. Hetzner Console → **Servers → billfolder-prod → Rescue → Reset root password** (só esse botão; "Enable rescue & power cycle", Power, Rebuild e Delete reiniciam ou apagam a VPS). Guarde a senha nova.
2. No Mac, entre como `deploy` por SSH e rode `su -`, colando a senha nova do root.
3. Como root: `passwd deploy` → defina e guarde a senha do `deploy`. `exit`.
4. Como `deploy`: `sudo -v && echo SUDO_OK`.

## 6. Deploy (Mac)

Cada push na `main` testa, publica a imagem e sobe o container (`docker compose up -d --no-deps verto-api`).

```bash
cd ~/Repos/Verto
git push -u origin main
gh run watch
```

Se o deploy falhou antes dos secrets existirem, rode só o job que falhou: `gh run rerun <run-id> --failed` (o id aparece em `gh run list`).

Esperado: os dois jobs verdes, `✓ Deploy complete`. Se o pull da imagem falhar com `denied`, a VPS não tem permissão no pacote novo: na VPS, `docker login ghcr.io` com um token `read:packages`.

Verificar: `curl -fsS https://verto-api.billfolder.app/v1/health` → `"status":"ok"`.

## 7. Conta da dona e cadastro fechado

Criar a conta (Mac). Pede email e senha sem deixar a senha no histórico; deve imprimir `200`:

```bash
read "VERTO_EMAIL?Email: "; read -s "VERTO_PASS?Senha (min. 8): "; echo; jq -n --arg e "$VERTO_EMAIL" --arg p "$VERTO_PASS" '{email:$e,password:$p,displayName:"Jess"}' | curl -sS -o /dev/null -w '%{http_code}\n' -X POST https://verto-api.billfolder.app/v1/auth/signup -H "Content-Type: application/json" --data @-; unset VERTO_PASS
```

Fechar o cadastro (Mac):

```bash
ssh -i ~/.ssh/billfolder_deploy_key deploy@178.104.160.8 'cd /opt/billfolder && sed -i "s/^VERTO_ALLOW_SIGNUP=true/VERTO_ALLOW_SIGNUP=false/" .env && docker compose up -d --no-deps verto-api'
```

Verificar: deve dar `403`.

```bash
curl -sS -o /dev/null -w '%{http_code}\n' -X POST https://verto-api.billfolder.app/v1/auth/signup -H "Content-Type: application/json" -d '{"email":"teste@example.com","password":"qualquer-coisa-123","displayName":"X"}'
```

## 8. Backup diário (VPS)

O BillFolder já faz backup às 03:00 pelo crontab do `deploy` (`/opt/billfolder/scripts/backup.sh`). O do Verto roda às 03:15, guarda 14 dias e registra tudo (inclusive erros do `pg_dump`) em `/opt/billfolder/logs/backup-verto.log`:

```bash
(crontab -l | grep -v 'verto-'; echo '15 3 * * * { cd /opt/billfolder && docker compose exec -T postgres pg_dump -U billfolder verto | gzip > /opt/billfolder/backups/verto-$(date +\%F).sql.gz && find /opt/billfolder/backups -name "verto-*.sql.gz" -mtime +14 -delete; } >> /opt/billfolder/logs/backup-verto.log 2>&1') | crontab -
crontab -l | tail -2
```

O `grep -v 'verto-'` substitui uma linha anterior do Verto em vez de duplicar; a linha do BillFolder fica intacta.

Testar agora:

```bash
cd /opt/billfolder && docker compose exec -T postgres pg_dump -U billfolder verto | gzip > backups/verto-$(date +%F).sql.gz && ls -la backups/verto-*
gunzip -c backups/verto-$(date +%F).sql.gz | grep -c "CREATE TABLE"
```

Esperado: um arquivo `verto-AAAA-MM-DD.sql.gz` e o número de tabelas do Verto (4 na v1: `users`, `refresh_tokens`, `sync_records`, `__EFMigrationsHistory`).
