# Divakar GloveOps

**A sample-request and approval portal for a disposable-glove manufacturer**, built by **Divakar Upadhyay** as a portfolio demo of an internal IT (MIS, Management Information Systems) toolset:
**ASP.NET Core MVC (Model–View–Controller) · Dapper · SQL Server stored procedures · jQuery · Git**, integrated with a **BPM-style (Business Process Management) approval engine, n8n, Dify (LLM, Large Language Model), Automation 360 (RPA, Robotic Process Automation) and Power BI (Business Intelligence)**.

> All companies, customers, products and figures are fictional demo data.

## The story
A sales rep requests glove **samples** for a customer (product, size, boxes, date). The request goes through an **e-form approval flow** before a plant produces it:

```
Sales ──submit──► 1. Sales Manager ──► 2. QA spec check ──► 3. Plant Manager* ──► Approved ──► n8n notifies the plant
                       │ send back / reject (comment required)                       (* only if ≥ 10,000 boxes,
                       └──────────────► applicant edits & resubmits                     routed to the producing plant)
```

## Architecture
```
 Browser (Razor + Bootstrap + jQuery AJAX)
        │  cookie auth, anti-forgery token
 ┌──────▼─────────────────────────────── Divakar.GloveOps.Web (.NET 10 MVC) ─┐
 │ Controllers: SampleRequests · Tasks · Integrations · Account · Home       │
 │ /api/integrations/n8n-callback  (shared-key protected)                    │
 └──────┬────────────────────────────────────────────────────────────────────┘
 ┌──────▼─────────────────────────────── Divakar.GloveOps.Core ──────────────┐
 │ ApprovalService ─► ISampleRequestRepository (Dapper) ─► stored procedures │
 │ RequestRules (validation, priority)  · N8nWorkflowNotifier · DifyAssistant│
 └──────┬──────────────────────────────┬───────────────────────┬─────────────┘
        ▼                              ▼                       ▼
  SQL Server: DivakarGloveOps       n8n webhook ──► callback    Dify chat API (RAG over spec sheets)
  dbo.* OLTP + approval engine
  bi.*  star schema ──────────────► Power BI report
        ▲
  Automation 360 bot ──► enters requests through the web UI (no-API scenario)
```

## What each part demonstrates
| JD (Job Description) item | Where |
|---|---|
| C# generics, inheritance, OOP | `DapperRepository` base class with generic `RunAsync<T>`; records; interfaces + DI (Dependency Injection) |
| MS SQL stored procedures | `database/02_procedures.sql`: transactional approval engine, `THROW` business rules, `UPDLOCK`, `ROWVERSION`, conditional routing via `FlowStep.MinQty` |
| Dapper + ORM (Object-Relational Mapping) | `Core/Data/Repositories.cs`: `DynamicParameters` with OUTPUT params, `QueryMultiple` (3 result sets), SQL errors → `BusinessRuleException` |
| .NET Core MVC | Controllers, view models, DataAnnotations + server-side rules, PRG (Post/Redirect/Get) pattern, TempData, cookie authentication, global anti-forgery |
| jQuery | AJAX approve/reject with anti-forgery header, live approval-route hint, quick table filter, chat UI |
| Git | this repo, plus GitHub Actions build & test (`.github/workflows/build.yml`) |
| BPM e-forms (BorG) | approval engine: steps, conditional step, send back, resubmit, **delegation (代理人)**, full audit history |
| Automation 360 | `automation360/`: bot design + input CSV with a deliberately bad row |
| Power BI | `powerbi/`: star schema, DAX measures, RLS (Row-Level Security) per plant |
| n8n | `n8n/`: importable workflows (approval webhook + callback, daily SQL report) |
| Dify | `dify/`: knowledge base files, prompt, C# API client, Ask AI page |
| Documentation | this README + one README per tool folder |

## Run it locally
Requirements: .NET 10 SDK (Software Development Kit), SQL Server (instance `DIVAKARLAPTOP\DEVELOPERMSSQL`, Windows sign-in).
```bat
reset-database.bat        :: creates DivakarGloveOps with demo data (+ ~14k Power BI rows)
start-gloveops.bat        :: builds and opens http://localhost:5080
```
Different SQL Server? Set the connection string:
`dotnet user-secrets set "ConnectionStrings:GloveOps" "Server=...;Database=DivakarGloveOps;..." --project src/Divakar.GloveOps.Web`

Tests: `dotnet test` (26 tests: rules, approval service, n8n notifier, and end-to-end stored-procedure tests that skip if the database isn't available).

Optional integrations (keys go in **user secrets**, never in Git): see [`n8n/README.md`](n8n/README.md) and [`dify/README.md`](dify/README.md).

## Demo script (about 5 minutes, for the interview)
1. **Sign in as Anna (Sales)** → Dashboard (KPIs from `usp_Dashboard`, one Dapper `QueryMultiple`).
2. **New sample request**: type 12,000 boxes and watch the jQuery hint change the route to include the Plant Manager. Submit.
3. **Switch user → Kevin (Sales Manager)** → My tasks → open it → **Send back** with no comment. The stored procedure refuses ("comment required"). Add a comment → sent back.
4. **Anna** edits and **resubmits**. The history shows *Resubmitted*.
5. **Kevin → Grace (QA) → plant manager** approve. The plant manager is chosen from the **product's plant**.
6. **Integrations** page: the n8n webhook call and n8n's callback are logged.
7. **Ask AI**: "Which glove is suitable for chemotherapy?" → Dify answers from the spec sheets.
8. Show the **Power BI** report (production defect rate per plant, RLS "View as" a plant manager).
9. Show the **A360** bot entering 20 rows from CSV, with row 17 failing gracefully.
10. Open `02_procedures.sql` → explain `usp_ApprovalTask_Act`: one transaction, `@@ROWCOUNT` guard against double approval, audit row, next step.

## Project layout
```
database/        01 schema · 02 procedures · 03 seed · 04 Power BI star schema · 00 run-all
src/Divakar.GloveOps.Core/   Models · Workflow (rules, ApprovalService) · Data (Dapper) · Integrations (n8n, Dify)
src/Divakar.GloveOps.Web/    MVC controllers, Razor views, jQuery (wwwroot/js/site.js)
tests/Divakar.GloveOps.Tests/  xUnit: unit + database end-to-end tests
n8n/ · dify/ · automation360/ · powerbi/   one folder per integration, each with a README
```

## Design decisions (good interview talking points)
- **State changes live in stored procedures**, each in one transaction with an audit row. Dapper keeps the C# side thin and fast.
- **Concurrency:** `UPDATE … WHERE Status='Pending' AND (ApproverId=@u OR DelegateId=@u)` plus a `@@ROWCOUNT` check means two clicks or two approvers can't both act.
- **Data-driven routing:** adding a step or changing the 10,000-box threshold is a `FlowStep` row change, not a code change.
- **Notifications never undo approvals:** the n8n call happens after commit, and failures are logged, not thrown.
- **Secrets** (Dify key, n8n key) live in user secrets or environment variables. The n8n callback uses a constant-time key comparison.
- **Demo sign-in** is a user picker. In production it would be Windows/AD (Active Directory) or Entra ID SSO (Single Sign-On).
