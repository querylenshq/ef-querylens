import * as fs from 'fs';
import * as os from 'os';
import * as path from 'path';
import { execFileSync } from 'child_process';
import { workspace } from 'vscode';

import { QueryLensSettings } from '../types';

export type DotnetRuntimeCheck = {
    ok: boolean;
    message?: string;
    runtimes?: string;
};

export function resolveDotnetPath(settings: QueryLensSettings, log?: (message: string) => void): string | undefined {
    const candidates = [
        settings.dotnetPath,
        readConfiguredDotnetPath(),
        findOnPath(),
        ...commonDotnetCandidates(),
    ];

    for (const candidate of candidates) {
        const resolved = normalizeDotnetCandidate(candidate);
        if (!resolved) {
            continue;
        }

        if (isUsableDotnetPath(resolved)) {
            if (resolved !== 'dotnet') {
                log?.(`[EFQueryLens] dotnet resolved path=${resolved}`);
            }
            return resolved;
        }
    }

    return undefined;
}

export function checkRequiredDotnetRuntimes(dotnetPath: string): DotnetRuntimeCheck {
    let runtimes: string;
    try {
        runtimes = execFileSync(dotnetPath, ['--list-runtimes'], {
            encoding: 'utf8',
            timeout: 5000,
        });
    } catch (error) {
        return {
            ok: false,
            message: `Could not run '${dotnetPath} --list-runtimes': ${String(error)}`,
        };
    }

    return validateRequiredDotnetRuntimes(runtimes);
}

export function validateRequiredDotnetRuntimes(runtimes: string): DotnetRuntimeCheck {
    const hasNetCore = /^Microsoft\.NETCore\.App\s+10\./m.test(runtimes);
    const hasAspNetCore = /^Microsoft\.AspNetCore\.App\s+10\./m.test(runtimes);
    if (hasNetCore && hasAspNetCore) {
        return { ok: true, runtimes };
    }

    const missing = [
        hasNetCore ? undefined : 'Microsoft.NETCore.App 10.x',
        hasAspNetCore ? undefined : 'Microsoft.AspNetCore.App 10.x',
    ].filter((value): value is string => Boolean(value));

    return {
        ok: false,
        message: `Missing required .NET runtime(s): ${missing.join(', ')}.`,
        runtimes,
    };
}

function readConfiguredDotnetPath(): string | undefined {
    return workspace.getConfiguration('dotnet').get<string>('dotnetPath');
}

function findOnPath(): string | undefined {
    const command = process.platform === 'win32' ? 'where' : 'which';
    try {
        const output = execFileSync(command, ['dotnet'], {
            encoding: 'utf8',
            timeout: 3000,
        });
        return output.split(/\r?\n/).find(line => line.trim().length > 0)?.trim();
    } catch {
        return undefined;
    }
}

function commonDotnetCandidates(): string[] {
    if (process.platform === 'win32') {
        return [
            'C:\\Program Files\\dotnet\\dotnet.exe',
            'C:\\Program Files (x86)\\dotnet\\dotnet.exe',
        ];
    }

    return [
        '/usr/share/dotnet/dotnet',
        '/usr/local/share/dotnet/dotnet',
        path.join(os.homedir(), '.dotnet', 'dotnet'),
    ];
}

function normalizeDotnetCandidate(candidate: string | undefined): string | undefined {
    const trimmed = candidate?.trim();
    if (!trimmed) {
        return undefined;
    }

    if (trimmed === 'dotnet') {
        return trimmed;
    }

    try {
        const stat = fs.existsSync(trimmed) ? fs.statSync(trimmed) : undefined;
        if (stat?.isDirectory()) {
            return path.join(trimmed, process.platform === 'win32' ? 'dotnet.exe' : 'dotnet');
        }
    } catch {
        return undefined;
    }

    return trimmed;
}

function isUsableDotnetPath(candidate: string): boolean {
    if (candidate === 'dotnet') {
        return true;
    }

    try {
        fs.accessSync(candidate, fs.constants.X_OK);
        return true;
    } catch {
        return false;
    }
}
