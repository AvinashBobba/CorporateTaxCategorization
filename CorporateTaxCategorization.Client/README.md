# Angular client

Angular 21 client for raw Excel upload, background SQL import status, DPL/TFA/CRD selection, button-triggered ML categorization, progress and paginated results.

From the repository root, run the complete app:

```powershell
dotnet run --project CorporateTaxCategorizationHost
```

Open http://localhost:4200/. Aspire starts the API and Angular dev server together; API requests go through the Angular proxy.

Standalone client development:

```powershell
npm ci
npm start
```

The standalone proxy defaults to http://localhost:5075. Override API_BASE_URL to change it. Node 22.12+ (22.x) is supported by the chosen Angular 21 release. Build production assets with `npm run build`; deploy them with an `/api` reverse proxy to ASP.NET Core. The current Aspire run uses the development server.

No tests or test targets are included, per project preference.
