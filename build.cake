// Install addins.
#addin "nuget:https://www.nuget.org/api/v2?package=Polly&version=4.2.0"
#addin "nuget:https://www.nuget.org/api/v2?package=Newtonsoft.Json&version=9.0.1"
#addin "nuget:https://www.nuget.org/api/v2?package=NuGet.Core&version=2.14"

// Install tools.
#tool "nuget:https://www.nuget.org/api/v2?package=GitVersion.CommandLine"
#tool "nuget:https://www.nuget.org/api/v2?package=NUnit.ConsoleRunner&version=3.4.0"
#tool "nuget:https://www.nuget.org/api/v2?package=gitlink"

// Load other scripts.
#load "./build/parameters.cake"

///////////////////////////////////////////////////////////////////////////////
// USINGS
///////////////////////////////////////////////////////////////////////////////

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Polly;

//////////////////////////////////////////////////////////////////////
// PARAMETERS
//////////////////////////////////////////////////////////////////////

BuildParameters parameters = BuildParameters.GetParameters(Context);
bool publishingError = false;

///////////////////////////////////////////////////////////////////////////////
// SETUP / TEARDOWN
///////////////////////////////////////////////////////////////////////////////

Setup(context =>
{
    parameters.Initialize(context);

    Information("==============================================");
    Information("==============================================");

    if (parameters.IsRunningOnGoCD)
    {
        Information("Pipeline Name: " + BuildSystem.GoCD.Environment.Pipeline.Name + "{" + BuildSystem.GoCD.Environment.Pipeline.Counter + "}");
        Information("Stage Name: " + BuildSystem.GoCD.Environment.Stage.Name + "{" + BuildSystem.GoCD.Environment.Stage.Counter + "}");
    }

    Information("Solution: " + parameters.Solution);
    Information("Target: " + parameters.Target);
    Information("Configuration: " + parameters.Configuration);
    Information("IsLocalBuild: " + parameters.IsLocalBuild);
    Information("IsRunningOnUnix: " + parameters.IsRunningOnUnix);
    Information("IsRunningOnWindows: " + parameters.IsRunningOnWindows);
    Information("IsRunningOnGoCD: " + parameters.IsRunningOnGoCD);
    Information("IsReleaseBuild: " + parameters.IsReleaseBuild);
    Information("ShouldPublish: " + parameters.ShouldPublish);

    // Increase verbosity?
    if(parameters.IsReleaseBuild && (context.Log.Verbosity != Verbosity.Diagnostic)) {
        Information("Increasing verbosity to diagnostic.");
        context.Log.Verbosity = Verbosity.Diagnostic;
    }

    Information("Building version {0} {5} of {4} ({1}, {2}) using version {3} of Cake",
        parameters.Version.SemVersion,
        parameters.Configuration,
        parameters.Target,
        parameters.Version.CakeVersion,
        parameters.Solution,
        parameters.Version.Sha.Substring(0,8));

});

///////////////////////////////////////////////////////////////////////////////
// TEARDOWN
///////////////////////////////////////////////////////////////////////////////

Teardown(context =>
{
    Information("Finished running tasks.");
});

//////////////////////////////////////////////////////////////////////
// TASKS
//////////////////////////////////////////////////////////////////////

Task("Clean")
    .Does(() =>
{
    CleanDirectories(parameters.Paths.Directories.ToClean);
});

Task("Restore-NuGet-Packages")
    .IsDependentOn("Clean")
    .Does(() =>
{
    var maxRetryCount = 10;
    Policy
        .Handle<Exception>()
        .Retry(maxRetryCount, (exception, retryCount, context) => {
            if (retryCount == maxRetryCount)
            {
                throw exception;
            }
            else
            {
                Verbose("{0}", exception);
            }})
        .Execute(()=> {
                NuGetRestore(parameters.Solution, new NuGetRestoreSettings {
                });
        });
});
Task("Update-NuGet-Packages")
    .IsDependentOn("Restore-NuGet-Packages")
    .Does(() =>
{
    var maxRetryCount = 10;
    Policy
        .Handle<Exception>()
        .Retry(maxRetryCount, (exception, retryCount, context) => {
            if (retryCount == maxRetryCount)
            {
                throw exception;
            }
            else
            {
                Verbose("{0}", exception);
            }})
        .Execute(()=> {
                // Update all our packages to latest build version
                NuGetUpdate(parameters.Solution, new NuGetUpdateSettings {
                    Safe = true,
                    ArgumentCustomization = args => args.Append("-FileConflictAction Overwrite")
                });
        });
});

Task("Build")
    .IsDependentOn("Update-NuGet-Packages")
    .Does(() =>
{   
    if(IsRunningOnWindows())
    {
      // Use MSBuild
      MSBuild(parameters.Solution, settings => {
        settings.SetConfiguration(parameters.Configuration);
        settings.SetVerbosity(Verbosity.Minimal);
      });
    }
    else
    {
      // Use XBuild
      XBuild(parameters.Solution, settings => {
        settings.SetConfiguration(parameters.Configuration);
        settings.SetVerbosity(Verbosity.Minimal);
      });
    }
});

Task("Run-Unit-Tests")
    .IsDependentOn("Build")
    .Does(() =>
{
    NUnit3("./src/**/bin/" + parameters.Configuration + "/*.UnitTests.dll", new NUnit3Settings {
        NoResults = true
        });
});

Task("Copy-Files")
    .IsDependentOn("Run-Unit-Tests")
    .Does(() =>
{
    // GitLink
    if(parameters.IsRunningOnWindows)
    {
        Information("Updating PDB files using GitLink");
        GitLink(
            Context.Environment.WorkingDirectory.FullPath,
            new GitLinkSettings {

                SolutionFileName = parameters.Solution.FullPath,
                ShaHash = parameters.Version.Sha
            });
    }

    // Copy files from artifact sources to artifact directory
    foreach(var project in parameters.Paths.Files.Projects) 
    {
        CleanDirectory(parameters.Paths.Directories.ArtifactsBin.Combine(project.AssemblyName));
        CopyFiles(project.GetBinaries(),
            parameters.Paths.Directories.ArtifactsBin.Combine(project.AssemblyName));
    }
    // Copy license
    CopyFileToDirectory("./LICENSE", parameters.Paths.Directories.ArtifactsBin);
});

Task("Zip-Files")
    .IsDependentOn("Copy-Files")
    .Does(() =>
{
    var files = GetFiles( parameters.Paths.Directories.ArtifactsBin + "/**/*" );
    Zip(parameters.Paths.Directories.ArtifactsBin, parameters.Paths.Files.ZipBinaries, files);

	
});

Task("Create-NuGet-Packages")
    .IsDependentOn("Copy-Files")
    .Does(() =>
{
    // Build libraries
    foreach(var nuget in parameters.Packages.Nuget)
    {
        Information("Building nuget package: " + nuget.Id + " Version: " + nuget.Nuspec.Version);
        NuGetPack(nuget.Nuspec);
    }
});

Task("Upload-AppVeyor-Artifacts")
    .IsDependentOn("Zip-Files")
    .IsDependentOn("Create-NuGet-Packages")
    .WithCriteria(() => parameters.IsRunningOnAppVeyor)
    .Does(() =>
{
    AppVeyor.UploadArtifact(parameters.Paths.Files.ZipBinaries);
    foreach(var package in GetFiles(parameters.Paths.Directories.NugetRoot + "/*"))
    {
        AppVeyor.UploadArtifact(package);
    }
});

Task("Publish-NuGet")
    .IsDependentOn("Create-NuGet-Packages")
    .WithCriteria(() => parameters.ShouldPublish)
    .Does(() =>
{
    // Resolve the API key.
    var apiKey = EnvironmentVariable("NUGET_API_KEY");
    if(string.IsNullOrEmpty(apiKey)) {
        throw new InvalidOperationException("Could not resolve NuGet API key.");
    }

    // Resolve the API url.
    var apiUrl = EnvironmentVariable("NUGET_API_URL");
    if(string.IsNullOrEmpty(apiUrl)) {
        throw new InvalidOperationException("Could not resolve NuGet API url.");
    }

    foreach(var package in parameters.Packages.Nuget)
    {
		Information("Publish nuget: " + package.PackagePath);

		var maxRetryCount = 10;
		Policy
			.Handle<Exception>()
			.Retry(maxRetryCount, (exception, retryCount, context) => {
				if (retryCount == maxRetryCount)
				{
					throw exception;
				}
				else
				{
					Verbose("{0}", exception);
				}})
			.Execute(()=> {

					// Push the package.
					NuGetPush(package.PackagePath, new NuGetPushSettings {
					  ApiKey = apiKey,
					  Source = apiUrl
					});
			});
    }
})
.OnError(exception =>
{
    Information("Publish-NuGet Task failed, but continuing with next Task...");
    publishingError = true;
});


//////////////////////////////////////////////////////////////////////
// TASK TARGETS
//////////////////////////////////////////////////////////////////////

Task("Package")
  .IsDependentOn("Zip-Files")
  .IsDependentOn("Create-NuGet-Packages");

Task("Default")
  .IsDependentOn("Package");

Task("AppVeyor")
  .IsDependentOn("Upload-AppVeyor-Artifacts")
  .IsDependentOn("Publish-NuGet")
  .Finally(() =>
{
    if(publishingError)
    {
        throw new Exception("An error occurred during the publishing of Aggregates.NET.  All publishing tasks have been attempted.");
    }
});
Task("GoCD")
  .IsDependentOn("Publish-NuGet")
  .Finally(() =>
{
    if(publishingError)
    {
        throw new Exception("An error occurred during the publishing of Aggregates.NET.  All publishing tasks have been attempted.");
    }
});

//////////////////////////////////////////////////////////////////////
// EXECUTION
//////////////////////////////////////////////////////////////////////

RunTarget(parameters.Target);
