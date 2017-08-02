public class BuildPackages
{
    public IEnumerable<BuildPackage> Nuget { get; private set; }

    public static BuildPackages GetPackages(
		DirectoryPath artifactsBinDir,
        DirectoryPath nugetRootPath,
        string semVersion,
        bool isOnWindows)
    {

		var SetNuGetNuspecCommonProperties = new Action<NuGetPackSettings> ((nuspec) => {
            nuspec.Version = semVersion;
            nuspec.Authors = new [] { "Charles Solar" };
            nuspec.Owners = new [] { "Charles Solar" };
            nuspec.LicenseUrl = new Uri("http://opensource.org/licenses/MIT");
            nuspec.ProjectUrl = new Uri("https://github.com/volak/Aggregates.NET");
            nuspec.RequireLicenseAcceptance = false;
            nuspec.Symbols = false;
            nuspec.NoPackageAnalysis = true;
            nuspec.Description = "A framework to help developers integrate the excelent NServicebus and GetEventStore libraries together and provide basic DDD classes.";
            nuspec.Copyright = "Copyright 2017";
            nuspec.Tags = new [] { "CQRS", "NServiceBus", "GetEventStore", "eventstore", "event store", "aggregate", "ddd", "repository", "unit of work", "uow" };
        });

		var nuspecs = new[]
		{
			new NuGetPackSettings()
			{
				Id = "Aggregates.NET",
				Dependencies = new []
				{
					new NuSpecDependency() { Id = "NServiceBus", Version = "[6.1.2,7)" },
					new NuSpecDependency() { Id = "NServiceBus.Callbacks", Version = "[2,3)" },
					new NuSpecDependency() { Id = "Metrics.NET", Version = "[0.4,1)" },
					new NuSpecDependency() { Id = "Newtonsoft.Json", Version = "[9,)" }
				},
				Files = new []
				{
					new NuSpecContent { Source = "Aggregates.NET.dll", Target = "lib/net46" },
					new NuSpecContent { Source = "Aggregates.NET.pdb", Target = "lib/net46" },
				},
				BasePath = artifactsBinDir.Combine("Aggregates.NET"),
				OutputDirectory = nugetRootPath
			},
			new NuGetPackSettings()
			{
				Id = "Aggregates.NET.Consumer",
				Dependencies = new []
				{
					new NuSpecDependency() { Id = "NServiceBus", Version = "[6.1.2,7)" },
					new NuSpecDependency() { Id = "Metrics.NET", Version = "[0.4,1)" },
					new NuSpecDependency() { Id = "Newtonsoft.Json", Version = "[9,)" },
					new NuSpecDependency() { Id = "Aggregates.NET", Version = "[0.10,0.11)" }
				},
				Files = new []
				{
					new NuSpecContent { Source = "Aggregates.NET.Consumer.dll", Target = "lib/net46" },
					new NuSpecContent { Source = "Aggregates.NET.Consumer.pdb", Target = "lib/net46" },
				},
				BasePath = artifactsBinDir.Combine("Aggregates.NET.Consumer"),
				OutputDirectory = nugetRootPath
			},
			new NuGetPackSettings()
			{
				Id = "Aggregates.NET.Domain",
				Dependencies = new []
				{
					new NuSpecDependency() { Id = "NServiceBus", Version = "[6.1.2,7)" },
					new NuSpecDependency() { Id = "Metrics.NET", Version = "[0.4,1)" },
					new NuSpecDependency() { Id = "Newtonsoft.Json", Version = "[9,)" },
					new NuSpecDependency() { Id = "Aggregates.NET", Version = "[0.10,0.11)" }
				},
				Files = new []
				{
					new NuSpecContent { Source = "Aggregates.NET.Domain.dll", Target = "lib/net46" },
					new NuSpecContent { Source = "Aggregates.NET.Domain.pdb", Target = "lib/net46" },
				},
				BasePath = artifactsBinDir.Combine("Aggregates.NET.Domain"),
				OutputDirectory = nugetRootPath
			},
			new NuGetPackSettings()
			{
				Id = "Aggregates.NET.GetEventStore",
				Dependencies = new []
				{
					new NuSpecDependency() { Id = "NServiceBus", Version = "[6.1.2,7)" },
					new NuSpecDependency() { Id = "Metrics.NET", Version = "[0.4,1)" },
					new NuSpecDependency() { Id = "Newtonsoft.Json", Version = "[9,)" },
					new NuSpecDependency() { Id = "Aggregates.NET", Version = "[0.10,0.11)" },
					new NuSpecDependency() { Id = "EventStore.Client", Version = "[4,5)" }
				},
				Files = new []
				{
					new NuSpecContent { Source = "Aggregates.NET.GetEventStore.dll", Target = "lib/net46" },
					new NuSpecContent { Source = "Aggregates.NET.GetEventStore.pdb", Target = "lib/net46" },
				},
				BasePath = artifactsBinDir.Combine("Aggregates.NET.GetEventStore"),
				OutputDirectory = nugetRootPath
			},


		};

		nuspecs.ToList().ForEach(x => {

			SetNuGetNuspecCommonProperties(x);
			if(!isOnWindows)
				x.Files = x.Files.Where(f => !f.Source.EndsWith("pdb")).ToArray();
		});

		var packages = nuspecs.Select(x => new BuildPackage( id: x.Id, nuspec: x, packagePath: nugetRootPath.CombineWithFilePath(string.Concat(x.Id, ".", x.Version, ".nupkg"))));

        return new BuildPackages { 
			Nuget = packages
		};
    }

}

public class BuildPackage
{
    public string Id { get; private set; }
    public NuGetPackSettings Nuspec { get; private set; }
    public FilePath PackagePath { get; private set; }

    public BuildPackage(
        string id,
        NuGetPackSettings nuspec,
        FilePath packagePath)
    {
        Id = id;
        Nuspec = nuspec;
        PackagePath = packagePath;
    }
}