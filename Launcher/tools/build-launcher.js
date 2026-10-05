// Gera um executavel portatil unico para Windows x64, e entao aplica o pipeline central de
// assinatura (Tools/Signing) em vez de deixar o electron-builder assinar implicitamente com
// qualquer certificado de assinatura de codigo presente no certificate store da maquina (isso
// causava assinatura silenciosa com o certificado autoassinado de desenvolvimento). Consulte
// Docs/CodeSigning.md para configurar SIGNING_MODE.
const { execFileSync } = require('child_process');
const fs = require('fs');
const path = require('path');

const root = path.join(__dirname, '..');
const projectRoot = path.join(root, '..');
const cli = path.join(root, 'node_modules', 'electron-builder', 'out', 'cli', 'cli.js');
if (!fs.existsSync(cli)) { console.error('electron-builder nao instalado. Rode: npm install'); process.exit(1); }

const out = path.join(root, 'dist');
fs.rmSync(out, { recursive: true, force: true });

// Impede que o electron-builder detecte e use automaticamente qualquer certificado de
// assinatura de codigo presente no store do Windows - a assinatura passa a ser sempre
// explicita, controlada pela variavel SIGNING_MODE (ver Tools/Signing/Sign-WindowsBuild.ps1).
const buildEnv = { ...process.env, CSC_IDENTITY_AUTO_DISCOVERY: 'false' };
execFileSync(process.execPath, [cli, '--win', 'portable', '--x64', '--publish', 'never'], { cwd: root, stdio: 'inherit', env: buildEnv });

const launcher = path.join(out, 'GameProjectKG-Launcher.exe');
if (!fs.existsSync(launcher)) {
    console.error('O executavel portatil nao foi gerado em ' + launcher);
    process.exit(1);
}
console.log('Launcher portatil: ' + launcher);

const signingMode = process.env.SIGNING_MODE;
if (!signingMode || signingMode === 'None') {
    console.warn('CODE SIGNING NOT CONFIGURED: defina SIGNING_MODE (LocalCertificate ou ' +
        'AzureArtifactSigning) e as variaveis correspondentes (ver Docs/CodeSigning.md) para ' +
        'assinar o Launcher antes de distribuir publicamente. Build atual NAO esta assinada.');
    process.exit(0);
}

console.log(`Assinando ${launcher} (modo ${signingMode}) ...`);
const psArgs = ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-File',
    path.join(projectRoot, 'Tools', 'Signing', 'Sign-WindowsBuild.ps1'), '-Files', launcher];
execFileSync('powershell.exe', psArgs, { cwd: projectRoot, stdio: 'inherit' });

console.log('Verificando assinatura ...');
const verifyArgs = ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-File',
    path.join(projectRoot, 'Tools', 'Signing', 'Verify-Signature.ps1'), '-Files', launcher];
try {
    execFileSync('powershell.exe', verifyArgs, { cwd: projectRoot, stdio: 'inherit' });
} catch (e) {
    console.error('Verificacao de assinatura falhou - release abortado.');
    process.exit(1);
}
