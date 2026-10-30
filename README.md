# Corporate tax categorization

Angular 21 client + ASP.NET Core REST API + SQL Server storage, orchestrated with .NET Aspire. The app imports Excel workbooks into SQL and runs local ONNX similarity-based categorization when the user requests it.

Run `dotnet run --project CorporateTaxCategorizationHost`, then open **http://localhost:4200/**. The Angular app supports client selection, upload, import status, DPL/TFA/CRD tabs, Categorize buttons, batch progress and 20-row pagination. Importing raw data does **not** automatically run prediction. See [working pipeline](docs/workbook-pipeline.md).

**Startup resets application data:** `Database:ResetOnStartup` defaults to true. Each API startup recreates the app-owned `tax` schema and clears prior predictions, then requeues archived Excel workbooks and the sample for the background raw import job. Set `Database__ResetOnStartup=false` to preserve data. The SQL database itself is not dropped.

## First use case: suggest classifications from reviewed ledger entries

Confirmed input: each client supplies an Excel workbook with separate **DPL**, **TFA** and **CRD** sheets. Use client-specific column mappings and sheet-specific classification workflows. See [client workbook design](docs/client-workbook-design.md) for the planned import, SQL, API and review architecture. The current description-only API is a synthetic demonstration of inference and similarity retrieval.

An accountant imports a general ledger for an entity and accounting period. For each line, the application finds similar previously approved entries and presents suggested **independent** classifications:

- Capital/revenue divide: capital, revenue, mixed, or review required.
- Tangible fixed assets: a versioned asset category, not applicable, or review required.
- Detailed P&L: a versioned reporting category, not applicable, or review required.

For example, a line describing a laptop purchase may resemble reviewed computer-equipment acquisitions. The UI shows those precedents and their labels; an accountant checks the invoice, context, accounting policy and applicable jurisdiction before accepting or changing them. A repair description may need more evidence to distinguish maintenance from improvement. These are illustrative workflows, not tax treatment rules. Asset classification does not itself establish capital-allowance eligibility. Mixed invoices need line splitting or manual review.

Inputs: entity, period, ledger line ID, nominal account, description, supplier, amount, currency, invoice reference and supporting document reference. Preserve original values. Do not infer treatment from amount or keywords alone.

Outputs: three proposed classifications, similar reviewed line IDs, cosine similarities, model/preprocessing/taxonomy versions, and review status. A similarity score is **not** a probability. Every suggestion requires review in the initial releases. No model result posts a journal or finalizes a return.

## Implemented increment

`CorporateTaxCategorization.MachineLearning` targets .NET 10 and references:

- `Microsoft.ML.OnnxRuntime`: `OnnxEmbeddingSession` loads and reuses a CPU inference session, validates a narrow model contract, and disposes native inference resources.
- `System.Numerics.Tensors`: `ReviewedExampleMatcher` uses `TensorPrimitives.CosineSimilarity` to rank reviewed precedent embeddings; rejects invalid or incompatible vectors.

The adapter expects a **future exported encoder** with one float32 `features` tensor of shape `[1,N]` and a float32 `embedding` output of shape `[1,D]`. It does not accept arbitrary downloaded transformer models. Training and serving must share the exact text feature vocabulary, normalization, feature order and model version. A transformer with token IDs, attention masks and pooling requires a separate adapter and tokenizer.

A pretrained `all-MiniLM-L6-v2` ONNX model and tokenizer assets are now downloaded under `CorporateTaxCategorization.MachineLearning/Models/all-MiniLM-L6-v2`. This general similarity model is not a trained tax classifier and is connected to the REST API through MiniLmTextEncoder, separately from the float-feature adapter. See [model notes](CorporateTaxCategorization.MachineLearning/Models/MODEL-NOTES.md) for provenance, verified runtime contract and next integration steps. No project-specific training data or accuracy claim is bundled.

Only compare embeddings produced by the same model and preprocessing version. The caller must restrict candidate history to the authorized tenant/entity and compatible taxonomy; the matcher is a numerical library, not an authorization boundary. Empty history returns no matches. Labels on a precedent are evidence for review, not a complete prediction policy.

## REST API

The console project has been replaced by `CorporateTaxCategorization.Api`. Run it directly:

```powershell
dotnet run --project CorporateTaxCategorization.Api --no-launch-profile --urls http://localhost:5080
```

Or run `dotnet run --project CorporateTaxCategorizationHost` to launch the API through Aspire.

- `GET /api/categorization/examples` returns six synthetic example entries.
- `POST /api/categorization/suggestions` accepts a description and returns ranked examples, labels, similarity scores, token count, truncation status and `reviewRequired: true`.

Example PowerShell request:

```powershell
Invoke-RestMethod -Method Post -Uri http://localhost:5080/api/categorization/suggestions -ContentType 'application/json' -Body '{"description":"New laptops for the finance team","count":3}'
```

See `CorporateTaxCategorization.Api/CorporateTaxCategorization.Api.http` for executable HTTP examples. `count` defaults to 3 and must be between 1 and 6. Missing, blank or oversized descriptions return HTTP 400 validation problems. Descriptions are limited to 32,000 characters; model input is truncated to 256 tokens and the response reports truncation. Unexpected server failures use problem responses.

The API loads one shared `MiniLmTextEncoder` at startup and precomputes the example embeddings once. Requests tokenize text, run local ONNX inference, mean-pool and normalize a 384-value vector, then call `ReviewedExampleMatcher.Find`. Inference access is serialized for this initial CPU service. The float-feature `OnnxEmbeddingSession` remains a separate adapter. The host disposes the model session on shutdown.

The legacy single-description responses identify `exampleSource: "SyntheticDemo"`; example labels are illustrative and similarity is not calibrated confidence. The workbook API separately persists raw rows, references, predictions and runs in SQL. Approved real-client precedents, authentication/tenant authorization and production hosting remain future increments. Upstream embedding parity and tax-category accuracy remain to be evaluated.

## Incremental delivery

1. **Current: contract and numerical foundation.** Define the use case, independent labels, ONNX adapter and precedent ranking. Agree jurisdiction and accountant-owned taxonomy before adding treatment rules.
2. **Reviewable vertical slice.** Angular, SQL persistence, background import and user-triggered predictions are implemented for the fixed synthetic template. Extend these with client mapping profiles, migrations, authentication, approved reference data and audited review/correction workflows.
3. **Train and evaluate.** Use `Microsoft.ML` for text featurization and a supervised baseline on approved labels; fit separate models for each classification dimension. Consider an embedding encoder for precedent retrieval. Verify export support for the selected pipeline/operators before choosing ONNX. Evaluate on held-out entities and later periods, excluding duplicate invoices across splits. Measure per-class precision/recall, capital-versus-revenue confusion, abstention coverage and latency. Compare with nominal-account mapping and manual baseline. Have accountants review errors.
4. **Integrate real ONNX inference.** Publish a versioned model with checksum, feature contract, taxonomy, training provenance and evaluation report. Add a real-model parity test against training output before enabling suggestions. Register the reusable inference session in API dependency injection; restrict model loading to trusted deployment artifacts. Persist ranked evidence and reviewer corrections. Do not silently fall back to another model on inference failure: return a manual-review status.
5. **Controlled automation.** Calibrate confidence on held-out reviewed data, define thresholds separately for each output and category, and monitor drift. Allow automatic acceptance only after agreed quality targets and explicit business approval. Retain override, audit and rollback.
6. **Optimize when measured.** Profile CPU throughput, batch size, memory and p95 latency first. Add GPU deployment profiles only if representative workloads benefit.

## SQL Server persistence design for increment 2

| Table | Purpose / key fields |
| --- | --- |
| LedgerImport | TenantId, EntityId, Period, SourceHash, status; idempotency key |
| LedgerLine | ImportId, SourceLineId, original accounting fields, rowversion |
| TaxonomyCategory | Dimension, Code, Version, jurisdiction, validity dates |
| ModelVersion | ArtifactHash, preprocessing version, taxonomy version, metrics, deployment state |
| ClassificationSuggestion | LineId, ModelVersionId, three proposed codes, status, created time |
| SuggestionEvidence | SuggestionId, reviewed line ID, cosine similarity and rank |
| ClassificationReview | LineId, accepted/corrected codes, reviewer, timestamp, reason; append-only history |
| LedgerEmbedding | LineId, model and preprocessing versions, dimension, vector payload |

Include tenant scope in keys and queries; enforce category references and review concurrency in the database/API. Keep original imports and review history distinct from model suggestions. Store currencies and amounts with appropriate SQL types, not floating point. Start with filtered candidate retrieval from SQL and in-process scoring; evaluate vector indexing only after volume justifies it. Never mix embedding versions.

Angular workflow: import preview and validation → review queue → three classification controls with evidence → accept/correct → audit history. Split mixed lines and flag insufficient evidence. Authorization, tenant checks and audit writes belong in the API.

## Optional ecosystem tools

| Tool | Where it fits |
| --- | --- |
| Microsoft.ML | Training, preprocessing, evaluation and baseline models in .NET; add during increment 3. |
| Microsoft.ML.OnnxTransformer | Optional ONNX execution inside an ML.NET pipeline (`ApplyOnnxModel`), when that pipeline is useful. Direct ONNX inference already covers the current adapter. |
| Microsoft.ML.OnnxRuntime.Gpu | Alternative deployment package/provider for NVIDIA CUDA; validate runtime/CUDA/cuDNN compatibility and benchmark before adopting. |
| Microsoft.ML.OnnxRuntime.DirectML | Alternative Windows GPU deployment; validate supported operators and provider constraints. |
| Netron | Inspect exported tensor names, shapes, operators and outputs during development; it does not train or validate prediction quality. |

Use one deliberate ONNX Runtime native package/provider profile per deployment, rather than installing CPU, CUDA and DirectML packages together. GPU support is not enabled in this increment.

References: [ONNX Runtime C#](https://onnxruntime.ai/docs/get-started/with-csharp.html), [TensorPrimitives.CosineSimilarity](https://learn.microsoft.com/dotnet/api/system.numerics.tensors.tensorprimitives.cosinesimilarity), [ML.NET ONNX transforms](https://learn.microsoft.com/dotnet/api/microsoft.ml.onnxcatalog.applyonnxmodel), [Netron](https://github.com/lutzroeder/netron).
