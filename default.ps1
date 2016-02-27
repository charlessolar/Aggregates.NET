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

task default -depends Clean, CreateNuGetPackages, Packaging


task Clean {
	Remove-Item $buildOutputDir -Force -Recurse -ErrorAction SilentlyContinue
	exec { msbuild /nologo /verbosity:quiet $solutionFilePath /t:Clean /p:platform="Any CPU"}
}


task Compile {
	exec { msbuild /nologo /verbosity:quiet $solutionFilePath /p:Configuration=Release /p:platform="Any CPU"}
}


task ILMerge -depends Compile {
	New-Item $mergedDir -Type Directory -ErrorAction SilentlyContinue

	$dllDir = "$srcDir\Aggregates.NET\bin\Release"
	#$inputDlls = "$dllDir\Aggregates.NET.dll"
	#@() |% { $inputDlls = "$inputDlls $dllDir\$_.dll" }
	#Invoke-Expression "$ilmerge_path /targetplatform:v4.5.2 /internalize /allowDup /target:library /log /out:$mergedDir\Aggregates.NET.dll $inputDlls"
	Copy-Item $dllDir\Aggregates.NET.dll $mergedDir\Aggregates.NET.dll
	Copy-Item $dllDir\Aggregates.NET.dll $mergedDir\Aggregates.NET.pdb

	$dllDir = "$srcDir\Aggregates.NET.Consumer\bin\Release"
	#$inputDlls = "$dllDir\Aggregates.NET.Consumer.dll"
	#@() |% { $inputDlls = "$inputDlls $dllDir\$_.dll" }
	#Invoke-Expression "$ilmerge_path /targetplatform:v4.5.2 /internalize /allowDup /target:library /log /out:$mergedDir\Aggregates.NET.Consumer.dll $inputDlls"
	Copy-Item $dllDir\Aggregates.NET.Consumer.dll $mergedDir\Aggregates.NET.Consumer.dll
	Copy-Item $dllDir\Aggregates.NET.Consumer.dll $mergedDir\Aggregates.NET.Consumer.pdb

	$dllDir = "$srcDir\Aggregates.NET.Domain\bin\Release"
	#$inputDlls = "$dllDir\Aggregates.NET.Domain.dll"
	#@() |% { $inputDlls = "$inputDlls $dllDir\$_.dll" }
	#Invoke-Expression "$ilmerge_path /targetplatform:v4.5.2 /internalize /allowDup /target:library /log /out:$mergedDir\Aggregates.NET.Domain.dll $inputDlls"
	Copy-Item $dllDir\Aggregates.NET.Domain.dll $mergedDir\Aggregates.NET.Domain.dll
	Copy-Item $dllDir\Aggregates.NET.Domain.dll $mergedDir\Aggregates.NET.Domain.pdb

	$dllDir = "$srcDir\Aggregates.NET.GetEventStore\bin\Release"
	#$inputDlls = "$dllDir\Aggregates.NET.GetEventStore.dll"
	#@() |% { $inputDlls = "$inputDlls $dllDir\$_.dll" }
	#Invoke-Expression "$ilmerge_path /targetplatform:v4.5.2 /internalize /allowDup /target:library /log /out:$mergedDir\Aggregates.NET.GetEventStore.dll $inputDlls"
	Copy-Item $dllDir\Aggregates.NET.GetEventStore.dll $mergedDir\Aggregates.NET.GetEventStore.dll
	Copy-Item $dllDir\Aggregates.NET.GetEventStore.dll $mergedDir\Aggregates.NET.GetEventStore.pdb
}

task Packaging -depends ILMerge {
	$versionString = Get-Version $assemblyInfoFilePath
	7z a -tzip $buildOutputDir\Aggregates.NET.Binaries.$versionString.zip $mergedDir\*.dll $mergedDir\*.pdb
	git archive -o $buildOutputDir\Aggregates.NET.Source.$versionString.zip HEAD
}

task CreateNuGetPackages -depends ILMerge {
	$versionString = Get-Version $assemblyInfoFilePath
	gci $srcDir -Recurse -Include *.nuspec | % {
		exec { .$rootDir\nuget\nuget.exe pack $_ -o $buildOutputDir -properties "version=$versionString;configuration=$configuration" }
		exec { .$rootDir\nuget\nuget.exe pack $_ -symbols -o $buildOutputDir -properties "version=$versionString;configuration=$configuration" }
	}
}