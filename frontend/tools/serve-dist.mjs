import { createServer, request as httpRequest } from 'node:http';
import { readFile, stat } from 'node:fs/promises';
import { extname, join, normalize, resolve } from 'node:path';

const root = resolve('dist/buildmind-frontend/browser');
const port = Number(process.env['PORT'] || 4300);
const apiProxy = process.env['BUILDMIND_API_PROXY'];
if (apiProxy && !/^http:\/\/(?:127\.0\.0\.1|localhost)(?::\d+)?$/.test(apiProxy)) {
  throw new Error('BUILDMIND_API_PROXY must be a loopback HTTP origin.');
}
const types = {
  '.css': 'text/css; charset=utf-8',
  '.html': 'text/html; charset=utf-8',
  '.ico': 'image/x-icon',
  '.js': 'text/javascript; charset=utf-8',
  '.json': 'application/json; charset=utf-8',
  '.png': 'image/png',
  '.svg': 'image/svg+xml',
  '.webp': 'image/webp',
  '.woff2': 'font/woff2',
};

createServer(async (request, response) => {
  try {
    const pathname = decodeURIComponent(new URL(request.url ?? '/', 'http://localhost').pathname);
    if (apiProxy && pathname.startsWith('/api/')) {
      const target = new URL(request.url ?? pathname, apiProxy);
      const headers = { ...request.headers, host: target.host };
      delete headers.origin;
      delete headers.referer;
      const upstream = httpRequest(
        target,
        { method: request.method, headers },
        (upstreamResponse) => {
          const responseHeaders = { ...upstreamResponse.headers };
          delete responseHeaders['set-cookie'];
          response.writeHead(upstreamResponse.statusCode ?? 502, responseHeaders);
          upstreamResponse.pipe(response);
        },
      );
      upstream.on('error', () => {
        if (!response.headersSent) response.writeHead(502);
        response.end('Local API unavailable');
      });
      request.pipe(upstream);
      return;
    }
    const candidate = resolve(root, `.${normalize(pathname)}`);
    if (!candidate.startsWith(root)) throw new Error('outside distribution root');
    let file = candidate;
    try {
      if ((await stat(file)).isDirectory()) file = join(file, 'index.html');
    } catch {
      file = join(root, 'index.html');
    }
    const body = await readFile(file);
    response.writeHead(200, {
      'content-type': types[extname(file)] ?? 'application/octet-stream',
      'cache-control': 'no-store',
    });
    response.end(body);
  } catch {
    response.writeHead(404);
    response.end('Not found');
  }
}).listen(port, '127.0.0.1', () => console.log(`BuildMind preview http://127.0.0.1:${port}`));
