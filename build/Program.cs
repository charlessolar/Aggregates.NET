using System.Threading.Tasks;
using Cake.Core;
using Cake.Core.Diagnostics;
using Cake.Frosting;
using Build.Extensions;
using Cake.Common.Build;
using Cake.Common.IO;
using System;
using Cake.Common.Xml;
using Cake.Common.Tools.DotNetCore;
using Cake.Common.Tools.DotNetCore.MSBuild;
using Cake.Common.Tools.DotNetCore.Build;
using Build.Helpers;
using System.Linq;
using Cake.Common.Tools.OpenCover;
using Cake.Common.Tools.DotNetCore.Test;
using Cake.Common.Tools.ReportGenerator;
using Cake.Common.Tools.DotNetCore.Publish;
using Cake.Curl;
using Cake.Common.Tools.DotNetCore.Pack;
using Cake.Common.Tools.NuGet;
using Cake.Common.Tools.NuGet.Push;
using Cake.Docker;
using Cake.Coverlet;
using System.Collections.Generic;
using Build.Tool;
using Cake.Common.Tools.ReportUnit;
using Cake.Core.IO;
using Cake.Common;

namespace Build
{

    public static class Program
    {
        public static int Main(string[] args)
        {
            return new CakeHost()
                .InstallTool(new Uri("nuget:?package=GitVersion.CommandLine&version=5.6.9"))
                .UseContext<Helpers.BuildParameters>()
                .UseLifetime<BuildLifetime>()
                .UseWorkingDirectory("..")
                .Run(args);
        }

        public static void HandleToolMissingException(Exception e, string tool, ICakeContext context)
        {
            if (e.Message.Contains("Could not locate", StringComparison.OrdinalIgnoreCase))
            {
                context.Log.Warning("This task requires the global tool {0} - for better results install it", tool);
            }
        }
    }

    public sealed class BuildLifetime : FrostingLifetime<Helpers.BuildParameters>
    {
        public override void Setup(BuildParameters context)
        {
            context.Setup();
            context.Info("==============================================");
            context.Info("==============================================");

            context.Info("Calculated Semantic Version: {0} sha: {1}", context.Version.SemVersion, context.Version.Sha.Substring(0, 8));
            context.Info("Calculated NuGet Version: {0}", context.Version.NuGet);

            context.Info("==============================================");
            context.Info("==============================================");

            context.Info("Solution: " + context.Solution);
            context.Info("Target: " + context.Target);
            context.Info("Configuration: " + context.Configuration);
            context.Info("IsLocalBuild: " + context.IsLocalBuild);
            context.Info("IsRunningOnUnix: " + context.IsRunningOnUnix);
            context.Info("IsRunningOnWindows: " + context.IsRunningOnWindows);
            context.Info("IsRunningOnVSTS: " + context.IsRunningOnVSTS);
            context.Info("IsReleaseBuild: " + context.IsReleaseBuild);
            context.Info("IsPullRequest: " + context.IsPullRequest);
            context.Info("IsMaster: " + context.IsMaster + " Branch: " + context.Branch);
            context.Info("BuildNumber: " + context.BuildNumber);

            // Increase verbosity?
            if (context.IsReleaseBuild && (context.Log.Verbosity != Verbosity.Diagnostic))
            {
                context.Info("Increasing verbosity to diagnostic.");
                context.Log.Verbosity = Verbosity.Diagnostic;
            }

            if (context.IsRunningOnVSTS)
            {
                var commands = context.BuildSystem().AzurePipelines.Commands;
                commands.UpdateBuildNumber(context.Version.SemVersion);
                commands.AddBuildTag(context.Version.Sha.Substring(0, 8));
                commands.AddBuildTag(context.Version.SemVersion);
                commands.AddBuildTag(context.BuildConfiguration);
            }


            context.Info("Building version {0} {5} of {4} ({1}, {2}) using version {3} of Cake",
                context.Version.SemVersion,
                context.Configuration,
                context.Target,
                context.Version.CakeVersion,
                context.Solution,
                context.Version.Sha.Substring(0, 8));
        }

        public override void Teardown(BuildParameters context, ITeardownContext info)
        {
        }
    }
    [TaskName("Clean")]
    public sealed class CleanTask : FrostingTask<Helpers.BuildParameters>
    {
        public override void Run(Helpers.BuildParameters context)
        {
            context.CleanDirectories(context.Paths.Directories.ToClean);
            context.EnsureDirectoryExists(context.Paths.Directories.TestResultsDir);
        }
    }

    [TaskName("Generate-NuGet-Config")]
    [IsDependentOn(typeof(CleanTask))]
    public sealed class GenerateNugetConfigTask : FrostingTask<Helpers.BuildParameters>
    {
        public override void Run(Helpers.BuildParameters context)
        {

            // Copy file into an ignored folder
            context.CopyFile("./build/nuget.config", "./tools/nuget.config");

        }
    }

    [TaskName("Restore-NuGet-Packages")]
    [IsDependentOn(typeof(GenerateNugetConfigTask))]
    public sealed class RestoreNugetPackagesTask : FrostingTask<Helpers.BuildParameters>
    {
        public override void Run(Helpers.BuildParameters context)
        {
            context.DotNetCoreRestore(context.Solution.FullPath, new Cake.Common.Tools.DotNetCore.Restore.DotNetCoreRestoreSettings
            {
                ConfigFile = "./tools/nuget.config",
                MSBuildSettings = context.MsBuildSettings
            });
        }
    }



    [TaskName("Build")]
    [IsDependentOn(typeof(RestoreNugetPackagesTask))]
    public sealed class BuildTask : FrostingTask<Helpers.BuildParameters>
    {
        public override void Run(Helpers.BuildParameters context)
        {
            context.DotNetCoreBuild(context.Solution.FullPath,
            new DotNetCoreBuildSettings
            {
                Configuration = context.BuildConfiguration,
                NoRestore = true,
                NoLogo = true,
                MSBuildSettings = context.MsBuildSettings,
                ArgumentCustomization = aggs => aggs
                        .Append("/p:ci=true")
                        .Append("/p:SourceLinkEnabled=true")
                        .Append("/p:UseSourceLink=true")
            });

        }
    }


    [TaskName("Run-Unit-Tests")]
    [IsDependentOn(typeof(BuildTask))]
    public sealed class RunUnitTestsTask : FrostingTask<Helpers.BuildParameters>
    {
        public override bool ShouldRun(BuildParameters context)
        {
            return context.Packages.Tests.Any();
        }
        public override void OnError(Exception exception, BuildParameters context)
        {
            context.Log.Warning("Unit Test failures... will not publish artifacts");
            context.TestFailures = true;
        }
        public override void Run(Helpers.BuildParameters context)
        {
            context.Info("Outputing coverage to {0}", context.Paths.Directories.TestResultsDir);

            var coverletSettings = new CoverletSettings
            {
                CollectCoverage = true,
                // It is problematic to merge the reports into one, as such we use a custom directory for coverage results
                CoverletOutputDirectory = context.Paths.Directories.TestResultsDir.Combine("coverlet"),
                CoverletOutputFormat = CoverletOutputFormat.opencover,

            };
            coverletSettings
                .WithFileExclusion("*/*Designer.cs")
                .WithFileExclusion("[xunit.*]*")
                .WithFileExclusion("[*Tests]*")
                .WithAttributeExclusion("ExcludeFromCodeCoverage")
                .WithAttributeExclusion("CompilerGeneratedAttribute")
                .WithAttributeExclusion("Obsolete");

            var coverletFullPath = coverletSettings.CoverletOutputDirectory.FullPath;
            // shove every option into dotnet test arg customerization until Coverlet extension works
            var settings = new DotNetCoreTestSettings
            {
                Configuration = context.BuildConfiguration,
                NoBuild = true,
                HandleExitCode = (_) => true,
                ArgumentCustomization = args => args
                    .Append("--logger \"trx;LogFileName=TestResults.trx\"")
                    .Append("--logger \"xunit;LogFileName=TestResults.xml\"")
                    .Append("--logger \"html;LogFileName=TestResults.html\"")
                    .Append($"--results-directory \"{context.Paths.Directories.TestResultsDir}\"")
                    .Append("--collect:\"XPlat Code Coverage\"")
                    .Append("--settings build/coverlet.runsettings")
            };

            foreach (var project in context.Packages.Tests)
            {
                coverletSettings.CoverletOutputName = project.Id.Replace('.', '-');
                //coverletSettings.WorkingDirectory = project.ProjectPath.GetDirectory();
                context.DotNetCoreTest(project.ProjectPath.FullPath, settings);
                //context.Coverlet(project.ProjectPath.GetDirectory(), coverletSettings, true, context.BuildConfiguration);
            }

            if (context.FileExists(context.Paths.Directories.TestResultsDir.CombineWithFilePath("TestResults.html")))
            {
                if (context.ShouldShowResults)
                {
                    context.StartProcess("cmd", new ProcessSettings
                    {
                        Arguments = $"/C start \"\" {context.Paths.Directories.TestResultsDir}/TestResults.html"
                    });
                }
                else if (context.CanShowResults)
                {
                    context.Log.Warning("Generated test results to {0}/TestResults.html - we can open them automatically if you add --results to command", context.Paths.Directories.TestResultsDir);
                }
            }
        }
    }

    [TaskName("Generate-Coverage-Report")]
    [IsDependentOn(typeof(RunUnitTestsTask))]
    public sealed class GenerateCoverageReportTask : FrostingTask<Helpers.BuildParameters>
    {
        public override bool ShouldRun(BuildParameters context)
        {
            return context.Packages.Tests.Any();
        }
        public override void OnError(Exception exception, BuildParameters context)
        {
            Program.HandleToolMissingException(exception, "dotnet-reportgenerator-globaltool", context);
        }
        public override void Run(Helpers.BuildParameters context)
        {
            var coverageFiles = context.GetFiles(context.Paths.Directories.TestResultsDir + "/**/coverage.cobertura.xml");
            // Generage nice human readable coverage report
            if (coverageFiles.Any())
            {
                var settings = new ReportGeneratorSettings()
                {
                    Verbosity = ReportGeneratorVerbosity.Error,
                    ClassFilters = new[] { "-System*", "-Microsoft*" },
                };
                settings.ReportTypes.Clear();
                settings.ReportTypes.Add(ReportGeneratorReportType.Badges);
                settings.ReportTypes.Add(ReportGeneratorReportType.HtmlInline);

                if (context.IsRunningOnVSTS)
                    settings.ReportTypes.Add(ReportGeneratorReportType.HtmlInline_AzurePipelines);

                context.ReportGenerator(coverageFiles, context.Paths.Directories.TestResultsDir, settings);
                context.Info("Test coverage report generated to {0}", context.Paths.Directories.TestResultsDir.CombineWithFilePath("index.htm"));


                if (context.FileExists(context.Paths.Directories.TestResultsDir.CombineWithFilePath("index.htm")))
                {
                    if (context.ShouldShowResults)
                    {
                        context.StartProcess("cmd", new ProcessSettings
                        {
                            Arguments = $"/C start \"\" {context.Paths.Directories.TestResultsDir}/index.htm"
                        });
                    }
                    else if (context.CanShowResults)
                    {
                        context.Log.Warning("Generated coverage results to {0}/index.htm - we can open them automatically if you add --results to command", context.Paths.Directories.TestResultsDir);
                    }
                }
            }
            else
            {
                context.Log.Warning("No coverage files was found, no local report is generated!");
            }


        }
    }

    // Different from "Copy-Files" because `dotnet publish` creates output without
    // *.deps.json, extracts all the depends into the same folder, and in general
    // copies everything the app needs to run
    // Used in Docker-Build and Binary-Build

    [TaskName("Publish-Files")]
    [IsDependentOn(typeof(RunUnitTestsTask))]
    public sealed class PublishFilesTask : FrostingTask<Helpers.BuildParameters>
    {
        public override void Run(Helpers.BuildParameters context)
        {
            foreach (var project in context.Paths.Files.Projects.Where(x => x.OutputType != "Test"))
            {
                context.DotNetCorePublish(project.ProjectFile.FullPath, new DotNetCorePublishSettings
                {
                    Configuration = context.BuildConfiguration,
                    NoRestore = true,
                    MSBuildSettings = context.MsBuildSettings,
                    OutputDirectory = context.Paths.Directories.ArtifactsBin.Combine(project.AssemblyName)
                });
            }
        }
    }
    [TaskName("Zip-Files")]
    [IsDependentOn(typeof(PublishFilesTask))]
    public sealed class ZipFilesTask : FrostingTask<Helpers.BuildParameters>
    {
        public override void Run(Helpers.BuildParameters context)
        {
            var files = context.GetFiles(context.Paths.Directories.ArtifactsBin + "/**/*");
            context.Zip(context.Paths.Directories.ArtifactsBin, context.Paths.Files.ZipBinaries, files);

        }
    }


    [TaskName("Create-Nuget-Packages")]
    [IsDependentOn(typeof(PublishFilesTask))]
    public sealed class CreateNugetPackagesTask : FrostingTask<Helpers.BuildParameters>
    {
        public override bool ShouldRun(BuildParameters context)
        {
            return context.ShouldBuildNuget;
        }
        public override void Run(Helpers.BuildParameters context)
        {
            // Build nuget
            foreach (var project in context.Packages.Nuget)
            {
                context.Info("Building nuget package: " + project.Id + " Version: " + context.Version.NuGet);
                context.DotNetCorePack(
                    project.ProjectPath.ToString(),
                    new DotNetCorePackSettings
                    {
                        Configuration = context.BuildConfiguration,
                        OutputDirectory = context.Paths.Directories.NugetRoot,
                        // todo: use sourcelink once auth gits are supported
                        IncludeSource = true,
                        IncludeSymbols = true,
                        NoBuild = true,
                        NoRestore = true,
                        Verbosity = context.IsLocalBuild ? DotNetCoreVerbosity.Quiet : DotNetCoreVerbosity.Normal,
                        MSBuildSettings = context.MsBuildSettings
                    }
                );

            }
        }
    }
    [TaskName("Publish-Nuget-Packages")]
    [IsDependentOn(typeof(CreateNugetPackagesTask))]
    public sealed class PublishNugetPackagesTask : FrostingTask<Helpers.BuildParameters>
    {
        public override bool ShouldRun(BuildParameters context)
        {
            return context.ShouldPublish;
        }
        public override void Run(Helpers.BuildParameters context)
        {

            // Resolve the API url.
            var apiUrl = context.EnvironmentVariable("ARTIFACTORY_NUGET_URL");
            if (string.IsNullOrEmpty(apiUrl) || string.IsNullOrEmpty(context.ApiKey))
            {
                throw new InvalidOperationException("Could not resolve NuGet API url.");
            }

            foreach (var package in context.Packages.Nuget)
            {
                context.Info("Publish nuget: " + package.PackagePath);
                var packageDir = string.Concat(apiUrl, "/", package.Id);

                // Push the package.
                context.NuGetPush(package.PackagePath, new NuGetPushSettings
                {
                    ApiKey = context.ApiKey,
                    Source = packageDir
                });
            }
        }
    }



    [TaskName("Create-VSTS-Artifacts")]
    [IsDependentOn(typeof(ZipFilesTask))]
    [IsDependentOn(typeof(CreateNugetPackagesTask))]
    [IsDependentOn(typeof(GenerateCoverageReportTask))]
    public sealed class CreateVSTSArtifactsTask : FrostingTask<Helpers.BuildParameters>
    {
        public override bool ShouldRun(BuildParameters context)
        {
            return context.IsRunningOnVSTS;
        }
        public override void Run(Helpers.BuildParameters context)
        {

            var commands = context.BuildSystem().AzurePipelines.Commands;
            commands.UploadArtifact("binaries", context.Paths.Files.ZipBinaries, "zip");

            foreach (var package in context.Packages.Nuget)
            {
                commands.UploadArtifact("nuget", package.PackagePath, package.Id);
            }

            commands.UploadArtifact("artifacts", context.Paths.Directories.ArtifactsDir + "/", "artifacts");
            commands.UploadArtifact("tests", context.Paths.Directories.TestResultsDir + "/", "test-results");

            commands.AddBuildTag(context.Version.Sha);
            commands.AddBuildTag(context.Version.SemVersion);
        }
    }
    [TaskName("Create-GitHub-Artifacts")]
    [IsDependentOn(typeof(ZipFilesTask))]
    [IsDependentOn(typeof(CreateNugetPackagesTask))]
    public sealed class CreateGitHubArtifactsTask : FrostingTask<Helpers.BuildParameters>
    {
        public override bool ShouldRun(BuildParameters context)
        {
            return context.IsRunningOnGitHub;
        }
        public override void Run(Helpers.BuildParameters context)
        {
            // GitHub has no commands support yet
            // use the action's yaml

        }
    }

    //////////////////////////////////////////////////////////////////////
    // TASK TARGETS
    //////////////////////////////////////////////////////////////////////

    // Typical use running ./build.ps1
    [TaskName("Default")]
    [IsDependentOn(typeof(GenerateCoverageReportTask))]
    public class DefaultTask : FrostingTask { }

    // Utility targets
    [TaskName("Package")]
    [IsDependentOn(typeof(ZipFilesTask))]
    public class PackageTask : FrostingTask { }

    [TaskName("Publish")]
    [IsDependentOn(typeof(PublishNugetPackagesTask))]
    public class PublishTask : FrostingTask { }

    // Build pipeline targets
    [TaskName("VSTS")]
    [IsDependentOn(typeof(PublishTask))]
    [IsDependentOn(typeof(CreateVSTSArtifactsTask))]
    [IsDependentOn(typeof(GenerateCoverageReportTask))]
    public class VSTSTask : FrostingTask { }

    [TaskName("GitHub")]
    [IsDependentOn(typeof(PublishTask))]
    [IsDependentOn(typeof(CreateGitHubArtifactsTask))]
    public class GitHubTask : FrostingTask { }
}