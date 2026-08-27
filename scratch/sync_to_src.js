const fs = require('fs');
const path = require('path');

const rootDir = path.join(__dirname, '..');
const targetDir = path.join(__dirname, '..', 'src', 'AIProposalEvaluator');

function copyRecursiveSync(src, dest) {
    const exists = fs.existsSync(src);
    const stats = exists && fs.statSync(src);
    const isDirectory = exists && stats.isDirectory();
    if (isDirectory) {
        if (!fs.existsSync(dest)) {
            fs.mkdirSync(dest, { recursive: true });
        }
        fs.readdirSync(src).forEach((childItemName) => {
            if (childItemName === 'bin' || childItemName === 'obj' || childItemName === 'scratch' || childItemName === '.git' || childItemName === 'src') return;
            copyRecursiveSync(path.join(src, childItemName), path.join(dest, childItemName));
        });
    } else {
        fs.copyFileSync(src, dest);
    }
}

const items = ['App.razor', '_Imports.razor', 'Program.cs', 'Pages', 'Components', 'Models', 'Services', 'Layout', 'wwwroot'];

items.forEach(item => {
    const srcPath = path.join(rootDir, item);
    const destPath = path.join(targetDir, item);
    if (fs.existsSync(srcPath)) {
        copyRecursiveSync(srcPath, destPath);
        console.log(`Copied ${item} to src/AIProposalEvaluator/`);
    }
});

// Rename frontend.csproj to AIProposalEvaluator.csproj in targetDir
fs.copyFileSync(path.join(rootDir, 'frontend.csproj'), path.join(targetDir, 'AIProposalEvaluator.csproj'));
console.log('Copied frontend.csproj to src/AIProposalEvaluator/AIProposalEvaluator.csproj');
