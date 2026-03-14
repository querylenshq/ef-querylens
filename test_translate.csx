#r "src/EFQueryLens.Core/bin/Debug/net10.0/EFQueryLens.Core.dll"
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using EFQueryLens.Core;

var engine = new QueryLensEngine();
var sw = Stopwatch.StartNew();
var result = await engine.TranslateAsync(new TranslationRequest
{
    AssemblyPath = @"c:/nemina/QueryLens/samples/SampleMySqlApp/bin/Debug/net10.0/SampleMySqlApp.dll",
    Expression = "db.Orders"
});
sw.Stop();

Console.WriteLine($"Translation took {sw.ElapsedMilliseconds}ms");
Console.WriteLine($"Success={result.Success}");
Console.WriteLine($"Error={result.ErrorMessage}");
Console.WriteLine($"SqlLen={(result.Sql?.Length ?? 0)}");
if (!string.IsNullOrWhiteSpace(result.Sql))
{
    Console.WriteLine(result.Sql.Substring(0, Math.Min(180, result.Sql.Length)).Replace("\r", " ").Replace("\n", " "));
}

await engine.DisposeAsync();
