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
using Cake.Common.Tools.ReportUnit;
using Cake.Core.IO;
using Cake.Common;
using Cake.Common.Build.AzurePipelines.Data;
using Cake.Common.Tools.DotNetCore.NuGet.Push;
using Cake.AzurePipelines.Module;

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
                .UseModule<AzurePipelinesModule>()
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
            context.Info("Configuration: " + context.BuildConfiguration);
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
            if (!context.ShouldPublish && context.ShouldHavePublished)
            {
                context.Log.Warning("This was not a successful build");
                throw new CakeException("Publish task chain failed");
            }
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
            context.CopyFile("./cake/nuget.config", "./tools/nuget.config");

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
                Verbosity = context.IsLocalBuild ? DotNetCoreVerbosity.Quiet : DotNetCoreVerbosity.Detailed,
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
                WorkingDirectory = context.Paths.Directories.BuildRoot,
                Configuration = context.BuildConfiguration,
                NoRestore = true,
                NoLogo = true,
                MSBuildSettings = context.MsBuildSettings,
                Verbosity = context.IsLocalBuild ? DotNetCoreVerbosity.Quiet : DotNetCoreVerbosity.Detailed,
                ArgumentCustomization = aggs =>
                {
                    aggs.Append("/p:SourceLinkEnabled=true")
                        .Append("/p:UseSourceLink=true");
                    if (!context.IsLocalBuild)
                        aggs.Append("/p:Deterministic=true")
                            .Append("/p:ContinuousIntegrationBuild=true");
                    return aggs;
                }
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
            foreach (var project in context.Packages.Tests)
            {
                var testResults = context.Paths.Directories.TestResultsDir.Combine(project.Id);
                context.Info("Running tests {0} output directory {1}", project.Id, testResults);
                // shove every option into dotnet test arg customerization until Coverlet extension works
                var settings = new DotNetCoreTestSettings
                {
                    Configuration = context.BuildConfiguration,
                    NoBuild = true,
                    ArgumentCustomization = args => args
                        .Append($"--logger \"trx;LogFileName=TestResults.trx\"")
                        .Append($"--logger \"xunit;LogFileName=TestResults.xml\"")
                        .Append($"--logger \"html;LogFileName=TestResults.html\"")
                        .Append($"--results-directory \"{testResults}\"")
                        .Append("--collect \"XPlat Code Coverage\"")
                        .Append("--settings ./coverlet.runsettings")
                };



                //coverletSettings.WorkingDirectory = project.ProjectPath.GetDirectory();
                context.DotNetCoreTest(project.ProjectPath.FullPath, settings);
                //context.Coverlet(project.ProjectPath.GetDirectory(), coverletSettings, true, context.BuildConfiguration);

                if (context.FileExists(testResults.CombineWithFilePath("TestResults.html")))
                {
                    if (context.ShouldShowResults)
                    {
                        context.StartProcess("cmd", new ProcessSettings
                        {
                            Arguments = $"/C start \"\" {testResults}/TestResults.html"
                        });
                    }
                    else if (context.CanShowResults)
                    {
                        context.Log.Warning("Generated test results to {0}/TestResults.html - we can open them automatically if you add --results to command", testResults);
                    }
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
            return context.Packages.Tests.Any() && !context.TestFailures;
        }
        public override void OnError(Exception exception, BuildParameters context)
        {
            Program.HandleToolMissingException(exception, "dotnet-reportgenerator-globaltool", context);
        }
        public override void Run(Helpers.BuildParameters context)
        {
            var coverageFiles = context.GetFiles(context.Paths.Directories.TestResultsDir + "/**/*.cobertura.xml");
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
                {
                    settings.ReportTypes.Add(ReportGeneratorReportType.Cobertura);
                    settings.ReportTypes.Add(ReportGeneratorReportType.HtmlInline_AzurePipelines);
                }

                context.ReportGenerator(coverageFiles, context.Paths.Directories.TestResultsDir + "/report", settings);
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
                    WorkingDirectory = context.Paths.Directories.BuildRoot,
                    Configuration = context.BuildConfiguration,
                    NoRestore = true,
                    NoLogo = true,
                    MSBuildSettings = context.MsBuildSettings,
                    Verbosity = context.IsLocalBuild ? DotNetCoreVerbosity.Quiet : DotNetCoreVerbosity.Detailed,
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
                    project.ProjectPath.FullPath,
                    new DotNetCorePackSettings
                    {
                        WorkingDirectory = context.Paths.Directories.BuildRoot,
                        Configuration = context.BuildConfiguration,
                        OutputDirectory = context.Paths.Directories.NugetRoot,
                        // uses sourcelink now
                        //IncludeSource = true,
                        //IncludeSymbols = true,
                        NoBuild = true,
                        NoRestore = true,
                        NoLogo = true,
                        Verbosity = context.IsLocalBuild ? DotNetCoreVerbosity.Quiet : DotNetCoreVerbosity.Detailed,
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
            var apiUrl = context.EnvironmentVariable("NUGET_URL");
            var apiKey = context.EnvironmentVariable("NUGET_API_KEY");
            if (string.IsNullOrEmpty(apiUrl) || string.IsNullOrEmpty(apiKey))
            {
                throw new InvalidOperationException("Could not resolve NuGet API url.");
            }

            foreach (var package in context.Packages.Nuget)
            {
                context.Info("Publish nuget: " + package.PackagePath);

                // Push the package.
                context.DotNetCoreNuGetPush(package.PackagePath.FullPath, new DotNetCoreNuGetPushSettings
                {
                    Verbosity = context.IsLocalBuild ? DotNetCoreVerbosity.Quiet : DotNetCoreVerbosity.Detailed,
                    ApiKey = apiKey,
                    Source = apiUrl,
                });
                // context.NuGetPush(package.PackagePath, new NuGetPushSettings
                // {
                //     ConfigFile = "./tools/nuget.config",
                //     Verbosity = context.IsLocalBuild ? DotNetCoreVerbosity.Quiet : DotNetCoreVerbosity.Detailed,
                //     ApiKey = apiKey,
                //     Source = apiUrl,
                // });
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

            // nugets published already
            // foreach (var package in context.Packages.Nuget)
            // {
            //     commands.UploadArtifact("nuget", package.PackagePath, package.Id);
            // }

            // dup artifact upload
            //commands.UploadArtifact("artifacts", context.Paths.Directories.ArtifactsDir + "/", "artifacts");
            commands.UploadArtifact("tests", context.Paths.Directories.TestResultsDir + "/", "test-results");
            var testResults = context.GetFiles($"{context.Paths.Directories.TestResultsDir}/*.trx").ToArray();

            commands.PublishTestResults(new AzurePipelinesPublishTestResultsData
            {

                Configuration = context.BuildConfiguration,
                MergeTestResults = true,
                TestResultsFiles = testResults,
                TestRunner = AzurePipelinesTestRunnerType.VSTest
            });
            var codeCoverage = context.GetFiles($"{context.Paths.Directories.TestResultsDir}/coverage/*.xml").ToArray();
            commands.PublishCodeCoverage(new AzurePipelinesPublishCodeCoverageData
            {
                AdditionalCodeCoverageFiles = codeCoverage,
                CodeCoverageTool = AzurePipelinesCodeCoverageToolType.Cobertura,
                ReportDirectory = context.Paths.Directories.TestResultsDir + "/report",
                SummaryFileLocation = context.Paths.Directories.TestResultsDir + "/report/coverage.xml"
            });


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