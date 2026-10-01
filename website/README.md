# TraceForge website

A static product site with locally served CSS and SVG assets. No client framework, package dependencies, analytics, cookies, contact form, or external fonts are used. Hosting providers can retain normal request logs.

## Build and preview

```powershell
cd website
npm run build
npm run dev
```

Open http://localhost:4173. The development server binds to 127.0.0.1.

`build.mjs` copies the public assets to `dist`, renders the data-handling page from `../docs/DATA_HANDLING.md`, and includes the PDF from `../output/pdf/TraceForge-data-handling.pdf`. Keep these files together when deploying from a checkout.

## Vercel

Import the TraceForge GitHub repository, set the root directory to `website`, and enable access to files outside the root directory. The framework is Other, build command is `node build.mjs`, and output directory is `dist`; these settings are also declared in `vercel.json`.

For a direct deployment of the already built static files, use the root directory `dist` with no build command. Its copied `vercel.json` preserves clean URLs and response headers.

Production deployment is public. No environment variables or secrets are needed by the site. Vercel authentication belongs to the deployment account and must never be committed.

To update the data-handling PDF, run `scripts/build-data-handling-pdf.py` from the repository root with Python and ReportLab. The Markdown document is the source for both formats. Rebuild the site afterwards.
