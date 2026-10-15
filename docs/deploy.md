# Verto API — primeiro deploy (runbook)

Executar na ordem. Toca infra compartilhada com o BillFolder (mesma VPS, mesmo Postgres), então cada passo tem uma verificação.

## 1. Create the GitHub repo and secrets

```bash
cd ~/Repos/Verto
gh repo create Verto --private --source=. --remote=origin
gh secret set VPS_HOST
gh secret set VPS_USER
gh secret set VPS_SSH_KEY < ~/.ssh/<the key BillFolder's deploy uses>
```

Use the same three values as the BillFolder repo (`VPS_USER` is `deploy`). Do not push yet — the server must exist first.

## 2. DNS on Cloudflare

Create an `A` record `verto-api` → the VPS IPv4, with the **same proxy setting as the existing `api` record** of `billfolder.app`.

Verify: `dig +short verto-api.billfolder.app` returns an IP.

## 3. Database and role (on the VPS)

```bash
ssh deploy@<VPS_HOST>
cd /opt/billfolder
docker compose ps postgres   # confirm the service is named "postgres"
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

Verify isolation: `docker compose exec -T postgres psql -U verto -d billfolder -c 'select 1'` must fail with `permission denied for database "billfolder"`, and `curl -fsS https://api.billfolder.app/v1/health` must still return `ok`.

## 4. Compose service

Add to `/opt/billfolder/docker-compose.yml` under `services:` (replace `<owner>` with the lowercase GitHub owner):

```yaml
  verto-api:
    image: ghcr.io/<owner>/verto-api:latest
    restart: unless-stopped
    # Caps Verto's memory so an Argon2 login burst can't push the host into OOM-killing BillFolder.
    mem_limit: 512m
    depends_on:
      - postgres
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
```

Verify: `docker compose config --quiet` exits 0.

## 5. nginx + TLS

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

## 6. First deploy

```bash
cd ~/Repos/Verto
git push -u origin main
gh run watch
```

Expected: both jobs green; last log line `✓ Deploy complete`. If the pull fails with `denied`, the VPS GHCR login does not cover the new package: on the VPS run `docker login ghcr.io` with a token that has `read:packages`.

Verify: `curl -fsS https://verto-api.billfolder.app/v1/health` → `"status":"ok"`.

## 7. Create the owner account, then close signup

```bash
curl -fsS -X POST https://verto-api.billfolder.app/v1/auth/signup \
  -H "Content-Type: application/json" \
  -d '{"email":"<your email>","password":"<strong password>","displayName":"Jess"}' > /dev/null && echo created
ssh deploy@<VPS_HOST> 'cd /opt/billfolder && sed -i "s/^VERTO_ALLOW_SIGNUP=true/VERTO_ALLOW_SIGNUP=false/" .env && docker compose up -d --no-deps verto-api'
```

Verify: the same signup `curl` with another email now returns HTTP 403.

## 8. Backups

On the VPS, `crontab -e` and add (BillFolder's existing 03:00 backup stays as is):

```cron
15 3 * * * cd /opt/billfolder && docker compose exec -T postgres pg_dump -U billfolder verto | gzip > /opt/billfolder/backups/verto-$(date +\%F).sql.gz && find /opt/billfolder/backups -name 'verto-*.sql.gz' -mtime +14 -delete
```

Verify the next day: `ls -la /opt/billfolder/backups/verto-*` shows a non-empty file. To test immediately, run the command once by hand.
