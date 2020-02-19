// Install addins.
#addin "nuget:?package=Cake.FileHelpers&version=3.2.1"
#addin "nuget:?package=Cake.Incubator&version=5.1.0"
#addin "nuget:?package=Cake.Docker&version=0.11.0"
#addin "nuget:?package=Cake.Curl&version=4.1.0"
//#addin "nuget:?package=Cake.Sonar&version=1.1.18"
#addin "nuget:?package=Cake.OpenCoverToCoberturaConverter&version=0.1.7.7"
#addin "nuget:?package=Cake.Coveralls&version=0.10.0"

// Install tools.
#tool "nuget:?package=GitReleaseManager&version=0.10.3"
#tool "nuget:?package=GitVersion.CommandLine&version=5.1.3"
#tool "nuget:?package=OpenCover&version=4.7.922"
//#tool "nuget:?package=MSBuild.SonarQube.Runner.Tool&version=4.3.0"
#tool "nuget:?package=OpenCoverToCoberturaConverter&version=0.3.4"
#tool "nuget:?package=ReportGenerator&version=4.4.7"
#tool "nuget:?package=coveralls.io&version=1.4.2"

// Load other scripts.
#load "./build/parameters.cake"

///////////////////////////////////////////////////////////////////////////////
// USINGS
///////////////////////////////////////////////////////////////////////////////

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

//////////////////////////////////////////////////////////////////////
// PARAMETERS
//////////////////////////////////////////////////////////////////////

BuildParameters parameters = BuildParameters.GetParameters(Context);

string GetDotNetCoreArgsVersions(BuildVersion version)
{

    return string.Format(
        @"/p:Version={1} /p:AssemblyVersion={0} /p:FileVersion={0} /p:ProductVersion={0}",
        version.SemVersion, version.NuGet);
}

///////////////////////////////////////////////////////////////////////////////
// SETUP / TEARDOWN
///////////////////////////////////////////////////////////////////////////////

Setup(context =>
{
    parameters.Initialize(context);

    Information("==============================================");
    Information("==============================================");

    Information("Calculated Semantic Version: {0} sha: {1}", parameters.Version.SemVersion, parameters.Version.Sha.Substring(0,8));
    Information("Calculated NuGet Version: {0}", parameters.Version.NuGet);

    Information("==============================================");
    Information("==============================================");

    Information("Solution: " + parameters.Solution);
    Information("Target: " + parameters.Target);
    Information("Configuration: " + parameters.Configuration);
    Information("IsLocalBuild: " + parameters.IsLocalBuild);
    Information("IsRunningOnUnix: " + parameters.IsRunningOnUnix);
    Information("IsRunningOnWindows: " + parameters.IsRunningOnWindows);
    Information("IsRunningOnVSTS: " + parameters.IsRunningOnVSTS);
    Information("IsRunningOnGitHub: " + parameters.IsRunningOnGitHub);
    Information("IsReleaseBuild: " + parameters.IsReleaseBuild);
    Information("IsPullRequest: " + parameters.IsPullRequest);
    Information("IsMaster: " + parameters.IsMaster + " Branch: " + parameters.Branch);
    Information("BuildNumber: " + parameters.BuildNumber);

    // Increase verbosity?
    if(parameters.IsReleaseBuild && (context.Log.Verbosity != Verbosity.Diagnostic)) {
        Information("Increasing verbosity to diagnostic.");
        context.Log.Verbosity = Verbosity.Diagnostic;
    }

    if(parameters.IsRunningOnVSTS) 
    {
        var commands = context.BuildSystem().TFBuild.Commands;
        commands.UpdateBuildNumber(parameters.Version.SemVersion);
        commands.AddBuildTag(parameters.Version.Sha.Substring(0,8));
        commands.AddBuildTag(parameters.Version.SemVersion);
        commands.AddBuildTag(parameters.Configuration);
    }
    if(parameters.IsRunningOnAppVeyor) {
        AppVeyor.UpdateBuildVersion(parameters.Version.SemVersion);
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

    if(!context.Successful)
    {
        Error(string.Format("Exception: {0} Message: {1}\nStack: {2}", context.ThrownException.GetType(), context.ThrownException.Message, context.ThrownException.StackTrace));
    }
});


//////////////////////////////////////////////////////////////////////
// DEFINITIONS
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
    DotNetCoreRestore(parameters.Solution.FullPath, new DotNetCoreRestoreSettings()
    {
        ConfigFile = new FilePath("./build/nuget.config"),
        ArgumentCustomization = aggs => aggs.Append(GetDotNetCoreArgsVersions(parameters.Version))
    });


});


Task("Build")
    .IsDependentOn("Restore-NuGet-Packages")
    .Does(() =>
{   

    DotNetCoreBuild(parameters.Solution.FullPath,
    new DotNetCoreBuildSettings {
        Configuration = parameters.Configuration,
        ArgumentCustomization = aggs => aggs
                .Append(GetDotNetCoreArgsVersions(parameters.Version))
                .Append("/p:ci=true")
                .Append("/p:SourceLinkEnabled=true")
    });


});

Task("Run-Unit-Tests")
    .IsDependentOn("Build")
    .Does(() =>
{
    EnsureDirectoryExists(parameters.Paths.Directories.TestResultsDir);

    
    var projects = parameters.Paths.Files.Projects
        .Where(x => x.OutputType != "Test")
        .Select(x => parameters.Paths.Directories.ArtifactsBin.Combine(x.AssemblyName));

    var settings = new OpenCoverSettings 
    {
		// Forces error in build when tests fail
		ReturnTargetCodeOffset = 0,

        MergeOutput = true,
        SkipAutoProps = true,
        OldStyle = true,
    };
    settings.WithFilter("+[Aggregates*]*").ExcludeByAttribute("*.ExcludeFromCodeCoverage*").ExcludeByFile("*/*Designer.cs");

    foreach(var project in parameters.Packages.Tests) 
    {
        OpenCover(t => t
            .DotNetCoreTest(project.ProjectPath.ToString(), new DotNetCoreTestSettings
            {
                Framework = "netcoreapp2.0",
                NoBuild = true,
                NoRestore = true,
                Configuration = parameters.Configuration,
                ResultsDirectory = parameters.Paths.Directories.TestResultsDir,
                ArgumentCustomization = builder => builder.Append("--logger \"trx;LogFileName=./report.xml\"")
            }),
            parameters.Paths.Directories.TestResultsDir.CombineWithFilePath("./OpenCover.xml"),
            settings
        );
    }

});


Task("Upload-Test-Coverage")
    .WithCriteria(() => !parameters.IsLocalBuild)
    .IsDependentOn("Run-Unit-Tests")
    .Does(() =>
{
    // Resolve the API key.
    var token = EnvironmentVariable("COVERALLS_TOKEN");
    if (string.IsNullOrEmpty(token))
    {
        throw new Exception("The COVERALLS_TOKEN environment variable is not defined.");
    }

    CoverallsIo(parameters.Paths.Directories.TestResultsDir.CombineWithFilePath("./OpenCover.xml"), new CoverallsIoSettings()
    {
        RepoToken = token
    });
});

Task("Copy-Files")
    .IsDependentOn("Run-Unit-Tests")
    .Does(() =>
{
    // Copy files from artifact sources to artifact directory
    foreach(var project in parameters.Paths.Files.Projects.Where(x => x.OutputType != "Test")) 
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
    // Build nuget
    foreach(var project in parameters.Packages.Nuget) 
    {
        Information("Building nuget package: " + project.Id + " Version: " + parameters.Version.NuGet);
        DotNetCorePack(
            project.ProjectPath.ToString(),
            new DotNetCorePackSettings 
            {
                Configuration = parameters.Configuration,
                OutputDirectory = parameters.Paths.Directories.NugetRoot,
                NoBuild = true,
                Verbosity = parameters.IsLocalBuild ? DotNetCoreVerbosity.Quiet : DotNetCoreVerbosity.Normal,
                ArgumentCustomization = aggs => aggs
                    .Append(GetDotNetCoreArgsVersions(parameters.Version))
            }
        );

    }

});

Task("Publish-Artifactory")
    .IsDependentOn("Create-NuGet-Packages")
    .WithCriteria(() => parameters.ShouldPublishToArtifactory)
    .Does(() =>
    {
        var username = parameters.Artifactory.UserName;
        var password = parameters.Artifactory.Password;

        if(string.IsNullOrEmpty(username) && parameters.IsLocalBuild)
        {
            Console.Write("Artifactory UserName: ");
            username = Console.ReadLine();
        }
        if(string.IsNullOrEmpty(password) && parameters.IsLocalBuild)
        {
            Console.Write("Artifactory Password: ");
            password = Console.ReadLine();
        }
        var apiKey = string.Concat(username, ":", password);
        

        // Resolve the API url.
        var apiUrl = EnvironmentVariable("ARTIFACTORY_NUGET_URL");
        if(string.IsNullOrEmpty(apiUrl)) {
            throw new InvalidOperationException("Could not resolve Artifactory NuGet API url.");
        }

        foreach(var package in parameters.Packages.Nuget)
        {
            Information("Publish nuget to artifactory: " + package.PackagePath);
            var packageDir = string.Concat(apiUrl, "/", package.Id);

            // Push the package.
            NuGetPush(package.PackagePath, new NuGetPushSettings {
              ApiKey = apiKey,
              Source = packageDir
            });
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
    var apiUrl = EnvironmentVariable("NUGET_URL");
    if(string.IsNullOrEmpty(apiUrl)) {
        throw new InvalidOperationException("Could not resolve NuGet API url.");
    }

    foreach(var package in parameters.Packages.Nuget)
    {
		Information("Publish nuget: " + package.PackagePath);
        var packageDir = apiUrl;

		// Push the package.
		NuGetPush(package.PackagePath, new NuGetPushSettings {
		  ApiKey = apiKey,
		  Source = packageDir
		});
    }
});

Task("Upload-AppVeyor-Artifacts")
    .IsDependentOn("Zip-Files")
    .IsDependentOn("Upload-Test-Coverage")
    .IsDependentOn("Create-NuGet-Packages")
    .WithCriteria(() => parameters.IsRunningOnAppVeyor)
    .Does(() =>
{
    AppVeyor.UploadArtifact(parameters.Paths.Files.ZipBinaries);

    foreach(var package in GetFiles(parameters.Paths.Directories.NugetRoot + "/*"))
    {
        AppVeyor.UploadArtifact(package, new AppVeyorUploadArtifactsSettings {
            ArtifactType = AppVeyorUploadArtifactType.NuGetPackage
        });
    }
});

Task("Create-VSTS-Artifacts")
    .IsDependentOn("Zip-Files")
    .IsDependentOn("Upload-Test-Coverage")
    .IsDependentOn("Create-NuGet-Packages")
    .WithCriteria(() => parameters.IsRunningOnVSTS)
    .Does(context =>
{
    var commands = context.BuildSystem().TFBuild.Commands;

    commands.UploadArtifact("binaries", parameters.Paths.Files.ZipBinaries);

    foreach(var package in GetFiles(parameters.Paths.Directories.NugetRoot + "/*"))
    {
        commands.UploadArtifact("nuget", package);
    }

    commands.UploadArtifact("artifacts", parameters.Paths.Directories.ArtifactsDir + "/", "artifacts");
    commands.UploadArtifact("tests", parameters.Paths.Directories.TestResultsDir + "/", "tests");

    commands.AddBuildTag(parameters.Version.Sha);
    commands.AddBuildTag(parameters.Version.SemVersion);
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
  .IsDependentOn("Upload-Test-Coverage")
  .IsDependentOn("Publish-NuGet");
Task("GitHub")
  .IsDependentOn("Upload-Test-Coverage")
  .IsDependentOn("Publish-NuGet");

Task("VSTS")
  .IsDependentOn("Create-VSTS-Artifacts");
Task("VSTS-Publish")
  .IsDependentOn("Publish-Nuget")
  .IsDependentOn("Publish-Artifactory");

//////////////////////////////////////////////////////////////////////
// EXECUTION
//////////////////////////////////////////////////////////////////////

RunTarget(parameters.Target);
