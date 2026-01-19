# Production Runbook

Operational procedures for Stock Analyzer production environment.

**Last Updated:** 2026-01-19

---

## Quick Reference

| Resource | URL/Command |
|----------|-------------|
| **Production URL** | https://psfordtaurus.com |
| **App Service URL** | https://app-stockanalyzer-prod.azurewebsites.net |
| **Health Check** | https://psfordtaurus.com/health/live |
| **GitHub Actions** | https://github.com/psford/claudeProjects/actions |
| **Azure Portal** | https://portal.azure.com |
| **Cloudflare** | https://dash.cloudflare.com |

---

## Deployment

### Deploy to Production

Production deployments are **manual only** via GitHub Actions.

1. Go to [GitHub Actions](https://github.com/psford/claudeProjects/actions)
2. Select "Deploy to Azure Production" workflow
3. Click "Run workflow"
4. Fill in:
   - **confirm_deploy:** Type `deploy` to confirm
   - **reason:** Brief description (e.g., "v2.4 - App Service migration")
5. Select branch (usually `master`)
6. Click "Run workflow"

The workflow will:
- Validate confirmation and test Azure credentials
- Build and test .NET solution
- Build Docker image (tagged `prod-{run_number}`)
- Push to ACR
- Update App Service container configuration
- Restart App Service
- Run health check with retry

### Verify Deployment

```bash
# Check health endpoint
curl -s https://psfordtaurus.com/health/live

# Check full health status
curl -s https://psfordtaurus.com/health | jq .

# Check App Service state
az webapp show --resource-group rg-stockanalyzer-prod --name app-stockanalyzer-prod --query "state"
```

---

## Rollback

### Option 1: Redeploy Previous Image (Fastest)

Each deployment tags images as `prod-{run_number}`. To rollback:

```bash
# Login to Azure
az login

# Get current run number from last successful deploy in GitHub Actions
# Then deploy the previous image (e.g., if current is prod-45, rollback to prod-44)

az webapp config container set \
  --name app-stockanalyzer-prod \
  --resource-group rg-stockanalyzer-prod \
  --docker-custom-image-name acrstockanalyzerer34ug.azurecr.io/stockanalyzer:prod-{PREVIOUS_RUN_NUMBER} \
  --docker-registry-server-url https://acrstockanalyzerer34ug.azurecr.io

# Restart to pick up new image
az webapp restart --name app-stockanalyzer-prod --resource-group rg-stockanalyzer-prod

# Verify health
curl -s https://psfordtaurus.com/health/live
```

### Option 2: Revert Git and Redeploy

If the code change itself was bad:

```bash
# Find the last good commit
git log --oneline -10

# Revert to that commit on master
git checkout master
git revert HEAD  # or specific commit

# Push and trigger deploy via GitHub Actions
git push
# Then manually trigger deploy workflow
```

### Option 3: Emergency Restart

If App Service is completely unresponsive:

```bash
# Force stop and start
az webapp stop --name app-stockanalyzer-prod --resource-group rg-stockanalyzer-prod
sleep 10
az webapp start --name app-stockanalyzer-prod --resource-group rg-stockanalyzer-prod
```

**Note:** App Service uses CNAME DNS, so there's no IP address to update on restart (unlike ACI).

---

## Monitoring

### Health Endpoints

| Endpoint | Purpose | Expected Response |
|----------|---------|-------------------|
| `/health/live` | Liveness probe | 200 OK |
| `/health/ready` | Readiness (DB connected) | 200 OK |
| `/health` | Full status JSON | 200 + JSON |

### Check App Service Logs

```bash
# Stream live logs
az webapp log tail --name app-stockanalyzer-prod --resource-group rg-stockanalyzer-prod

# Download log files
az webapp log download --name app-stockanalyzer-prod --resource-group rg-stockanalyzer-prod --log-file logs.zip
```

### Check Container Logs via Kudu

```bash
# Get Kudu credentials and access container logs
az webapp deployment list-publishing-credentials \
  --name app-stockanalyzer-prod \
  --resource-group rg-stockanalyzer-prod \
  --query "{user: publishingUserName, pass: publishingPassword}"

# Then access: https://app-stockanalyzer-prod.scm.azurewebsites.net/api/logs/docker
```

---

## Common Issues

### Issue: 502/504 Gateway Timeout

**Cause:** Container crashed or taking too long to start.

**Fix:**
```bash
# Check App Service state
az webapp show --resource-group rg-stockanalyzer-prod --name app-stockanalyzer-prod --query "state"

# Restart the app
az webapp restart --name app-stockanalyzer-prod --resource-group rg-stockanalyzer-prod

# Wait 60 seconds and check health
sleep 60
curl -s https://psfordtaurus.com/health/live
```

### Issue: Container Fails Health Probe

**Cause:** Startup probe timeout (default 240s). ImageCacheService prefill may cause thread pool starvation.

**Check:**
```bash
# Stream logs to see startup messages
az webapp log tail --name app-stockanalyzer-prod --resource-group rg-stockanalyzer-prod

# Look for "HeartbeatSlow" warnings indicating thread pool starvation
```

**Temporary Fix:** Restart; the second startup is faster (container cached).

### Issue: Database Connection Failed

**Cause:** SQL credentials expired or network issue.

**Check:**
```bash
# View logs for connection errors
az webapp log tail --name app-stockanalyzer-prod --resource-group rg-stockanalyzer-prod | grep -i "sql\|connection"
```

**Fix:** Check Key Vault secret `sql-connection-string` is correct.

### Issue: Cloudflare 522 Error

**Cause:** Origin (App Service) not responding.

**Check:**
1. App Service is running
2. HTTPS is enabled on App Service
3. Cloudflare SSL mode is "Full (strict)"

```bash
# Test direct access to App Service
curl -s https://app-stockanalyzer-prod.azurewebsites.net/health/live
```

### Issue: Deployment Failed - ACR Authentication

**Cause:** ACR password in GitHub Secrets is incorrect or expired.

**Fix:**
```bash
# Get new ACR password
az acr credential show --name acrstockanalyzerer34ug --query "passwords[0].value" -o tsv

# Update GitHub secret ACR_PASSWORD with new value
```

---

## Key Vault Access

### View Secrets

```bash
# List secret names
az keyvault secret list --vault-name kv-stockanalyzer-er34ug --query "[].name" -o tsv

# Get a secret value
az keyvault secret show --vault-name kv-stockanalyzer-er34ug --name finnhub-api-key --query "value" -o tsv
```

### Update Secrets

```bash
# Update a secret
az keyvault secret set --vault-name kv-stockanalyzer-er34ug --name finnhub-api-key --value "new-api-key"

# Restart App Service to pick up changes (Key Vault refs are cached)
az webapp restart --name app-stockanalyzer-prod --resource-group rg-stockanalyzer-prod
```

---

## Contacts

| Role | Contact |
|------|---------|
| Primary | Patrick (repo owner) |
| Azure Support | https://portal.azure.com/#blade/Microsoft_Azure_Support/HelpAndSupportBlade |
| Cloudflare | https://dash.cloudflare.com (free tier = community support) |

---

## Version History

| Date | Change |
|------|--------|
| 2026-01-19 | Updated for App Service migration, Key Vault, removed ACI references |
| 2026-01-18 | Initial runbook created (ACI-based) |
