# Working workbook pipeline

The implemented order is **upload → background raw SQL import → import succeeds → user clicks Categorize → background ML batches**. Import never requests predictions. Categorization is scoped to client, workbook and sheet type. The Angular 21 client provides upload, import status, sheet selection, Categorize actions, run/batch progress and pagination. Aspire launches Angular and the API together.

## Run and use

```powershell
dotnet run --project CorporateTaxCategorizationHost
```

Open `http://localhost:4200/`. The API remains at `http://localhost:5075`; Angular proxies `/api` requests through the Aspire-provided API endpoint. Client `SYNTH-001` has the supplied 150-row workbook: 50 rows in each DPL, TFA and CRD sheet. Select the workbook to upload, or inspect the sample after the import job finishes. Only successfully imported workbooks display Categorize buttons. Each type's button is disabled while its run is active or when no pending rows remain.

SQL Server: `localhost`, database `CorporateTaxCategorization`, Windows integrated authentication. Override with `ConnectionStrings__TaxDatabase`. SQL Server must be available before the API starts.

`Database:ResetOnStartup` is **true by default**, as requested for this demo. Every API startup deletes/recreates the app's `tax` schema and removes its six legacy `dbo` tables, clearing imports, runs and predictions. The reset uses one SQL transaction and never drops the database. If other objects or dependencies prevent removal, startup fails and the transaction rolls back. Set `Database__ResetOnStartup=false` to retain SQL data across restarts. A fresh database is created if absent; persistent deployments need migrations for future schema changes.

After a reset, archived original workbooks and the bundled sample are copied into the inbox with content-hash filenames. The background import worker reloads raw SQL rows, deduplicating identical content per client. It is the worker, not schema initialization, that imports data. Nothing queues categorization automatically; the UI buttons remain the trigger. Import IDs change after each reset, so the `.http` file uses a named import-list request to resolve the current ID.

The Angular app requires Node/npm (`npm ci` for standalone installation); Aspire installs npm dependencies and starts the dev server on port 4200. `npm run build` creates production assets, which need a same-origin `/api` reverse proxy when deployed.

## Directory import job

Default directory: `data/workbooks/inbox/{CLIENT-ID}` relative to the repository. Example:

```powershell
New-Item -ItemType Directory -Force data/workbooks/inbox/SYNTH-001
Copy-Item outputs/synthetic-workbooks/SYNTH-001-FY2026.xlsx data/workbooks/inbox/SYNTH-001/
```

Override `WorkbookStorage__Root` to change the storage root. The API's multipart upload stages a `.uploading` file and atomically renames it to `.xlsx` when copying finishes. For external producers, use the same staging/rename approach. The background worker scans every 3 seconds, requires an unchanged file size/timestamp on consecutive scans and exclusive read access, then imports. It also discovers files left after a restart. It does not follow inbox directory/file links.

The importer checks workbook limits, sheet names, headers, client/entity/period consistency, duplicate record IDs, amounts and currencies. It stores the original workbook bytes, raw cells and normalized fields in one SQL transaction. Only after commit does the workbook appear as imported. Completed files move to `archive/{CLIENT-ID}`. Invalid files move to `rejected/{CLIENT-ID}` with an error JSON file; transient IO/database failures leave files available for retry. `GET .../workbooks/inbox` shows waiting files and rejection messages. A matching client/file SHA256 returns the existing import rather than duplicating rows. Archive failures can safely retry import because of that identity.

## Excel template

Exactly three sheets, named `DPL`, `TFA`, `CRD`. Header row 1:

`ClientId, EntityId, Period, RecordId, AccountCode, Description, Amount, Currency, IsSynthetic, SourceReference`

Use one entity and period per workbook, 1–1000 rows per sheet, descriptions up to 2000 characters and file size up to 5 MB. This initial template accepts values only, not formulas; arbitrary client header mappings are not implemented yet. Monetary values use SQL decimal(19,4). Raw records and source locations are retained separately from predictions.

The provided workbook has no expected-category column. Synthetic reference category descriptions are seeded into a separate SQL table only when the user requests prediction for a synthetic workbook. Synthetic files must use `SYNTH-` client IDs, and synthetic category references cannot be used for real imports. Real client reference-data curation and approval remain to be added; real data without compatible references yields a failed run explaining the missing prerequisite.

## Button-triggered prediction job

`POST /api/clients/{clientId}/workbooks/{importId}/categorize?type=TFA` returns HTTP 202 and persists a queued run. DPL and CRD work identically. Duplicate clicks return the existing active run; completed types with no pending rows return HTTP 409.

The prediction worker reads only queued/running categorization requests. It retrieves up to **20 persisted pending SQL rows** for the selected client/import/type in stable row-ID order. It generates MiniLM embeddings and ranks that client's compatible type-specific category descriptions. These are semantic nearest-category suggestions, not a trained tax classifier or calibrated confidence estimates. It saves the top candidates, suggested category, model/preprocessing version, truncation flag and review requirement. Imported rows are never used as labelled reference data or expected answers.

Each batch transaction saves predictions, batch membership, row review state and run progress together. SQL application locks prevent two workers or duplicate requests from claiming the same client/import/type simultaneously; a unique prediction index prevents duplicate results. A crash before commit leaves those rows pending, and an interrupted running job resumes after restart. Runtime failures mark the run failed with pending rows preserved; another user request can retry. A 50-row type completes as **20 + 20 + 10**, with every result in `NeedsReview`.

## API

- `POST /api/clients/{clientId}/workbooks`: multipart `file`; returns upload ID and queued import status.
- `GET /api/clients/{clientId}/workbooks/inbox`: waiting/rejected files.
- `GET /api/clients/{clientId}/workbooks`: successful imports, raw/predicted counts and per-type `canCategorize` flags.
- `POST /api/clients/{clientId}/workbooks/{id}/categorize?type=DPL`: explicit prediction request.
- `GET /api/clients/{clientId}/workbooks/{id}/runs`: categorization progress/errors.
- `GET /api/clients/{clientId}/workbooks/{id}/batches`: saved batch sizes and run IDs.
- `GET /api/clients/{clientId}/workbooks/{id}/records?type=TFA&skip=0&take=20`: raw descriptions, statuses, predictions and evidence; pagination is capped at 20.

Database tables: `Imports`, `RawRecords`, `CategoryReferences`, `CategorizationRuns`, `PredictionBatches`, `Predictions`. The original single-description `/api/categorization` endpoints remain separate demonstration endpoints.

This is a local synthetic-data increment: client filters are implemented, but authentication/authorization for real multi-client deployment, durable multi-node file leasing, spreadsheet mapping profiles and approval/export workflows are still outstanding. No test projects were added; implementation was checked with builds, actual workbook import, SQL counts and manual API requests.
