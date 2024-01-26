
Set-StrictMode -Version 3.0

# Set script to exit if a command fails
$ErrorActionPreference = "Stop"

# Set script to run cmdlets without confirmation
$ConfirmPreference = "None";

# Get the directory of the build script
$ScriptDir = Split-Path $MyInvocation.MyCommand.Path -Parent

# Check dotnet is installed
if (!(Get-Command "dotnet" -ErrorAction SilentlyContinue))
{
    Write-Error "dotnet is not installed or could not be found"
    exit 1
}

# Run the build application
dotnet run --project $ScriptDir/cake/Build.csproj -- $args
exit $LASTEXITCODE;