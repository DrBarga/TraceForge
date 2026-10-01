import { cp, mkdir, readFile, writeFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";
import path from "node:path";

const root = path.dirname(fileURLToPath(import.meta.url));
const output = path.join(root, "dist");
const escapeHtml = (value) =>
  value
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;");

function inline(value) {
  return escapeHtml(value)
    .replace(
      /\[([^\]]+)\]\((mailto:[^\s)]+|https:\/\/[^\s)]+)\)/g,
      '<a href="$2">$1</a>',
    )
    .replace(/\*\*([^*]+)\*\*/g, "<strong>$1</strong>")
    .replace(/`([^`]+)`/g, "<code>$1</code>");
}

function renderBrief(markdown) {
  return markdown
    .trim()
    .split(/\r?\n\s*\r?\n/)
    .map((block) => {
      if (block.startsWith("# ")) return `<h1>${inline(block.slice(2))}</h1>`;
      if (block.startsWith("## ")) return `<h2>${inline(block.slice(3))}</h2>`;
      if (block.startsWith("- ")) {
        const items = block.split(/\r?\n/).map((line) => {
          if (!line.startsWith("- "))
            throw new Error(
              "Unsupported list continuation in data-handling document",
            );
          return `<li>${inline(line.slice(2))}</li>`;
        });
        return `<ul>${items.join("")}</ul>`;
      }
      return `<p>${inline(block.replace(/\r?\n/g, " "))}</p>`;
    })
    .join("\n");
}

await mkdir(path.join(output, "downloads"), { recursive: true });
await cp(path.join(root, "public"), output, { recursive: true });
const markdown = await readFile(
  path.join(root, "..", "docs", "DATA_HANDLING.md"),
  "utf8",
);
const securityPage = `<!doctype html>
<html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><meta name="description" content="What TraceForge 1.0.0 collects, where diagnostic data is stored, retention, access controls, and how to delete or share files."><title>Data handling &amp; security — TraceForge</title><link rel="icon" href="/favicon.svg"><link rel="stylesheet" href="/styles.css"></head>
<body><a class="skip-link" href="#main">Skip to content</a><header class="header"><a class="brand" href="/"><img src="/favicon.svg" width="30" height="30" alt="">TraceForge<span class="brand-dot">/</span></a><nav aria-label="Main navigation"><a href="/#workbench">Explore</a><a href="/security" aria-current="page">Data &amp; security</a><a href="/#about">Maintainer</a></nav><a class="nav-download" href="https://github.com/DrBarga/TraceForge/releases/tag/v1.0.0">Get 1.0.0 ↗</a></header>
<main id="main" class="document container"><span class="eyebrow">DESKTOP RELEASE 1.0.0</span><div class="document-actions"><a class="button button-primary" href="/downloads/TraceForge-data-handling.pdf">Download the PDF ↓</a><a class="text-link" href="https://github.com/DrBarga/TraceForge/blob/master/docs/DATA_HANDLING.md">Read the source document ↗</a></div>${renderBrief(markdown)}<section class="website-note"><h2>About this website</h2><p>The product site serves static pages with no analytics, tracking cookies, external fonts, or contact forms. Vercel processes normal requests to host it. Download links lead to GitHub and contact links open your email client. This website does not receive desktop diagnostic data.</p></section></main><footer class="footer container"><a class="brand" href="/">TraceForge<span class="brand-dot">/</span></a><span>Bohdan Zelenskyi · 2026</span><div><a href="/">Product</a><a href="mailto:bogdan.zelya.s@gmail.com">Contact</a><a href="https://github.com/DrBarga/TraceForge">GitHub</a></div></footer></body></html>`;
await writeFile(path.join(output, "security.html"), securityPage);
await writeFile(
  path.join(output, "downloads", "TraceForge-data-handling.md"),
  markdown,
);
await cp(
  path.join(root, "..", "output", "pdf", "TraceForge-data-handling.pdf"),
  path.join(output, "downloads", "TraceForge-data-handling.pdf"),
);
const config = JSON.parse(
  await readFile(path.join(root, "vercel.json"), "utf8"),
);
delete config.buildCommand;
delete config.outputDirectory;
await writeFile(
  path.join(output, "vercel.json"),
  `${JSON.stringify(config, null, 2)}\n`,
);
console.log("Built TraceForge product site and data-handling downloads.");
