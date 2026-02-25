#!/usr/bin/env dotnet-script
// 
// conflicts-commit-validate.csx
// Validates commits on conflicts branches
//
// Usage: dotnet script .husky/csx/conflicts-commit-validate.csx <commit-msg-file>
//
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Diagnostics;

// Helper: Check if a merge is in progress
bool IsMergeInProgress()
{
    // .git/MERGE_HEAD exists if a merge is in progress
    return System.IO.File.Exists(".git/MERGE_HEAD");
}

// Helper: Check if all conflicts are resolved but merge not committed
bool AllConflictsResolvedButNotCommitted()
{
    var psi = new System.Diagnostics.ProcessStartInfo
    {
        FileName = "git",
        Arguments = "status --porcelain",
        RedirectStandardOutput = true,
        UseShellExecute = false,
        CreateNoWindow = true
    };
    using var process = System.Diagnostics.Process.Start(psi);
    var output = process.StandardOutput.ReadToEnd();
    process.WaitForExit();
    // If no lines start with 'U', all conflicts are resolved
    var lines = output.Split('\n');
    bool hasUnmerged = false;
    for (int i = 0; i < lines.Length; i++)
    {
        if (lines[i].StartsWith("U"))
        {
            hasUnmerged = true;
            break;
        }
    }
    return IsMergeInProgress() && !hasUnmerged;
}


// 1. Get current branch name
string GetCurrentBranch()
{
    var psi = new ProcessStartInfo
    {
        FileName = "git",
        Arguments = "branch --show-current",
        RedirectStandardOutput = true,
        UseShellExecute = false,
        CreateNoWindow = true
    };
    using var process = Process.Start(psi);
    var output = process.StandardOutput.ReadToEnd();
    process.WaitForExit();
    return output.Trim();
}

// 2. Check if branch is a conflicts branch
var branchName = GetCurrentBranch();
var isConflictsBranch = Regex.IsMatch(branchName, @"/conflicts/", RegexOptions.IgnoreCase);
if (!isConflictsBranch)
{
    // Not a conflicts branch, allow
    return 0;
}

// 3. Read commit message (Husky.Net: Args[0][0] is the file path)
// Debug: Print Args info
Console.ForegroundColor = ConsoleColor.Cyan;

// 4. Check if this is a merge commit (multiple parents)
bool IsMergeCommit()
{
    try
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "git",
            Arguments = "rev-parse --verify HEAD^2",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var process = System.Diagnostics.Process.Start(psi);
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return process.ExitCode == 0;
    }
    catch { return false; }
}

// 4b. If merge in progress and all conflicts resolved, allow commit
if (AllConflictsResolvedButNotCommitted())
{
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine("✔ All conflicts resolved. Please run 'git commit' (no -m) to complete the merge commit.");
    Console.ResetColor();
    return 0;
}

// 5. Check for conflict/merge keywords in commit message


// Only allow merge commits on conflicts branches
if (IsMergeCommit())
{
    // Allow merge commits (conflict resolution)
    return 0;
}

// 6. Otherwise, block and inform user
Console.ForegroundColor = ConsoleColor.Red;
Console.WriteLine($"❌ You are attempting to commit to a conflicts branch: {branchName}");
Console.ResetColor();
Console.WriteLine();
Console.ForegroundColor = ConsoleColor.Yellow;
Console.WriteLine("Only merge conflict resolution commits are allowed on conflicts branches.");
Console.WriteLine("If you need to make additional changes, do so on your feature/fix/hotfix branch and re-merge.");
Console.WriteLine();
Console.ResetColor();
return 1;
