# n8n workflows

## What is n8n?
**n8n** (say "n-eight-n", short for *nodemation*: node + automation) is an **open-source workflow automation tool**. You connect apps and services by dragging **nodes** onto a canvas and linking them into a **workflow**. It works like Zapier or Make, but you can **self-host** it on your own server or PC, so company data never leaves your network.

Every workflow has:
- **Trigger node**: what starts it. Examples: a **Webhook** (another system calls a URL), a **Schedule** (cron, e.g. every day at 08:00), a new email, a new database row, a form.
- **Action nodes**: what it does. Examples: **HTTP Request** (call any REST API, Representational State Transfer), **Microsoft SQL**, Email, Microsoft Teams, Slack, Telegram, Google Sheets, OpenAI/AI Agent.
- **Logic nodes**: **IF**, **Switch**, **Merge**, **Loop**, and **Code** (JavaScript/Python) for custom logic.
- **Credentials**: passwords and API keys stored encrypted, not inside the workflow.
- **Executions**: a log of every run with each node's input and output, so debugging is easy.

## Why companies use it
- **Glue between systems:** connect BPM (Business Process Management), SQL Server, SAP, email and Teams without writing and deploying a new C# service each time.
- **Self-hosted:** data stays inside the company. This matters for medical, finance and HR data.
- **Fast to build and change:** a new integration takes hours, not weeks, and other IT staff can read the visual flow.
- **AI-ready:** built-in LLM (Large Language Model) and AI Agent nodes, and it can call Dify or OpenAI.
- **Free and open source:** the community edition costs nothing; you pay only for the server.

## Typical uses (at a manufacturer like a glove company)
| Use | Flow |
|---|---|
| Approval notification | BPM form approved → webhook → n8n → write log to SQL → notify the plant on Teams/email |
| Daily report | Schedule 08:00 → SQL query of yesterday's output and defects → HTML table → email managers |
| System sync | Nightly: copy new customers/products from ERP (Enterprise Resource Planning, e.g. SAP) API → internal SQL database |
| Order intake | New email with PO (Purchase Order) PDF → AI extracts fields → create a draft order → alert sales |
| Alerts | Every 15 min: check defect rate > 2% or machine downtime → Teams/LINE alert to QA |
| AI helper | New sample request → call Dify → suggest the right glove model → add as a comment |

## n8n vs RPA vs C# (when to use which)
| Tool | Best for |
|---|---|
| **n8n** | API-to-API integration, notifications, scheduled jobs, quick automations |
| **RPA (Robotic Process Automation, e.g. Automation 360)** | systems with **no API**, where a bot must click the screen (SAP GUI, old portals) |
| **C# / .NET** | core business logic, complex rules, high volume, anything that needs unit tests |

## Where it's used
**In GloveOps:**
- **Outbound:** when a request gets its final approval, `ApprovalService` → `N8nWorkflowNotifier` (`Core/Integrations/Integrations.cs`) POSTs the request as JSON to the n8n webhook with the `X-GloveOps-Key` header. Failures are logged, never thrown.
- **Inbound:** n8n calls back `POST /api/integrations/n8n-callback` (`Program.cs`), which checks the shared key.
- **Integrations page:** both directions appear in the integration log (`IntegrationLog` table).
- **Daily report:** workflow 02 reads the `bi` schema in SQL Server directly.

**In real companies:** connecting BPM, ERP, CRM (Customer Relationship Management), email and chat tools; scheduled reports and data syncs; alerting; and AI automations. It usually runs self-hosted on a company server or in Docker.

## Workflows in this folder

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

### Verified (2026-10-31, n8n 2.8.4)
- Both JSON files import with `n8n import:workflow`. In n8n 2.x, "Activate" is called **Publish** (`n8n publish:workflow --id=<id>`, then restart n8n).
- Without `X-GloveOps-Key` the Code node stops the run ("Invalid X-GloveOps-Key"). With the key, the full loop logged in GloveOps:
  `n8n-webhook · Sent · HTTP 200 {"ok":true,"requestNo":"SR-2026-00009"}` and `n8n-callback · Received · SR-2026-00009: PlantNotified …`.
- Note: when a run fails inside n8n, the webhook still answers HTTP 200 with an empty body. That's why GloveOps treats n8n's **callback** ("Received") as the real confirmation, not the webhook reply. Check failed runs in n8n → **Executions**.

## 4. Daily report (optional)
Open workflow 02 → the SQL Server node → **Credentials → Create new**: host `localhost`, port `1433` (enable TCP/IP for the instance in SQL Server Configuration Manager), database `DivakarGloveOps`, and a SQL login with read access to the `bi` schema. Click **Test workflow** to run it immediately.

## 5. What to say in the interview
> "When a sample request gets its final approval, GloveOps posts a JSON event to an n8n webhook with a shared key. The approval is already committed, so if n8n is down the app just logs 'Failed' and the user isn't blocked. n8n validates the key, builds the plant notification, and calls back an API (Application Programming Interface) in GloveOps, so we have an end-to-end audit trail on both sides. A second workflow queries SQL Server every weekday at 8 am and emails a production summary. Workflows are exported as JSON and kept in Git."
