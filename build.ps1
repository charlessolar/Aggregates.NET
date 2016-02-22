param(
	[int]$buildNumber = 0
	)

if(Test-Path Env:\APPVEYOR_BUILD_NUMBER){
	$buildNumber = [int]$Env:APPVEYOR_BUILD_NUMBER
	Write-Host "Using APPVEYOR_BUILD_NUMBER"
}

"Build number $buildNumber"

$packageConfigs = Get-ChildItem . -Recurse | where{$_.Name -eq "packages.config"}
foreach($packageConfig in $packageConfigs){
	Write-Host "Restoring" $packageConfig.FullName
	nuget\nuget.exe i $packageConfig.FullName -o src\packages
}

Import-Module .\src\packages\psake.4.4.1\tools\psake.psm1
Import-Module .\Nuget\BuildFunctions.psm1
Invoke-Psake .\default.ps1 default -framework "4.5.1x64" -properties @{ buildNumber=$buildNumber }
Remove-Module BuildFunctions
Remove-Module psake