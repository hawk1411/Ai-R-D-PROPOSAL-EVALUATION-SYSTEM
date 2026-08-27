const http = require('http');
const fs = require('fs');
const path = require('path');

const PORT = 5000;
const PUBLIC_DIR = path.join(__dirname, '..', 'bin', 'Release', 'net10.0', 'publish', 'wwwroot');

const MIME_TYPES = {
    '.html': 'text/html; charset=UTF-8',
    '.js': 'text/javascript; charset=UTF-8',
    '.mjs': 'text/javascript; charset=UTF-8',
    '.css': 'text/css; charset=UTF-8',
    '.json': 'application/json; charset=UTF-8',
    '.wasm': 'application/wasm',
    '.png': 'image/png',
    '.jpg': 'image/jpeg',
    '.svg': 'image/svg+xml',
    '.ico': 'image/x-icon',
    '.woff': 'font/woff',
    '.woff2': 'font/woff2',
    '.ttf': 'font/ttf',
    '.dat': 'application/octet-stream',
    '.pdb': 'application/octet-stream'
};

const server = http.createServer((req, res) => {
    // Enable CORS and disable cache during dev
    res.setHeader('Access-Control-Allow-Origin', '*');
    res.setHeader('Cache-Control', 'no-cache, no-store, must-revalidate');

    let reqUrl = req.url.split('?')[0];
    if (reqUrl === '/') reqUrl = '/index.html';

    let filePath = path.join(PUBLIC_DIR, reqUrl);

    // If file doesn't exist directly, check if it's SPA route (fallback to index.html ONLY for non-file requests)
    if (!fs.existsSync(filePath) || fs.statSync(filePath).isDirectory()) {
        const ext = path.extname(reqUrl);
        if (!ext) {
            filePath = path.join(PUBLIC_DIR, 'index.html');
        } else {
            res.writeHead(404, { 'Content-Type': 'text/plain' });
            return res.end(`404 Not Found: ${reqUrl}`);
        }
    }

    const ext = path.extname(filePath).toLowerCase();
    const contentType = MIME_TYPES[ext] || 'application/octet-stream';

    fs.readFile(filePath, (err, data) => {
        if (err) {
            res.writeHead(500, { 'Content-Type': 'text/plain' });
            return res.end(`500 Internal Server Error: ${err.message}`);
        }
        res.writeHead(200, { 'Content-Type': contentType });
        res.end(data);
    });
});

server.listen(PORT, () => {
    console.log(`Blazor WebAssembly Server running at http://localhost:${PORT}`);
});
