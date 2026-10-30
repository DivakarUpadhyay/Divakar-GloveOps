# n8n workflows

| File | Trigger | What it does | Needs credentials? |
|---|---|---|---|
| `01-approval-notification.json` | **Webhook** `POST /webhook/gloveops-approved` (called by GloveOps on final approval) | checks the shared key → builds the plant message → **calls back** GloveOps (`/api/integrations/n8n-callback`) → responds `{ok:true}`. An Email node is included but disabled | No (email optional) |
| `02-daily-production-report.json` | **Schedule** weekdays 08:00 | SQL Server query of the latest day's output and defect % per plant → HTML table → email managers | SQL Server (+ SMTP for email) |

```
GloveOps (final approval) ──POST──► n8n Webhook ──► Code: check key + message ──┬──► HTTP: callback GloveOps ──► Respond
                                                                              └──► Email plant (enable with SMTP)
```

## 1. Run n8n locally (no Docker needed)
```bash
npx n8n            # first run downloads n8n; then open http://localhost:5678 and create the owner account
```
(With Docker: `docker run -it --rm -p 5678:5678 docker.n8n.io/n8nio/n8n`.)

## 2. Import the workflows
In n8n: **Workflows → ⋯ → Import from File** → choose each JSON file. Or use the CLI (Command Line Interface):
```bash
npx n8n import:workflow --separate --input=n8n
```

## 3. Connect GloveOps ↔ n8n
1. Open *GloveOps — sample request approved* and click **Activate** (top right). The production webhook URL is then live.
2. Tell GloveOps where to send events. Use the **same shared key** that the Code node and the HTTP Request header use (`change-me-gloveops`; change both for real use):
   ```bash
   dotnet user-secrets set "N8n:WebhookUrl" "http://localhost:5678/webhook/gloveops-approved" --project src/Divakar.GloveOps.Web
   dotnet user-secrets set "N8n:SharedKey"  "change-me-gloveops" --project src/Divakar.GloveOps.Web
   ```
3. Restart GloveOps. Approve a request through to the end (Kevin → Grace).
4. Check GloveOps → **Integrations**. You'll see two rows:
   - `n8n-webhook · Sent · HTTP 200 {"ok":true,...}`, the outbound call
   - `n8n-callback · Received · SR-… PlantNotified`, n8n calling back
5. In n8n → **Executions** you can see each run with input and output per node.

## 4. Daily report (optional)
Open workflow 02 → the SQL Server node → **Credentials → Create new**: host `localhost`, port `1433` (enable TCP/IP for the instance in SQL Server Configuration Manager), database `DivakarGloveOps`, and a SQL login with read access to the `bi` schema. Click **Test workflow** to run it immediately.

## 5. What to say in the interview
> "When a sample request gets its final approval, GloveOps posts a JSON event to an n8n webhook with a shared key. The approval is already committed, so if n8n is down the app just logs 'Failed' and the user isn't blocked. n8n validates the key, builds the plant notification, and calls back an API (Application Programming Interface) in GloveOps, so we have an end-to-end audit trail on both sides. A second workflow queries SQL Server every weekday at 8 am and emails a production summary. Workflows are exported as JSON and kept in Git."
