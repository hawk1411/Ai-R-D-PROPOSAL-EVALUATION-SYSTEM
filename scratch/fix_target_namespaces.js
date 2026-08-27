const fs = require('fs');
const path = require('path');

const targetDir = path.join(__dirname, '..', 'src', 'AIProposalEvaluator');

function replaceInDir(dir) {
    const files = fs.readdirSync(dir);
    for (const file of files) {
        const fullPath = path.join(dir, file);
        const stat = fs.statSync(fullPath);
        if (stat.isDirectory()) {
            replaceInDir(fullPath);
        } else if (file.endsWith('.cs') || file.endsWith('.razor')) {
            let content = fs.readFileSync(fullPath, 'utf8');
            content = content.replace(/frontend\.Layout/g, 'AIProposalEvaluator.Layout');
            content = content.replace(/frontend\.Models/g, 'AIProposalEvaluator.Models');
            content = content.replace(/frontend\.Services/g, 'AIProposalEvaluator.Services');
            content = content.replace(/frontend\.Components/g, 'AIProposalEvaluator.Components');
            content = content.replace(/namespace frontend/g, 'namespace AIProposalEvaluator');
            content = content.replace(/using frontend;/g, 'using AIProposalEvaluator;');
            fs.writeFileSync(fullPath, content, 'utf8');
        }
    }
}

replaceInDir(targetDir);
console.log('Successfully updated all namespaces in src/AIProposalEvaluator');
