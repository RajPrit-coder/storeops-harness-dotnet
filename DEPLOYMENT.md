# Deployment

## Target
**Local container** (spec §3.4 option "Local Docker"), run with **Podman 5.8** on Windows (WSL machine)
using the repository's `Dockerfile` and `docker-compose.yml`. Podman is OCI-compatible, so the same files
work unchanged with Docker Desktop (`docker` instead of `podman`).

| Item | Value |
|---|---|
| Image | `localhost/storeops-api:latest` — multi-stage: `dotnet/sdk:10.0` build → `dotnet/aspnet:10.0` runtime |
| Container port → host | `8080` → `http://localhost:5000` |
| Environment | `ASPNETCORE_ENVIRONMENT=Production` — every request needs a bearer token (no dev auto-login) |
| Storage | in-memory (resets when the container restarts) |

## Steps taken
```bash
# 1. Build the image (first build pulls the .NET SDK and runtime images)
podman build -t storeops-api .

# 2. Start it in the background (podman compose delegates to docker-compose)
podman compose up -d

# 3. Confirm it is running
podman ps
#  storeops-harness-dotnet-storeops-api-1  localhost/storeops-api:latest  Up  0.0.0.0:5000->8080/tcp

# 4. Call the harness-generated endpoint: creates test activities, bulk-updates them, shows the audit
powershell -ExecutionPolicy Bypass -File docs\deployment\demo-bulk-status.ps1

# 5. Stop it
podman compose down
```

Configuration decisions:
- **`global.json` is excluded from the build context** (`.dockerignore`): the local SDK pin (10.0.202) would
  fail if the image's SDK is on a different feature band; the image supplies its own .NET 10 SDK.
- **Production environment** in `docker-compose.yml`, so the container behaves like a real deployment:
  `Staff:DevAutoLoginToken` lives only in `appsettings.Development.json` and is not loaded.
- No secrets are needed: tokens are seeded demo values for the in-memory stub.

## Evidence — the new feature responding in the running container
The harness-generated endpoint `PATCH /api/activities/bulk-status` (sprints 1–3 in `.harness/reviews/`)
called against the container. Full transcript: [docs/deployment/container-demo.txt](docs/deployment/container-demo.txt).

```text
$ curl -X PATCH http://localhost:5000/api/activities/bulk-status \
       -H "Authorization: Bearer dev-token-store-manager" -H "Content-Type: application/json" \
       -d '{"status":"DONE","activityIds":["<store-001 act>","<store-001 act>","<store-002 act>","act-does-not-exist"],
            "note":"Evening shift handover"}'

[HTTP 200]
{"status":"DONE","requested":4,"updated":2,"unchanged":0,"failed":2,"results":[
  {"activityId":"act-df47…","outcome":"UPDATED","previousStatus":"TODO","newStatus":"DONE","error":null},
  {"activityId":"act-7c6f…","outcome":"UPDATED","previousStatus":"TODO","newStatus":"DONE","error":null},
  {"activityId":"act-6ccf…","outcome":"FAILED","error":{"code":"NOT_FOUND", …}},     ← other store: not leaked, not changed
  {"activityId":"act-does-not-exist","outcome":"FAILED","error":{"code":"NOT_FOUND", …}}]}

$ curl http://localhost:5000/api/activities/act-df47…/audit
[{"previousStatus":"TODO","newStatus":"DONE","changedBy":"usr-sm-001","source":"BULK_STATUS_UPDATE",
  "note":"Evening shift handover", …}]
```

What it demonstrates: partial-failure handling (two failures did not stop two updates), store scoping
(store-002's activity reported `NOT_FOUND` and still `TODO` afterwards), the audit entry per updated task,
and authentication (`401 UNAUTHORIZED` envelope without a token).

### Screenshot
![bulk-status call against the running container](docs/deployment/screenshot-bulk-status.png)
