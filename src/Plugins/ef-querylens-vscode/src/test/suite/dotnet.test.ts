import * as assert from 'assert';

import { validateRequiredDotnetRuntimes } from '../../runtime/dotnet';

suite('dotnet runtime preflight', () => {
    test('accepts .NET 10 runtime and ASP.NET Core runtime', () => {
        const result = validateRequiredDotnetRuntimes([
            'Microsoft.NETCore.App 10.0.0 [/usr/share/dotnet/shared/Microsoft.NETCore.App]',
            'Microsoft.AspNetCore.App 10.0.0 [/usr/share/dotnet/shared/Microsoft.AspNetCore.App]',
        ].join('\n'));

        assert.strictEqual(result.ok, true);
    });

    test('reports missing ASP.NET Core runtime', () => {
        const result = validateRequiredDotnetRuntimes(
            'Microsoft.NETCore.App 10.0.0 [/usr/share/dotnet/shared/Microsoft.NETCore.App]'
        );

        assert.strictEqual(result.ok, false);
        assert.ok(result.message?.includes('Microsoft.AspNetCore.App 10.x'));
    });
});
