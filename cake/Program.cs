using System.Threading.Tasks;
using Cake.Core.Diagnostics;
using Build.Extensions;
using Cake.Common.Build;
using Cake.Common.Xml;
using Build.Helpers;
using System.Linq;
using Cake.Common.Tools.OpenCover;
using Cake.Common.Tools.ReportGenerator;
using Cake.Curl;
using Cake.Common.Tools.NuGet;
using Cake.Common.Tools.NuGet.Push;
using Cake.Docker;
using Cake.Coverlet;
using System.Collections.Generic;
using Cake.Common.Tools.ReportUnit;
using Cake.Common.Build.AzurePipelines.Data;
using Cake.Common.Tools.DotNet.Test;
using Cake.Common.Tools.DotNet;
using Cake.Common.Tools.DotNet.Publish;
using Cake.Common.Tools.DotNet.Pack;
using Cake.Common.Tools.DotNet.Build;


namespace Build
{

    public static class Program
    {
        public static int Main(string[] args)
        {
            return new CakeHost()
                .InstallTool(new Uri("nuget:?package=GitVersion.CommandLine&version=5.11.1"))
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
        public override void Setup(BuildParameters parameters, ISetupContext context)
        {
            parameters.Setup();
            context.Info("==============================================");
            context.Info("==============================================");

            context.Info("Calculated Semantic Version: {0} sha: {1}", parameters.Version.SemVersion, parameters.Version.Sha.Substring(0, 8));
            context.Info("Calculated NuGet Version: {0}", parameters.Version.NuGet);

            context.Info("==============================================");
            context.Info("==============================================");

            context.Info("Solution: " + parameters.Solution);
            context.Info("Target: " + parameters.Target);
            context.Info("Configuration: " + parameters.BuildConfiguration);
            context.Info("IsLocalBuild: " + parameters.IsLocalBuild);
            context.Info("IsRunningOnUnix: " + parameters.IsRunningOnUnix);
            context.Info("IsRunningOnWindows: " + parameters.IsRunningOnWindows);
            context.Info("IsRunningOnVSTS: " + parameters.IsRunningOnVSTS);
            context.Info("IsReleaseBuild: " + parameters.IsReleaseBuild);
            context.Info("IsPullRequest: " + parameters.IsPullRequest);
            context.Info("IsMaster: " + parameters.IsMaster + " Branch: " + parameters.Branch);
            context.Info("BuildNumber: " + parameters.BuildNumber);

            // Increase verbosity?
            if (parameters.IsReleaseBuild && (context.Log.Verbosity != Verbosity.Diagnostic))
            {
                context.Info("Increasing verbosity to diagnostic.");
                context.Log.Verbosity = Verbosity.Diagnostic;
            }

            if (parameters.IsRunningOnVSTS)
            {
                var commands = context.BuildSystem().AzurePipelines.Commands;
                commands.UpdateBuildNumber(parameters.Version.SemVersion);
                commands.AddBuildTag(parameters.Version.Sha.Substring(0, 8));
                commands.AddBuildTag(parameters.Version.SemVersion);
                commands.AddBuildTag(parameters.BuildConfiguration);
            }

            context.Info("Building version {0} {5} of {4} ({1}, {2}) using version {3} of Cake",
                parameters.Version.SemVersion,
                parameters.Configuration,
                parameters.Target,
                parameters.Version.CakeVersion,
                parameters.Solution,
                parameters.Version.Sha.Substring(0, 8));
        }

        public override void Teardown(BuildParameters context, ITeardownContext info)
        {
            if (!context.ShouldPublish && context.ShouldHavePublished)
            {
                context.Log.Warning("This was not a successful build");
                throw new CakeException("Publish task chain failed");
            }
            if (info.ThrownException != null)
            {
                context.Log.Error($"Exception: {info.ThrownException.GetType().Name}: {info.ThrownException.Message}\n{info.ThrownException.StackTrace}");
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
            context.DotNetRestore(context.Solution.FullPath, new Cake.Common.Tools.DotNet.Restore.DotNetRestoreSettings
            {
                ConfigFile = "./tools/nuget.config",
                //Verbosity = context.IsLocalBuild ? DotNetCoreVerbosity.Quiet : DotNetCoreVerbosity.Detailed,
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
            context.DotNetBuild(context.Solution.FullPath,
            new DotNetBuildSettings
            {
                WorkingDirectory = context.Paths.Directories.BuildRoot,
                Configuration = context.BuildConfiguration,
                NoRestore = true,
                NoLogo = true,
                MSBuildSettings = context.MsBuildSettings,
                //Verbosity = context.IsLocalBuild ? DotNetCoreVerbosity.Quiet : DotNetCoreVerbosity.Detailed,
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
            context.HasFailed = true;
        }
        public override void Run(Helpers.BuildParameters context)
        {
            foreach (var project in context.Packages.Tests)
            {
                var testResults = context.Paths.Directories.TestResultsDir.Combine(project.Id);
                context.Info("Running tests {0} output directory {1}", project.Id, testResults);
                // shove every option into dotnet test arg customerization until Coverlet extension works
                var settings = new DotNetTestSettings
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
                context.DotNetTest(project.ProjectPath.FullPath, settings);
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
            return context.Packages.Tests.Any() && !context.HasFailed;
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
                settings.ReportTypes.Add(ReportGeneratorReportType.Cobertura);
                settings.ReportTypes.Add(ReportGeneratorReportType.Xml);

                settings.ReportTypes.Add(context.IsLocalBuild ? ReportGeneratorReportType.Html : ReportGeneratorReportType.HtmlInline_AzurePipelines);

                context.ReportGenerator(coverageFiles, context.Paths.Directories.TestResultsDir + "/generated", settings);
                context.Info("Test coverage report generated to {0}", context.Paths.Directories.TestResultsDir.CombineWithFilePath("index.htm"));


                if (context.FileExists(context.Paths.Directories.TestResultsDir.CombineWithFilePath("index.htm")))
                {
                    if (context.ShouldShowResults)
                    {
                        context.StartProcess("cmd", new ProcessSettings
                        {
                            Arguments = $"/C start \"\" {context.Paths.Directories.TestResultsDir}/generated/index.htm"
                        });
                    }
                    else if (context.CanShowResults)
                    {
                        context.Log.Warning("Generated coverage results to {0}/generated/index.htm - we can open them automatically if you add --results to command", context.Paths.Directories.TestResultsDir);
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
        public override void OnError(Exception exception, BuildParameters context)
        {
            context.Log.Warning("Failed to publish");
            context.HasFailed = true;
        }
        public override void Run(Helpers.BuildParameters context)
        {
            foreach (var project in context.Paths.Files.Projects.Where(x => x.OutputType != "Test"))
            {
                context.DotNetPublish(project.ProjectFile.FullPath, new DotNetPublishSettings
                {
                    WorkingDirectory = context.Paths.Directories.BuildRoot,
                    Configuration = context.BuildConfiguration,
                    NoRestore = true,
                    NoLogo = true,
                    MSBuildSettings = context.MsBuildSettings,
                    //Verbosity = context.IsLocalBuild ? DotNetCoreVerbosity.Quiet : DotNetCoreVerbosity.Detailed,
                    OutputDirectory = context.Paths.Directories.ArtifactsBin.Combine(project.AssemblyName)
                });
            }
        }
    }
    [TaskName("Zip-Files")]
    [IsDependentOn(typeof(PublishFilesTask))]
    public sealed class ZipFilesTask : FrostingTask<Helpers.BuildParameters>
    {
        public override void OnError(Exception exception, BuildParameters context)
        {
            context.Log.Warning("Failed to create zip");
            context.HasFailed = true;
        }
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
        public override void OnError(Exception exception, BuildParameters context)
        {
            context.Log.Warning("Failed to create Nuget packages");
            context.HasFailed = true;
        }
        public override void Run(Helpers.BuildParameters context)
        {
            // Build nuget
            foreach (var project in context.Packages.Nuget)
            {
                context.Info("Building nuget package: " + project.Id + " Version: " + context.Version.NuGet);
                context.DotNetPack(
                    project.ProjectPath.FullPath,
                    new DotNetPackSettings
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
                        //Verbosity = context.IsLocalBuild ? DotNetCoreVerbosity.Quiet : DotNetCoreVerbosity.Detailed,
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
        public override void OnError(Exception exception, BuildParameters context)
        {
            context.Log.Warning("Failed to publish Nuget packages");
            context.HasFailed = true;
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
                // context.DotNetCoreNuGetPush(package.PackagePath.FullPath, new DotNetCoreNuGetPushSettings
                // {
                //     ApiKey = apiKey,
                //     Source = apiUrl,
                // });
                context.NuGetPush(package.PackagePath, new NuGetPushSettings
                {
                    ConfigFile = "./tools/nuget.config",
                    ApiKey = apiKey,
                    Source = apiUrl,
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
        public override void OnError(Exception exception, BuildParameters context)
        {
            context.Log.Warning("Failed to publish Azure artifacts");
            context.HasFailed = true;
        }
        public override void Run(Helpers.BuildParameters context)
        {

            var commands = context.BuildSystem().AzurePipelines.Commands;
            commands.UploadArtifact("bin", context.Paths.Files.ZipBinaries, "zip");

            // nugets published already
            // foreach (var package in context.Packages.Nuget)
            // {
            //     commands.UploadArtifact("nuget", package.PackagePath, package.Id);
            // }

            // dup artifact upload
            //commands.UploadArtifact("artifacts", context.Paths.Directories.ArtifactsDir + "/", "artifacts");
            //commands.UploadArtifact("tests", context.Paths.Directories.TestResultsDir + "/", "test-results");

            var testResults = context.GetFiles($"{context.Paths.Directories.TestResultsDir}/**/*.trx").ToArray();
            if (testResults.Any())
            {
                commands.PublishTestResults(new AzurePipelinesPublishTestResultsData
                {
                    Configuration = context.BuildConfiguration,
                    MergeTestResults = true,
                    TestResultsFiles = testResults,
                    TestRunner = AzurePipelinesTestRunnerType.VSTest
                });
            }
            var codeCoverage = context.GetFiles($"{context.Paths.Directories.TestResultsDir}/**/*.cobertura.xml").ToArray();
            if (codeCoverage.Any())
            {
                commands.PublishCodeCoverage(new AzurePipelinesPublishCodeCoverageData
                {
                    AdditionalCodeCoverageFiles = codeCoverage,
                    CodeCoverageTool = AzurePipelinesCodeCoverageToolType.Cobertura,
                    ReportDirectory = context.Paths.Directories.TestResultsDir + "/generated",
                    SummaryFileLocation = context.Paths.Directories.TestResultsDir + "/generated/Cobertura.xml"
                });
            }


            commands.AddBuildTag(context.Version.Sha);
            commands.AddBuildTag(context.Version.SemVersion);
        }
    }
    [TaskName("Create-GitHub-Artifacts")]
    [IsDependentOn(typeof(ZipFilesTask))]
    [IsDependentOn(typeof(CreateNugetPackagesTask))]
    [IsDependentOn(typeof(GenerateCoverageReportTask))]
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
    [IsDependentOn(typeof(CreateNugetPackagesTask))]
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
    public class VSTSTask : FrostingTask { }

    [TaskName("GitHub")]
    [IsDependentOn(typeof(PublishTask))]
    [IsDependentOn(typeof(CreateGitHubArtifactsTask))]
    public class GitHubTask : FrostingTask { }
}