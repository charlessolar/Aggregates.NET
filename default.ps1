properties {
	$projectName = "Aggregates.NET"
	$configuration = "Release"
	$buildNumber = 0
	$rootDir  = Resolve-Path .\
	$buildOutputDir = "$rootDir\build"
	$mergedDir = "$buildOutputDir\merged"
	$reportsDir = "$buildOutputDir\reports"
	$srcDir = "$rootDir\src"
	$solutionFilePath = "$srcDir\$projectName.sln"
	$assemblyInfoFilePath = "$srcDir\SharedAssemblyInfo.cs"
	$ilmerge_path = "$srcDir\packages\ILMerge.2.14.1208\tools\ilmerge.exe"
}

task default -depends Clean, UpdateVersion, RunTests, CreateNuGetPackages

task Clean {
	Remove-Item $buildOutputDir -Force -Recurse -ErrorAction SilentlyContinue
	exec { msbuild /nologo /verbosity:quiet $solutionFilePath /t:Clean /p:platform="Any CPU"}
}

task UpdateVersion {
	$version = Get-Version $assemblyInfoFilePath
	$oldVersion = New-Object Version $version
	$newVersion = New-Object Version ($oldVersion.Major, $oldVersion.Minor, $oldVersion.Build, $buildNumber)
	Update-Version $newVersion $assemblyInfoFilePath
}

task Compile {
	exec { msbuild /nologo /verbosity:quiet $solutionFilePath /p:Configuration=Release /p:platform="Any CPU"}
}

task RunTests -depends Compile {
	New-Item $reportsDir -Type Directory -ErrorAction SilentlyContinue

	$nunitRunner = "$srcDir\packages\nunit.runners.2.6.4\tools\nunit-console.exe"

	.$nunitRunner "$srcDir\Aggregates.NET.Unit\bin\Release\Aggregates.NET.Unit.dll"
}

task ILMerge -depends Compile {
	New-Item $mergedDir -Type Directory -ErrorAction SilentlyContinue

	$dllDir = "$srcDir\Aggregates.NET\bin\Release"
	#$inputDlls = "$dllDir\Aggregates.NET.dll"
	#@() |% { $inputDlls = "$inputDlls $dllDir\$_.dll" }
	#Invoke-Expression "$ilmerge_path /targetplatform:v4.0 /internalize /allowDup /target:library /log /out:$mergedDir\Aggregates.NET.dll $inputDlls"
	Copy-Item $dllDir\Aggregates.NET.dll $mergedDir\Aggregates.NET.dll
	Copy-Item $dllDir\Aggregates.NET.dll $mergedDir\Aggregates.NET.pdb

	$dllDir = "$srcDir\Aggregates.NET.RavenDB\bin\Release"
	#$inputDlls = "$dllDir\Aggregates.NET.RavenDB.dll"
	#@() |% { $inputDlls = "$inputDlls $dllDir\$_.dll" }
	#Invoke-Expression "$ilmerge_path /targetplatform:v4.0 /internalize /allowDup /target:library /log /out:$mergedDir\Aggregates.NET.RavenDB.dll $inputDlls"
	Copy-Item $dllDir\Aggregates.NET.RavenDB.dll $mergedDir\Aggregates.NET.RavenDB.dll
	Copy-Item $dllDir\Aggregates.NET.RavenDB.dll $mergedDir\Aggregates.NET.RavenDB.pdb
}

task CreateNuGetPackages -depends ILMerge {
	$versionString = Get-Version $assemblyInfoFilePath
	$version = New-Object Version $versionString
	$packageVersion = $version.Major.ToString() + "." + $version.Minor.ToString() + "." + $version.Build.ToString() + "-build" + $buildNumber.ToString().PadLeft(5,'0')
	$packageVersion
	gci $srcDir -Recurse -Include *.nuspec | % {
		exec { .$rootDir\nuget\nuget.exe pack $_ -o $buildOutputDir -properties "version=$packageVersion;configuration=$configuration" }
	}
}