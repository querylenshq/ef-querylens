import fs from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';
import { spawnSync } from 'child_process';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

const pluginRoot = path.resolve(__dirname, '..');
const repoRoot = path.resolve(pluginRoot, '..', '..', '..');
const lspOutputRoot = path.join(pluginRoot, 'server');
const daemonOutputRoot = path.join(pluginRoot, 'daemon');
const runtimeIdentifier = process.env.QUERYLENS_RUNTIME_IDENTIFIER ?? getDefaultRuntimeIdentifier();
const buildConfiguration = 'Release';
const targetFramework = 'net10.0';

const lspSource = resolveRuntimeDirectory(
  'EFQueryLens.Lsp',
  'EFQueryLens.Lsp.dll',
  repoRoot,
  runtimeIdentifier,
  buildConfiguration,
  targetFramework
);
const daemonSource = resolveRuntimeDirectory(
  'EFQueryLens.Daemon',
  'EFQueryLens.Daemon.dll',
  repoRoot,
  runtimeIdentifier,
  buildConfiguration,
  targetFramework
);

const lspDestination = lspOutputRoot;
const daemonDestination = daemonOutputRoot;

copyDirectory(lspSource, lspDestination);
copyDirectory(daemonSource, daemonDestination);

console.log(`[EFQueryLens] bundled runtime prepared:`);
console.log(`  lsp:    ${lspSource} -> ${lspDestination}`);
console.log(`  daemon: ${daemonSource} -> ${daemonDestination}`);
console.log(`  rid:    ${runtimeIdentifier}`);

function resolveRuntimeDirectory(
  projectName,
  requiredFileName,
  repositoryRoot,
  runtime,
  configuration,
  framework
) {
  const projectPath = path.join(repositoryRoot, 'src', projectName, `${projectName}.csproj`);
  const publishDir = path.join(
    repositoryRoot,
    'src',
    projectName,
    'bin',
    configuration,
    framework,
    runtime,
    'publish'
  );

  publishProject(projectPath, runtime, configuration, framework);

  if (fs.existsSync(path.join(publishDir, requiredFileName))) {
    return publishDir;
  }

  throw new Error(
    `[EFQueryLens] Could not find ${requiredFileName} for ${projectName} at ${publishDir}.`
  );
}

function publishProject(projectPath, runtime, configuration, framework) {
  const result = spawnSync(
    'dotnet',
    [
      'publish',
      projectPath,
      '-c',
      configuration,
      '-f',
      framework,
      '-r',
      runtime,
      '--self-contained',
      'false',
      '/p:UseAppHost=true',
    ],
    {
      stdio: 'inherit',
      cwd: repoRoot,
    }
  );

  if (result.status !== 0) {
    throw new Error(`[EFQueryLens] dotnet publish failed for ${projectPath} (exit code ${result.status ?? 'unknown'}).`);
  }
}

function getDefaultRuntimeIdentifier() {
  if (process.platform === 'win32') {
    return process.arch === 'arm64' ? 'win-arm64' : 'win-x64';
  }

  if (process.platform === 'darwin') {
    return process.arch === 'arm64' ? 'osx-arm64' : 'osx-x64';
  }

  if (process.platform === 'linux') {
    return process.arch === 'arm64' ? 'linux-arm64' : 'linux-x64';
  }

  throw new Error(`[EFQueryLens] Unsupported platform '${process.platform}'. Set QUERYLENS_RUNTIME_IDENTIFIER explicitly.`);
}

function copyDirectory(sourceDir, destinationDir) {
  fs.mkdirSync(path.dirname(destinationDir), { recursive: true });

  try {
    removeDirectoryWithRetry(destinationDir);
    fs.cpSync(sourceDir, destinationDir, { recursive: true, force: true });
    return;
  } catch (error) {
    if (!isFileLockError(error)) {
      throw error;
    }
  }

  console.warn(
    `[EFQueryLens] Could not fully replace ${destinationDir} because files are locked. Falling back to in-place copy.`
  );
  copyDirectoryBestEffort(sourceDir, destinationDir);
}

function removeDirectoryWithRetry(directoryPath) {
  if (!fs.existsSync(directoryPath)) {
    return;
  }

  let lastError;

  try {
    fs.rmSync(directoryPath, {
      recursive: true,
      force: true,
      maxRetries: 15,
      retryDelay: 100,
    });
    return;
  } catch (error) {
    lastError = error;
    if (!isFileLockError(error)) {
      throw error;
    }
  }

  const processNames = getLikelyLockedRuntimeProcesses(directoryPath);
  if (processNames.length > 0) {
    console.warn(
      `[EFQueryLens] Runtime files appear locked at ${directoryPath}. Attempting to stop stale runtime processes: ${processNames.join(', ')}`
    );
    for (const processName of processNames) {
      killProcessByName(processName);
    }
  }

  sleepSync(300);

  try {
    fs.rmSync(directoryPath, {
      recursive: true,
      force: true,
      maxRetries: 20,
      retryDelay: 150,
    });
    return;
  } catch (error) {
    lastError = error;
    if (!isFileLockError(error)) {
      throw error;
    }
  }

  throw lastError;
}

function copyDirectoryBestEffort(sourceDir, destinationDir) {
  fs.mkdirSync(destinationDir, { recursive: true });
  const lockedFiles = [];
  copyDirectoryEntriesBestEffort(sourceDir, destinationDir, lockedFiles);

  if (lockedFiles.length === 0) {
    return;
  }

  const preview = lockedFiles.slice(0, 5);
  for (const lockedPath of preview) {
    console.warn(`[EFQueryLens] Locked file retained: ${lockedPath}`);
  }

  if (lockedFiles.length > preview.length) {
    console.warn(
      `[EFQueryLens] ${lockedFiles.length - preview.length} more locked files were retained in place.`
    );
  }
}

function copyDirectoryEntriesBestEffort(sourceDir, destinationDir, lockedFiles) {
  const entries = fs.readdirSync(sourceDir, { withFileTypes: true });
  for (const entry of entries) {
    const sourcePath = path.join(sourceDir, entry.name);
    const destinationPath = path.join(destinationDir, entry.name);

    if (entry.isDirectory()) {
      fs.mkdirSync(destinationPath, { recursive: true });
      copyDirectoryEntriesBestEffort(sourcePath, destinationPath, lockedFiles);
      continue;
    }

    try {
      fs.copyFileSync(sourcePath, destinationPath);
    } catch (error) {
      if (!isFileLockError(error)) {
        throw error;
      }

      lockedFiles.push(destinationPath);
    }
  }
}

function getLikelyLockedRuntimeProcesses(directoryPath) {
  const folder = path.basename(directoryPath).toLowerCase();

  if (folder === 'daemon') {
    return ['EFQueryLens.Daemon.exe', 'EFQueryLens.Daemon'];
  }

  if (folder === 'server') {
    return ['EFQueryLens.Lsp.exe', 'EFQueryLens.Lsp'];
  }

  return ['EFQueryLens.Daemon.exe', 'EFQueryLens.Daemon', 'EFQueryLens.Lsp.exe', 'EFQueryLens.Lsp'];
}

function killProcessByName(processName) {
  if (process.platform === 'win32') {
    spawnSync('taskkill', ['/F', '/T', '/IM', processName], { stdio: 'ignore' });
    return;
  }

  spawnSync('pkill', ['-f', processName], { stdio: 'ignore' });
}

function isFileLockError(error) {
  if (error == null || typeof error !== 'object') {
    return false;
  }

  const candidate = error;
  return candidate.code === 'EPERM' || candidate.code === 'EBUSY' || candidate.code === 'EACCES' || candidate.code === 'ENOTEMPTY';
}

function sleepSync(milliseconds) {
  const waitBuffer = new SharedArrayBuffer(4);
  const waitArray = new Int32Array(waitBuffer);
  Atomics.wait(waitArray, 0, 0, milliseconds);
}
