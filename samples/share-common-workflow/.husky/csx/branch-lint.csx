/// <summary>
/// Branch name linter with JIRA ticket validation
/// Enforces format: developer-name/type/HSAMED-XXXX-short-description
/// Supported types: feat, fix, hotfix
/// </summary>

using System.Text.RegularExpressions;

// Get current branch name
var process = new System.Diagnostics.Process();
process.StartInfo.FileName = "git";
process.StartInfo.Arguments = "branch --show-current";
process.StartInfo.UseShellExecute = false;
process.StartInfo.RedirectStandardOutput = true;
process.StartInfo.CreateNoWindow = true;

process.Start();
var branchName = process.StandardOutput.ReadToEnd().Trim();
process.WaitForExit();

// Skip validation for protected branches, deployment branches, and automated branches
var protectedBranches = new[] { "dev", "deploy/dev", "deploy/sit", "deploy/uat", "deploy/prod", "update-dependencies" };
if (Array.IndexOf(protectedBranches, branchName) >= 0)
{
    Console.WriteLine($"✅ Skipping branch validation for protected branch: {branchName}");
    return 0;
}

// Skip validation for prod-rel branches (deploy/prod-rel-*, deploy/test-prod-rel, test/prod-rel-*)
if (Regex.IsMatch(branchName, @"^(deploy\/(prod-rel-|test-prod-rel)|test\/prod-rel-)"))
{
    Console.WriteLine($"✅ Skipping branch validation for prod-rel branch: {branchName}");
    return 0;
}

// Pattern: developer-name/type/HSAMED-XXXX[-HSAMED-YYYY...]-description (description is optional but recommended)
// Now supports: feat, fix, hotfix, conflicts
var pattern = @"^[a-z][a-z0-9-]*\/(?:feat|fix|hotfix|conflicts)\/HSAMED-\d{4,5}(?:-HSAMED-\d{4,5})*(?:-[a-z0-9_-]+)?$";

if (Regex.IsMatch(branchName, pattern))
{
    Console.WriteLine($"✅ Branch name is valid: {branchName}");
    return 0;
}

Console.ForegroundColor = ConsoleColor.Red;
Console.WriteLine($"❌ Invalid branch name: {branchName}");
Console.ResetColor();
Console.WriteLine();
Console.WriteLine("Required format: developer-name/type/HSAMED-XXXX[-HSAMED-YYYY...]-short-description");
Console.WriteLine();
Console.WriteLine("Supported types:");
Console.WriteLine("  • feat      - New features and enhancements");
Console.WriteLine("  • fix       - Bug fixes");
Console.WriteLine("  • hotfix    - Emergency production fixes");
Console.WriteLine("  • conflicts - Conflict resolution branches (see branching guide)");
Console.WriteLine();
Console.ForegroundColor = ConsoleColor.Yellow;
Console.WriteLine("⚠️  Other branch types (chore, docs, test, refactor, etc.) are no longer allowed.");
Console.WriteLine("    Use feat/ for new work, fix/ for bug fixes, conflicts/ for merge conflict resolution.");
Console.ResetColor();
Console.WriteLine();
Console.WriteLine("Examples:");
Console.WriteLine("  ✅ nemina/feat/HSAMED-7569-user-authentication");
Console.WriteLine("  ✅ john/fix/HSAMED-1234-database-connection-timeout");
Console.WriteLine("  ✅ sarah/hotfix/HSAMED-5678-critical-security-patch");
Console.WriteLine("  ✅ mike/feat/HSAMED-1111-HSAMED-2222-multiple-features");
Console.WriteLine("  ✅ alex/feat/HSAMED-9999 (short description optional)");
Console.WriteLine("  ✅ nemina/conflicts/nemina-feat-HSAMED-7569-user-auth-to-dev");
Console.WriteLine("  ✅ john/conflicts/john-fix-HSAMED-1234-db-connection-to-sit");
Console.WriteLine();
Console.WriteLine("For production releases, use deploy/prod-rel-YYYYMMDD branches (exempt from this check).");

return 1;
