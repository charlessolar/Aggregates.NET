using Cake.Common;
using Cake.Common.Build;
using Cake.Common.Diagnostics;
using Cake.Common.IO;
using Cake.Common.Tools.DotNetCore;
using Cake.Common.Tools.DotNetCore.MSBuild;
using Cake.Core;
using Cake.Core.IO;
using Cake.Frosting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Build.Helpers
{

    public class BuildParameters : FrostingContext
    {

        public BuildParameters(ICakeContext context)
            : base(context)
        {
            initialize(context);
        }

        public FilePath Solution { get; private set; }
        public string Target { get; private set; }
        public string BuildConfiguration { get; private set; }
        public bool IsLocalBuild { get; private set; }
        public bool IsRunningOnUnix { get; private set; }
        public bool IsRunningOnWindows { get; private set; }
        public bool IsRunningOnVSTS { get; private set; }
        public bool IsRunningOnAppVeyor { get; private set; }
        public bool IsRunningOnGitHub { get; private set; }
        public bool ResultsRequested { get; private set; }
        public string Repository { get; private set; }
        public BuildCredentials GitHub { get; private set; }
        public BuildCredentials Artifactory { get; private set; }
        public BuildVersion Version { get; private set; }
        public BuildPaths Paths { get; private set; }
        public BuildPackages Packages { get; private set; }
        public int BuildNumber { get; private set; }
        public string Branch { get; private set; }
        public bool IsMaster { get; private set; }
        public bool IsPullRequest { get; private set; }

        public bool TestFailures { get; set; }

        public DotNetCoreMSBuildSettings MsBuildSettings { get; private set; }


        public bool IsPreRelease
        {
            get
            {
                return !IsMaster;
            }
        }
        public bool IsReleaseBuild
        {
            get
            {
                return (IsRunningOnAppVeyor || IsRunningOnGitHub || IsRunningOnVSTS) && !IsLocalBuild;
            }
        }
        public bool ShouldPublish
        {
            get
            {
                return !IsLocalBuild && IsReleaseBuild && !IsPullRequest && !TestFailures;
            }
        }
        public bool ShouldHavePublished
        {
            get
            {
                return !IsLocalBuild && IsReleaseBuild && !IsPullRequest;
            }
        }

        public bool ShouldBuildNuget
        {
            get
            {
                return Packages.Nuget.Any() && !TestFailures;
            }
        }
        public bool ShouldBuildDocker
        {
            get
            {
                return Packages.Images.Any() && !TestFailures;
            }
        }
        public bool ShouldBuildBinaries
        {
            get
            {
                return Packages.Binaries.Any() && !TestFailures;
            }
        }
        public bool ShouldPublishToArtifactory
        {
            get
            {
                return IsRunningOnVSTS && ShouldPublish;
            }
        }
        public bool ShouldShowResults
        {
            get
            {
                return IsRunningOnWindows && IsLocalBuild && ResultsRequested;
            }
        }
        public bool CanShowResults
        {
            get
            {
                return IsRunningOnWindows && IsLocalBuild;
            }
        }

        private void initialize(ICakeContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException("context");
            }


            var solution = context.GetFiles("../**/*.sln").FirstOrDefault();
            if (solution == null)
            {
                context.Information("Unable to find solution in directory!");
                throw new InvalidOperationException("Unable to find solution in directory!");
            }

            var target = context.Argument("target", "Default");
            var results = context.HasArgument("results");
            var buildSystem = context.BuildSystem();

            var repository = "";

            var isVSTS = buildSystem.IsRunningOnAzurePipelines || buildSystem.IsRunningOnAzurePipelinesHosted;

            var buildNumber = 0;
            var branch = "master";
            var pr = false;
            if (buildSystem.AppVeyor.IsRunningOnAppVeyor)
            {
                buildNumber = buildSystem.AppVeyor.Environment.Build.Number;
                branch = buildSystem.AppVeyor.Environment.Repository.Branch;
                pr = buildSystem.AppVeyor.Environment.PullRequest.IsPullRequest;
            }
            if (isVSTS)
            {
                buildNumber = buildSystem.AzurePipelines.Environment.Build.Id;
                branch = buildSystem.AzurePipelines.Environment.Repository.SourceBranchName;
                repository = context.EnvironmentVariable("BUILD_REPOSITORY_URI");
            }
            if (buildSystem.GitHubActions.IsRunningOnGitHubActions)
            {
                buildNumber = -1;
                int.TryParse(context.EnvironmentVariable("GITHUB_RUN_NUMBER"), out buildNumber);
                branch = buildSystem.GitHubActions.Environment.Workflow.Ref.Replace("refs/heads/", "");
                repository = buildSystem.GitHubActions.Environment.Workflow.Repository;
            }

            Solution = solution.MakeAbsolute(context.Environment);
            Target = target;
            BuildConfiguration = context.Argument("configuration", "Release");
            IsLocalBuild = buildSystem.IsLocalBuild;
            IsRunningOnUnix = context.IsRunningOnUnix();
            IsRunningOnWindows = context.IsRunningOnWindows();
            IsRunningOnVSTS = isVSTS;
            IsRunningOnGitHub = buildSystem.GitHubActions.IsRunningOnGitHubActions;
            ResultsRequested = results;
            Repository = repository;
            GitHub = BuildCredentials.GetGitHubCredentials(context);
            Artifactory = BuildCredentials.GetArtifactoryCredentials(context);
            BuildNumber = buildNumber;
            Branch = branch;
            IsMaster = StringComparer.OrdinalIgnoreCase.Equals("master", branch);
            IsPullRequest = pr;

        }
        public void Setup()
        {

            Version = BuildVersion.Calculate(this);

            Paths = BuildPaths.GetPaths(this, BuildConfiguration, Version.SemVersion);

            Packages = BuildPackages.GetPackages(
                this,
                IsRunningOnWindows,
                Version,
                Paths.Directories.ArtifactsBin,
                Paths.Directories.NugetRoot,
                Paths.Files.Projects
                );

            MsBuildSettings = new DotNetCoreMSBuildSettings()
            {
                Verbosity = DotNetCoreVerbosity.Minimal,
            }
            .WithProperty("Version", Version.NuGet)
            .WithProperty("AssemblyVersion", Version.SemVersion)
            .WithProperty("FileVersion", Version.SemVersion);
        }


    }

}
