// Install addins.
#addin "nuget:?package=Polly&version=4.2.0"
#addin "nuget:?package=Newtonsoft.Json&version=9.0.1"

// Install tools.
#tool "nuget:?package=NUnit.ConsoleRunner&version=3.4.0"

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

    if (parameters.IsRunningOnAppVeyor)
    {
        Information("Repository Name: " + BuildSystem.AppVeyor.Environment.Repository.Name);
        Information("Repository Branch: " + BuildSystem.AppVeyor.Environment.Repository.Branch);
    }

    Information("Target: " + parameters.Target);
    Information("Configuration: " + parameters.Configuration);
    Information("IsLocalBuild: " + parameters.IsLocalBuild);
    Information("IsRunningOnUnix: " + parameters.IsRunningOnUnix);
    Information("IsRunningOnWindows: " + parameters.IsRunningOnWindows);
    Information("IsRunningOnAppVeyor: " + parameters.IsRunningOnAppVeyor);
    Information("IsPullRequest: " + parameters.IsPullRequest);
    Information("IsMainRepo: " + parameters.IsMainRepo);
    Information("IsMasterBranch: " + parameters.IsMasterBranch);
    Information("IsTagged: " + parameters.IsTagged);
    Information("ShouldPublish: " + parameters.ShouldPublish);

    // Increase verbosity?
    if(parameters.IsMasterBranch && (context.Log.Verbosity != Verbosity.Diagnostic)) {
        Information("Increasing verbosity to diagnostic.");
        context.Log.Verbosity = Verbosity.Diagnostic;
    }

    Information("Building version {0} of Aggregates.NET ({1}, {2}) using version {3} of Cake. (IsTagged: {4})",
        parameters.Version.SemVersion,
        parameters.Configuration,
        parameters.Target,
        parameters.Version.CakeVersion,
        parameters.IsTagged);

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
                NuGetRestore("./src/Aggregates.NET.sln");
        });
});

Task("Build")
    .IsDependentOn("Restore-NuGet-Packages")
    .Does(() =>
{	
    if(IsRunningOnWindows())
    {
      // Use MSBuild
      MSBuild("./src/Aggregates.NET.sln", settings => {
        settings.SetConfiguration(parameters.Configuration);
        settings.SetVerbosity(Verbosity.Minimal);
	  });
    }
    else
    {
      // Use XBuild
      XBuild("./src/Aggregates.NET.sln", settings => {
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
	// Copy files from artifact sources to artifact directory
	foreach(var sources in parameters.Paths.Directories.ArtifactSources)
		CopyDirectory(sources, parameters.Paths.Directories.ArtifactsBin);

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
		Information("Building nuget package: " + nuget.Id);
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

//////////////////////////////////////////////////////////////////////
// EXECUTION
//////////////////////////////////////////////////////////////////////

RunTarget(parameters.Target);