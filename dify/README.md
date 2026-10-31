# Dify app — "Glove Advisor" (RAG chatbot)

## What is Dify?
**Dify** (from *Define + Modify*) is an **open-source platform for building AI applications** on top of LLMs (Large Language Models) such as GPT, Claude, Gemini or local models. Instead of writing all the AI plumbing yourself, you build the app in a web studio and call it through a REST (Representational State Transfer) API.

Main parts:
- **App types:** Chatbot, Agent (an AI that can use tools), Workflow (multi-step AI pipeline), Text generator.
- **Knowledge base (RAG, Retrieval-Augmented Generation):** upload PDF/Word/Markdown files. Dify splits them into **chunks**, creates **embeddings** (number vectors that capture meaning), and finds the most relevant chunks for each question, so answers come from **your documents**.
- **Prompt orchestration:** system instructions, variables, model settings (temperature), conversation memory.
- **Tools:** web search, HTTP requests, code, database queries, which agents can call.
- **Model providers:** switch between OpenAI, Anthropic, Azure OpenAI, Gemini or Ollama without changing your app.
- **API for every app**, plus logs, annotations and usage monitoring.
- Runs on **Dify Cloud** or **self-hosted** with Docker.

## Why companies use it
- Staff waste time searching SOPs (Standard Operating Procedures), specs and policies. A chatbot answers in seconds.
- **Self-hosting** keeps confidential documents inside the company network.
- **RAG reduces hallucination:** the AI answers from company documents and can cite them.
- IT builds AI apps in days, not months, and business users can update the knowledge base themselves.
- One platform for many AI use cases, with central logs and access control.

## Typical uses (at a manufacturer like a glove company)
| Use | How |
|---|---|
| Product spec assistant | "Which glove meets ASTM D6978 for chemo drugs?" answered from spec sheets |
| SOP / ISO 13485 assistant | Employees ask quality-procedure questions in Chinese, English or Vietnamese |
| HR / IT helpdesk bot | Leave policy, VPN setup, password reset steps |
| Customer email drafts | Draft replies or quotation emails from product data |
| Document extraction | Workflow reads PO (Purchase Order) PDFs and returns JSON fields for the order system |
| Report summaries | Summarise QC (Quality Control) or complaint reports for managers |

## When to use Dify vs other options
| Option | Best for |
|---|---|
| **Dify** | company AI apps with a knowledge base, prompt management, and an API for other systems |
| **Calling an LLM API directly from C#** | one simple AI feature with no document retrieval |
| **n8n AI nodes** | AI as one step inside an automation flow |
| **Fine-tuning a model** | changing the model's style or format (RAG is better for facts that change often) |

## Where it's used
**In GloveOps:**
- **Ask AI page** (`Views/Integrations/AskAi.cshtml`): jQuery chat box → `IntegrationsController.Ask` → `DifyAssistant` (`Core/Integrations/Integrations.cs`) → Dify `POST /chat-messages`. The API key stays on the server.
- **Knowledge base:** the 7 files in `knowledge/` (fictional spec sheets + FAQ).
- **Integrations page** shows whether Dify is configured.

**In real companies:** an internal chatbot in the intranet or Teams, an AI step in BPM forms ("suggest the right product"), a helpdesk, and document-processing workflows called from n8n or C#.

**Story:** sales and customer-service staff waste time searching spec sheets. "Glove Advisor" answers questions **only from company documents** and names the source, using RAG (Retrieval-Augmented Generation). GloveOps calls it from the **Ask AI** page through Dify's REST API, so the API key stays on the server.

Knowledge files: [`knowledge/`](knowledge) holds 6 fictional product spec sheets and an FAQ.

## 1. Create the app
**Option A, Dify Cloud (fastest):** sign up at cloud.dify.ai (free sandbox).
**Option B, self-host (what a medical company would do):** needs Docker.
```bash
git clone https://github.com/langgenius/dify.git
cd dify/docker && cp .env.example .env && docker compose up -d
# open http://localhost/install, then set Dify:BaseUrl = http://localhost/v1
```

Then:
1. **Settings → Model Provider:** add a provider (e.g. OpenAI, Anthropic or Azure OpenAI) with your API key.
2. **Knowledge → Create Knowledge:** upload every file in `knowledge/`.
   - **Free Sandbox plan:** only **one file** per knowledge base. Upload [`GloveOps-Knowledge-AllInOne.md`](GloveOps-Knowledge-AllInOne.md) instead (all 7 documents in one file) and set the chunk **delimiter to `---`**, so each product sheet becomes one chunk. Sandbox also includes 200 free message credits with Dify-provided models (no OpenAI key needed). This is how the live demo app was set up on 2026-10-30.
   - Chunking: *General*, max chunk length 500 tokens, overlap 50.
   - Index: *High quality* (embedding model), retrieval *Hybrid search* (vector + keyword), Top K = 3.
3. **Studio → Create app → Chatbot**, name it `Glove Advisor`.
   - **Context:** add the knowledge base.
   - **Instructions (system prompt):**
     ```
     You are Glove Advisor for a disposable-glove manufacturer.
     Answer ONLY from the provided context. If the context does not contain the answer,
     say "I don't have that information in the product documents."
     Always name the product SKU and the source document.
     Reply in the same language as the question (English, 中文, or Tiếng Việt).
     Keep answers short: a recommendation and 2–4 bullet points.
     ```
   - Model temperature: 0.2 (fewer creative guesses).
4. Test in the Dify preview, then **Publish**.
5. **API Access → API Key → Create.** Copy the key (it starts with `app-`).

## 2. Connect GloveOps
```bash
dotnet user-secrets set "Dify:ApiKey" "app-xxxxxxxx" --project src/Divakar.GloveOps.Web
# self-hosted only:
dotnet user-secrets set "Dify:BaseUrl" "http://localhost/v1" --project src/Divakar.GloveOps.Web
```
Restart GloveOps → **Ask AI**. The code is `DifyAssistant` in `src/Divakar.GloveOps.Core/Integrations/Integrations.cs`. It sends `POST {BaseUrl}/chat-messages` with `response_mode: "blocking"` and keeps `conversation_id` so follow-up questions have context.

## 3. Demo questions
| Question | Expected |
|---|---|
| Which glove is suitable for handling chemotherapy drugs? | NBR-CH-200, ASTM D6978 |
| 哪一款手套適合半導體無塵室？ | CR-NBR-100, ISO Class 5 (answered in Chinese) |
| Our nurse has a latex allergy. Can she use LTX-SG-100? | No, latex; suggests nitrile |
| What does AQL 1.5 mean? | Explanation from the FAQ |
| What is the price of NBR-EX-100? | "I don't have that information…" (no hallucination) |

## 4. What to say in the interview
> "Dify is an open-source LLM app platform. I built a RAG chatbot over product spec sheets: Dify chunks the documents, stores embeddings, retrieves the top three chunks for each question, and the model answers only from them with the SKU and source. I set a low temperature and an instruction to say 'I don't know' instead of guessing. GloveOps calls the Dify API from the server, so the browser never sees the key, and it keeps the conversation ID for follow-ups. For a medical company I'd self-host Dify with Docker so documents stay inside the network."
