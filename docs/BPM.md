# BPM (Business Process Management) — e-form approval engine

## What is BPM?
**BPM** is the practice, and the software, of **designing, running and improving business processes**. In Taiwanese companies it usually means an **e-form and approval system (電子表單簽核系統)**: paper or Excel forms become **digital forms** that move automatically through **approval steps** according to rules.

The JD (Job Description) names **BorG BPM**, a Taiwanese e-form system (BorG SPM, with the *BorG MFlow* mobile app). Similar products: **UOF** (一等一), **Flowring Agentflow**, **EasyFlow** (鼎新). Global equivalents: Power Automate Approvals, Camunda, Nintex.

Main parts:
- **Form designer:** fields, validation, default values (e.g. the applicant's department filled in from HR data).
- **Flow designer:** steps, approver per step (by role, department or manager), **conditions** (amount > NT$100k goes to the General Manager).
- **Approval actions:** approve, reject, **send back (退回)**, **withdraw (撤回)**, **countersign (會簽)** in parallel, **add an approver (加簽)**, **notify only (知會)**.
- **Delegation (代理人):** a substitute approves while someone is on leave.
- **Timeout reminders (逾時提醒)** and escalation.
- **Audit trail:** who did what and when, with comments.
- **Scripts / APIs:** run code or call other systems when a form is submitted or approved.
- **Mobile approval:** managers approve from a phone.

## Why companies use it
- Paper and email approvals are **slow, get lost and leave no proof**.
- Medical-device makers (ISO 13485, FDA) must **prove who approved what and when**. The audit trail does that.
- Managers approve anywhere, even while visiting the Vietnam plant.
- Reports show where approvals are stuck and how long each step takes.
- Standard rules apply to everyone: no skipped steps.

## Typical uses (at a manufacturer like a glove company)
| Form | Typical route |
|---|---|
| Leave / overtime request | Employee → supervisor → HR |
| Purchase request | Requester → department head → purchasing → GM (General Manager) if over the limit |
| **Sample request** (this demo) | Sales → Sales Manager → QA → Plant Manager if a large order |
| Quality deviation / NCR (Non-Conformance Report) | QC (Quality Control) → QA (Quality Assurance) manager → plant manager |
| Special price approval | Sales → sales director → finance |
| New customer / supplier | Sales or purchasing → finance (credit check) → management |
| IT access request | Employee → manager → IT |

## Developer's role
1. Build forms and flows in the BPM product, or build them in .NET when the product can't do it.
2. Write **scripts and SQL** for defaults, validation and lookups.
3. **Integrate:** when a form is approved, push data to SAP or ERP, SQL or other systems (directly or through n8n).
4. Build **reports** on form data (SQL, Power BI).

## Where it's used
**In GloveOps** (the BPM concept built in .NET + SQL Server, standing in for BorG):
| BPM feature | Where |
|---|---|
| Form | `SampleRequest` table + `Views/SampleRequests/Edit.cshtml` |
| Flow definition (steps, conditional step) | `FlowStep` table; `MinQty = 10000` makes the plant step conditional |
| Approver resolution (plant manager of the product's plant) | `usp_ResolveApprover` |
| Approve / reject / send back (comment required) | `usp_ApprovalTask_Act` (one transaction, double-approval guard) |
| Resubmit after send back | `usp_SampleRequest_Submit` |
| Delegation (代理人) | `Delegation` table, `usp_Delegation_Set`, **Delegation** page |
| My tasks inbox | `usp_MyTasks`, **My tasks** page |
| Audit trail | `ApprovalHistory` table, timeline on the request detail page |
| Integration after approval | `ApprovalService` → n8n webhook |

**In real companies:** almost every Taiwanese mid-to-large company runs an e-form system for HR, purchasing, quality and sales approvals. IT teams maintain the forms and connect them to ERP/SAP.
