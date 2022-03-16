using Cake.Core;
using Cake.Core.IO;
using Cake.Docker;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Build.Helpers
{
    public class BuildPackages
    {
        public IEnumerable<BuildPackage> Nuget { get; private set; }
        public IEnumerable<BuildDocker> Images { get; private set; }
        public IEnumerable<BuildBinary> Binaries { get; private set; }
        public IEnumerable<BuildTest> Tests { get; private set; }


        public static BuildPackages GetPackages(
            ICakeContext context,
            bool windows,
            BuildVersion version,
            DirectoryPath artifactsDir,
            DirectoryPath nugetDir,
            IEnumerable<ProjectInfo> projects)
        {
            var nugets = projects.Where(x => x.OutputType == "Library" && !x.ProjectName.EndsWith("UnitTests")).Select(project =>
                new BuildPackage(
                    id: project.AssemblyName,
                    projectPath: project.ProjectFile.FullPath,
                    packagePath: nugetDir.CombineWithFilePath(string.Concat(project.AssemblyName, ".", version.NuGet, ".nupkg"))
                ));


            var dockerfiles = projects.Where(x => x.OutputType == "Docker" || x.OutputType == "Web").Select(project => {
                return new BuildDocker(
                    id: project.AssemblyName.ToLower(),
                    baseDir: artifactsDir.Combine(project.AssemblyName),
                    projectPath: project.ProjectFile.FullPath,
                    packagePath: artifactsDir.CombineWithFilePath(string.Concat(project.AssemblyName, "-", version.SemVersion + ".tar")),
                    settings: new DockerImageBuildSettings
                    {
                        File = artifactsDir.Combine(project.AssemblyName).CombineWithFilePath("Dockerfile").FullPath,
                        BuildArg = new[] {
                        "CONT_IMG_VER=" + version.SemVersion
                        },
                        Label = new[] {
                        "Commit=" + version.Sha.Substring(0,8)
                        },
                        Tag = new[] {
                        project.AssemblyName.ToLower()
                        }
                    }
                );
            }).ToList();

            var binaries = projects.Where(x => x.OutputType == "Exe").Select(project => {
                return new BuildBinary(
                    id: project.AssemblyName,
                    baseDir: artifactsDir.Combine(project.AssemblyName),
                    projectPath: project.ProjectFile.FullPath,
                    packagePath: artifactsDir.CombineWithFilePath(string.Concat(project.AssemblyName, "-", version.SemVersion + ".zip"))
                    );
            });
            var tests = projects.Where(x => x.ProjectName.EndsWith("UnitTests")).Select(project => {
                return new BuildTest(
                    id: project.AssemblyName,
                    projectPath: project.ProjectFile.FullPath
                );
            });


            return new BuildPackages
            {
                Nuget = nugets,
                Images = dockerfiles,
                Binaries = binaries,
                Tests = tests
            };
        }

    }
    public interface IPackage
    {
        string Id { get; }
        FilePath ProjectPath { get; }
    }

    public class BuildPackage : IPackage
    {
        public string Id { get; private set; }
        public FilePath ProjectPath { get; private set; }
        public FilePath PackagePath { get; private set; }

        public BuildPackage(
            string id,
            FilePath projectPath,
            FilePath packagePath)
        {
            Id = id;
            ProjectPath = projectPath;
            PackagePath = packagePath;
        }
    }

    public class BuildDocker : IPackage
    {
        public string Id { get; private set; }
        public DirectoryPath BaseDir { get; private set; }
        public DockerImageBuildSettings Settings { get; private set; }
        public FilePath ProjectPath { get; private set; }
        public FilePath PackagePath { get; private set; }

        public BuildDocker(
            string id,
            DirectoryPath baseDir,
            DockerImageBuildSettings settings,
            FilePath projectPath,
            FilePath packagePath)
        {
            Id = id;
            BaseDir = baseDir;
            Settings = settings;
            ProjectPath = projectPath;
            PackagePath = packagePath;
        }
    }

    public class BuildBinary : IPackage
    {
        public string Id { get; private set; }
        public DirectoryPath BaseDir { get; private set; }
        public FilePath ProjectPath { get; private set; }
        public FilePath PackagePath { get; private set; }

        public BuildBinary(
            string id,
            FilePath projectPath,
            DirectoryPath baseDir,
            FilePath packagePath)
        {
            Id = id;
            BaseDir = baseDir;
            ProjectPath = projectPath;
            PackagePath = packagePath;
        }
    }

    public class BuildTest
    {
        public string Id { get; private set; }
        public FilePath ProjectPath { get; private set; }

        public BuildTest(
            string id,
            FilePath projectPath)
        {
            Id = id;
            ProjectPath = projectPath;
        }
    }

}
