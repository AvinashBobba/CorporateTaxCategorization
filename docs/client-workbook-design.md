# Client workbook ingestion and classification design

Implementation update: [the working pipeline](workbook-pipeline.md) now includes Angular upload/status/type-selection screens and imports the supplied fixed Excel template through an inbox background job into SQL Server. Prediction starts only after the user clicks Categorize for a client/workbook/type and runs in batches of 20. The broader mapping and approval architecture below remains the intended later design.

## Confirmed input and scope

Each client supplies an Excel workbook with separate sheets for DPL (detailed profit and loss), TFA (tangible fixed assets), and CRD (capital/revenue divide). Sheet names, header positions, column names and category mappings may differ by client. The system must preserve client, entity, period, workbook, sheet and source-row identity throughout processing. Workbook ingestion and persistence are now implemented for the fixed synthetic template; arbitrary client mappings and reviewed taxonomies remain future work.

Assume `.xlsx` for the first increment. Confirm actual headers, row granularity, formulas and required category codes from representative anonymized workbooks before implementing parsers. Do not assume a DPL line is a transaction or a TFA line is an individual acquisition: they may be account totals or asset-class movements. Classify at the supplied granularity. Relationships between sheets require explicit source references or reviewed links, never row position.

## Processing flow

Angular selects authorized client, entity and period → API stores original workbook → detects sheets and proposes a saved column mapping → user reviews import preview → API normalizes valid rows into SQL → background classification job processes each sheet type → Angular shows evidence and exceptions → accountant accepts/corrects → export approved results with source references.

Use a versioned client/template profile to map worksheet names, header rows, columns, date/currency formats, amount signs, row grain and excluded subtotal rows. Support explicit user assignment of a sheet to DPL, TFA or CRD; an unknown name or missing required field produces an import issue rather than silent guessing. Preview missing/duplicate sheets and let the user resolve them. Quarantine invalid rows while keeping their source values and validation messages.

## Separate classification tasks

| Sheet | Candidate fields to map, subject to real templates | Classification output |
| --- | --- | --- |
| DPL | Account code/name, description, current/prior amount, reporting hierarchy | Client-approved DPL category code |
| TFA | Asset/reference, description, existing class, additions/disposals, dates, cost/movement values | Client-approved asset category code; separate review flags |
| CRD | Entry/reference, description, account, amount, invoice/supporting reference | Client-approved capital/revenue/mixed/review category |

Do not force every row to receive all three labels. A DPL row requests DPL classification; TFA and CRD use their own category sets, validation and reviewed history. A reviewed cross-sheet link can provide context, but should not silently propagate a decision. In particular, accounting asset classification is not a capital-allowance decision.

## Decision pipeline

1. Validate the normalized row and applicable taxonomy version; missing required context produces `NeedsInformation`.
2. Apply versioned accountant-approved account/category mappings scoped to the client and sheet type. Record the mapping as evidence and its version.
3. For unresolved rows, encode description plus named textual context (for example account name and asset class) using the same model/preprocessing version as candidate history. Use structured amounts/dates for validation or later trained features; embedding numerical text alone is not a financial decision method.
4. Retrieve only authorized, approved examples for the client, compatible sheet type, taxonomy and embedding version. Add explicit entity/period policy where required. Never use another client's raw rows as precedents. An approved shared taxonomy description may be an explicitly labelled alternative when history is absent.
5. Return candidates with source-row evidence and cosine similarity. Negative, weak or conflicting matches and absent history may produce no proposed category. Initially keep every result in `NeedsReview`; similarity does not establish a calibrated probability. Do not simply adopt the first returned match or an arbitrary similarity threshold.
6. Save accountant acceptance/correction as a separate audited event. It becomes future precedent only after approval. A future trained classifier can supplement retrieval after evaluation per client/sheet/category.

## SQL Server entities

- Client/Entity and user access scope: apply authorization before any workbook, row, vector or review lookup. Client IDs supplied in requests alone do not grant access.
- WorkbookImport: client, entity, period, original file location/hash, profile version, lifecycle state and idempotency key scoped to client/entity/period/profile.
- WorksheetImport: workbook, source sheet name, DPL/TFA/CRD type, header row and mapping version.
- SourceRow: worksheet, original row number/reference, raw cells, formulas/cached-value provenance and validation status. Preserve originals for traceability.
- NormalizedRow plus DplRow/TfaRow/CrdRow details: canonical fields and row grain. Use SQL decimal for money and separate currency; never float monetary values.
- Category/ClientCategoryMapping: sheet type, category code and taxonomy/policy versions, jurisdiction applicability and effective dates where relevant.
- RowEmbedding: source row, model revision, preprocessing version, dimension and vector. Never compare incompatible versions.
- ClassificationRun/RowSuggestion/SuggestionEvidence: run state, proposed category, candidate scores, mapping/model versions and reviewed precedent IDs.
- ClassificationReview: immutable decision history with row, category, reviewer, timestamp, reason and concurrency token. Reprocessing creates a new suggestion, not an overwritten approval.

Store workbook bytes in controlled file/blob storage, with metadata and normalized rows in SQL. Do not evaluate macros or external links. Surface formula cells with missing/stale cached values for review; parsing is not recalculation. Exclude blank rows, headers and subtotals using the template profile, and reconcile row counts/totals without double counting. Keep identical rows distinct by source location; flag possible duplicates separately. A content hash supports retry handling, not automatic deduplication across clients or periods.

## Proposed API contracts

| Endpoint | Purpose |
| --- | --- |
| POST /api/clients/{clientId}/workbooks | Multipart upload with entity, period and optional template profile; return import ID and processing status |
| GET /api/clients/{clientId}/workbooks/{id} | Import state, detected sheets, validation summary and job progress |
| PUT /api/clients/{clientId}/workbooks/{id}/mapping | Save validated sheet/header/column assignments and profile version |
| GET /api/clients/{clientId}/workbooks/{id}/rows?sheetType=DPL | Paginated preview/review rows; also filter TFA or CRD |
| POST /api/clients/{clientId}/workbooks/{id}/categorization-runs | Start idempotent asynchronous run; HTTP 202 with run ID |
| GET /api/clients/{clientId}/workbooks/{id}/categorization-runs/{runId} | Progress and failure summary |
| GET /api/clients/{clientId}/workbooks/{id}/rows/{rowId}/suggestion | Sheet-specific proposed code, evidence, versions and review state |
| POST /api/clients/{clientId}/workbooks/{id}/rows/{rowId}/reviews | Accept/correct with expected row version; reject stale updates |
| GET /api/clients/{clientId}/workbooks/{id}/export | Export with DPL/TFA/CRD source references and explicit approval status |

New suggestion responses should include client/entity/period, import/sheet/row IDs, sheet type, source description, proposed category or null, decision method, review status, candidate evidence, model/preprocessing/taxonomy versions and truncation/validation messages. Scores should be named `similarity`; do not call them confidence. The existing `/api/categorization/suggestions` endpoint is a demo contract until replaced by this contextual workflow.

## Incremental implementation

1. Obtain representative anonymized workbook structures and agree category codes for each sheet type. Implement upload, sheet mapping and preview first, with SQL/Aspire storage integration.
2. Add normalized persistence and manual category review separately for DPL, TFA and CRD. Preserve originals and provenance.
3. Replace synthetic examples with approved, scoped SQL history; add background ONNX suggestions with review and export.
4. Evaluate performance and categorization quality on representative held-out client/period data before changing the review requirement or adding GPU execution.

User preference: do not add test projects or tests unless requested. Builds and manual API checks can be used for implementation verification.
