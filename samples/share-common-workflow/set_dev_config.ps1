# Get the directory where the script is located
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition

# Path to the dev_configs directory
$devConfigDir = Join-Path $scriptDir "dev_config"

# Get the list of JSON files in the dev_config directory
$jsonFiles = Get-ChildItem -Path $devConfigDir -Filter *.json

foreach ($jsonFile in $jsonFiles) {
    # Extract the project name from the JSON file name (without the .json extension)
    $projectName = $jsonFile.BaseName
    
    # Search for the .csproj file recursively
    $projectFile = Get-ChildItem -Path $scriptDir -Filter "$projectName.csproj" -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1

    if ($projectFile) {
        # Run the command to set user secrets
        Write-Host "Setting user secrets for project: $projectName"
        $command = "type `"$($jsonFile.FullName)`" | dotnet user-secrets set --project `"$($projectFile.FullName)`""
        #Write-Host $command
        Invoke-Expression $command
    } else {
        Write-Host "Project file not found for: $projectName"
    }
}
