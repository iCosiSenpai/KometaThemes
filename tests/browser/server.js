// Test host: the plugin's embedded files, a Jellyfin-like shell and the mock API.
const http = require('node:http');
const fs = require('node:fs');
const path = require('node:path');
const { MockApi } = require('./mock-api');

const root = path.resolve(__dirname, '..', '..');
const plugin = path.join(root, 'Jellyfin.Plugin.KometaThemes');
const resources = {
  KometaThemes: path.join(plugin, 'Configuration', 'configPage.html'),
  KometaThemesJs: path.join(plugin, 'Web', 'kometa.js'),
  KometaThemesCss: path.join(plugin, 'Web', 'kometa.css'),
  KometaThemesIcon: path.join(plugin, 'Web', 'assets', 'kometathemes-icon.png')
};
const types = { '.html': 'text/html; charset=utf-8', '.css': 'text/css; charset=utf-8', '.js': 'text/javascript; charset=utf-8', '.png': 'image/png' };
const api = new MockApi();

function send(response, status, type, body) {
  response.writeHead(status, { 'Content-Type': type, 'Cache-Control': 'no-store' });
  response.end(body);
}

function poster(id) {
  let hash = 0;
  for (const c of id) hash = (hash * 31 + c.charCodeAt(0)) >>> 0;
  const hue = hash % 360;
  return `<svg xmlns="http://www.w3.org/2000/svg" width="80" height="120" viewBox="0 0 80 120"><defs><linearGradient id="g" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="hsl(${hue},55%,45%)"/><stop offset="1" stop-color="hsl(${(hue + 40) % 360},60%,25%)"/></linearGradient></defs><rect width="80" height="120" fill="url(#g)"/></svg>`;
}

const server = http.createServer((request, response) => {
  const url = new URL(request.url, 'http://127.0.0.1:4173');
  if (url.pathname === '/healthz') return send(response, 200, 'text/plain', 'ok');
  if (url.pathname === '/__reset') {
    api.reset(Object.fromEntries(url.searchParams));
    return send(response, 204, 'text/plain', '');
  }
  if (url.pathname === '/web/host.html') return send(response, 200, types['.html'], fs.readFileSync(path.join(__dirname, 'host.html')));
  if (url.pathname === '/web/item.html') return send(response, 200, types['.html'], fs.readFileSync(path.join(__dirname, 'item.html')));
  if (url.pathname === '/KometaThemes/ItemButton.js') return send(response, 200, types['.js'], fs.readFileSync(path.join(plugin, 'Web', 'item-button.js')));
  if (url.pathname === '/web/configurationpage') {
    const file = resources[url.searchParams.get('name')];
    if (!file) return send(response, 404, 'text/plain', 'unknown resource');
    return send(response, 200, types[path.extname(file)], fs.readFileSync(file));
  }
  if (/^\/Items\/[^/]+\/Images\/Primary$/.test(url.pathname)) {
    const itemId = url.pathname.split('/')[2];
    if (itemId.endsWith('000000000003')) return send(response, 404, 'text/plain', 'no image');
    return send(response, 200, 'image/svg+xml', poster(itemId));
  }
  if (url.pathname.startsWith('/KometaThemes/')) {
    if (request.headers.authorization !== 'MediaBrowser Token="test"') return send(response, 401, 'text/plain', '');
    let raw = '';
    request.on('data', (chunk) => { raw += chunk; });
    request.on('end', () => {
      const body = raw ? JSON.parse(raw) : undefined;
      const route = url.pathname.slice('/KometaThemes/'.length);
      const result = api.handle(request.method, route, Object.fromEntries(url.searchParams), body);
      setTimeout(() => {
        if (result.status === 204) return send(response, 204, 'text/plain', '');
        send(response, result.status, 'application/json; charset=utf-8', JSON.stringify(result.data));
      }, Number(process.env.KT_DELAY || 60));
    });
    return undefined;
  }
  return send(response, 404, 'text/plain', 'not found');
});

server.listen(Number(process.env.KT_PORT || 4173), '127.0.0.1');
