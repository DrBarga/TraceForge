import { createServer } from "node:http";
import { readFile, stat } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

const root = path.join(path.dirname(fileURLToPath(import.meta.url)), "dist");
const types = {
  ".html": "text/html; charset=utf-8",
  ".css": "text/css; charset=utf-8",
  ".js": "text/javascript; charset=utf-8",
  ".svg": "image/svg+xml",
  ".pdf": "application/pdf",
  ".md": "text/plain; charset=utf-8",
  ".txt": "text/plain; charset=utf-8",
};
const config = JSON.parse(
  await readFile(path.join(root, "vercel.json"), "utf8"),
);

createServer(async (request, response) => {
  try {
    const pathname = decodeURIComponent(
      new URL(request.url, "http://localhost").pathname,
    );
    if (pathname.includes("\0")) throw new Error("Invalid path");
    let file = path.resolve(root, `.${pathname}`);
    if (!file.startsWith(`${root}${path.sep}`) && file !== root) {
      response.writeHead(403).end();
      return;
    }
    if (pathname === "/") file = path.join(root, "index.html");
    else if (!path.extname(file)) file += ".html";
    let status = 200;
    if (
      !(await stat(file).catch(() => null))?.isFile() ||
      path.basename(file) === "vercel.json"
    ) {
      file = path.join(root, "404.html");
      status = 404;
    }
    const headers = Object.fromEntries(
      config.headers[0].headers.map(({ key, value }) => [key, value]),
    );
    // HTTPS upgrading applies on the deployed site, not the HTTP-only local preview.
    headers["Content-Security-Policy"] = headers[
      "Content-Security-Policy"
    ].replace("; upgrade-insecure-requests", "");
    response.writeHead(status, {
      ...headers,
      "Content-Type": types[path.extname(file)] ?? "application/octet-stream",
    });
    response.end(await readFile(file));
  } catch {
    response.writeHead(400).end("Bad request");
  }
}).listen(4173, "127.0.0.1", () =>
  console.log("TraceForge preview: http://localhost:4173"),
);
