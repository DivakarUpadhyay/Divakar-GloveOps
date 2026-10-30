# Dify app — "Glove Advisor" (RAG chatbot)

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
