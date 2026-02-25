
# Navigate to the directory where the script is located
$scriptDirectory = Split-Path -Parent $MyInvocation.MyCommand.Definition

Write-Host "Script directory: $scriptDirectory"

Set-Location $scriptDirectory

Get-ChildItem -Path . -Filter "packages.lock.json" -Recurse | Remove-Item -Force
Get-ChildItem -Path . -Filter "*.nupkg" -Recurse | Remove-Item -Force
# Function to check if a project is packable



# Function to check if the package version already exists in the local source
function Check-PackageVersionExists {
    param (
        [string]$packageId,
        [string]$packageVersion
    )
    $searchResult = dotnet package search $packageId --source local --format json | ConvertFrom-Json
    foreach ($result in $searchResult.searchResult) {
        foreach ($package in $result.packages) {
            if ($package.id.Trim() -eq $packageId.Trim() -and $package.latestVersion.Trim() -eq $packageVersion.Trim()) {
                Write-Host "Package $packageId version $packageVersion already exists in the local source. " -ForegroundColor Yellow
                return $true
            }
        }
    }
    return $false
}

$csprojFiles = Get-ChildItem -Path $solutionDirectory -Recurse -Filter *.csproj
foreach ($csproj in $csprojFiles) {
    $xml = [xml](Get-Content $csproj.FullName)

    if (($xml.Project.PropertyGroup.IsPackable -eq "true")) {
        # Run dotnet pack to create the NuGet package
 
        $suffix = "dev"
        $packageId = $xml.Project.PropertyGroup.PackageId
        $version = $xml.Project.PropertyGroup.VersionPrefix +".$suffix"

        Check-PackageVersionExists -packageId $packageId -packageVersion $version

        dotnet pack $csproj.FullName --include-symbols --version-suffix $suffix

        # Check if the packing was successful
        if ($LASTEXITCODE -eq 0) {
            Write-Host "Package for project $packageId - $version created successfully."
        } else {
            Write-Host "Error occurred while creating the package for project $($csproj.FullName)."
        }
    } else {
        Write-Host "project $($csproj.FullName) is not packable. Skipping."
    }
}
Get-ChildItem -Path . -Filter "packages.lock.json" -Recurse | Remove-Item -Force

# Get paths to the NuGet cache locations
$httpCachePath = dotnet nuget locals http-cache --list
$httpCachePath = ($httpCachePath -replace "http-cache:\s*", "").Trim()
$globalPackagesPath = dotnet nuget locals global-packages --list
$globalPackagesPath = ($globalPackagesPath -replace "global-packages:\s*", "").Trim()
$pluginCachePath = dotnet nuget locals plugins-cache --list
$pluginCachePath = $pluginCachePath -replace "plugins-cache:\s*", ""

foreach ($csproj in $csprojFiles) {
    $xml = [xml](Get-Content $csproj.FullName)

    if (($xml.Project.PropertyGroup.IsPackable -eq "true")) {
        # Run dotnet pack to create the NuGet package
 
        $packageId = "$($xml.Project.PropertyGroup.PackageId)".Trim().ToLower()
        $version = "$($xml.Project.PropertyGroup.Version)".Trim()

        $newPath = Join-Path "$packageId" "$version"
        $gPath = Join-Path "$globalPackagesPath" "$newPath"
        Remove-Item -Path "$gPath*" -Recurse -Force -ErrorAction SilentlyContinue
        Remove-Item -Path "$httpCachePath\$newPath*" -Recurse -Force -ErrorAction SilentlyContinue
        Remove-Item -Path "$pluginCachePath\$newPath*" -Recurse -Force -ErrorAction SilentlyContinue

        Write-Host "Cleaning up the NuGet cache directories. for $packageId - $version"
    } else {
        Write-Host "project $($csproj.FullName) is not packable. Skipping."
    }
}


# Display the contents of the output directory
Write-Host "Packages available in the output directory:"
Get-ChildItem $outputDirectory -Filter *.nupkg -Recurse | ForEach-Object {

    $path = $_.FullName

    Write-Host " - $path"
    
    dotnet nuget push $path --source local --skip-duplicate

}



