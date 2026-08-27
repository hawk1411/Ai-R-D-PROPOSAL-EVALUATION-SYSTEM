const fs = require('fs');
const path = require('path');

const frameworkDir = path.join(__dirname, '..', 'bin', 'Release', 'net10.0', 'publish', 'wwwroot', '_framework');

if (fs.existsSync(frameworkDir)) {
    const files = fs.readdirSync(frameworkDir);
    let count = 0;

    for (const file of files) {
        // Match fingerprinted files: name.<10-char-hash>.ext
        const match = file.match(/^(.+)\.([a-z0-9]{10})\.(wasm|js|dat|pdb|json)$/);
        if (match) {
            const baseName = match[1];
            const ext = match[3];
            const cleanName = `${baseName}.${ext}`;
            const srcPath = path.join(frameworkDir, file);
            const destPath = path.join(frameworkDir, cleanName);

            fs.copyFileSync(srcPath, destPath);
            count++;
        }
    }
    console.log(`Successfully created ${count} un-fingerprinted asset copies in _framework.`);
} else {
    console.error(`Framework directory not found: ${frameworkDir}`);
}
