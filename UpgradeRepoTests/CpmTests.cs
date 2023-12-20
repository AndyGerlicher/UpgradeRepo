using Microsoft.Extensions.Logging;
using Shouldly;
using UpgradeRepo;
using UpgradeRepo.Cpm;

namespace UpgradeRepoTests
{
    public class CpmTests
    {
        private readonly Func<Package, string> _defaultResolver = _ => string.Empty;

        [Fact]
        public void GetNameVersionTest()
        {
            string line =
                """
                <PackageReference Include="Microsoft.Azure.WebJobs.Extensions.ServiceBus" Version="5.0.0-beta.3"
                """;

            var package = ProjectFileHelpers.GetPackageFromLine(line);

            package.Name.ShouldBe("Microsoft.Azure.WebJobs.Extensions.ServiceBus");
            package.VersionString.ShouldBe("5.0.0-beta.3");
        }

        [Fact]
        public void RemoveVersionTest()
        {
            string line = """<PackageReference Include="Microsoft.Azure.WebJobs.Extensions.ServiceBus" Version="5.0.0-beta.3"/>""";

            var line2 = ProjectFileHelpers.UpdateVersion(line, _defaultResolver);

            line2.ShouldBe("""<PackageReference Include="Microsoft.Azure.WebJobs.Extensions.ServiceBus"/>""");
        }

        [Fact]
        public async Task UpdatePackageReferenceTestLineTest()
        {
            using var testFile =
                TestProjectFile.CreateWithPackage("Microsoft.Azure.WebJobs.Extensions.ServiceBus", "5.0.0-beta.3");

            var vpvm = new CpmUpgradePlugin(new FileSystem(), new LoggerFactory().CreateLogger<CpmUpgradePlugin>());
            vpvm.AddFile(testFile.FullPath);
            await vpvm.ReadAllPackagesAsync();
            var packages = vpvm.GetPackages().ToList();
            
            packages.Count.ShouldBe(1);
            packages[0].Name.ShouldBe("Microsoft.Azure.WebJobs.Extensions.ServiceBus");
            packages[0].VersionString.ShouldBe("5.0.0-beta.3");
            
            await vpvm.WriteAllUpdatesAsync();
            
            testFile.Contents.ShouldBe("""<PackageReference Include="Microsoft.Azure.WebJobs.Extensions.ServiceBus" />""");
        }

        [Fact]
        public async Task ProcessLineMultiplePackagesTest()
        {
            using var testFile =
                TestProjectFile.CreateWithMultiplePackages(new()
                {
                    Tuple.Create("Microsoft.Azure.WebJobs.Extensions.ServiceBus", "5.0.0-beta.3"),
                    Tuple.Create("Other", "3.5")
                });

            var vpvm = new CpmUpgradePlugin(new FileSystem(), new LoggerFactory().CreateLogger<CpmUpgradePlugin>());
            vpvm.AddFile(testFile.FullPath);
            await vpvm.ReadAllPackagesAsync();
            
            var packages = vpvm.GetPackages().ToList();
            packages.Count.ShouldBe(2);
            packages.ShouldContain(new Package("Microsoft.Azure.WebJobs.Extensions.ServiceBus", "5.0.0-beta.3"));
            packages.ShouldContain(new Package("Other", "3.5"));

            await vpvm.WriteAllUpdatesAsync();
            testFile.Contents.ShouldBe("""
                                       <PackageReference Include="Microsoft.Azure.WebJobs.Extensions.ServiceBus" />
                                       <PackageReference Include="Other" />
                                       
                                       """);
        }

        [Fact]
        public async Task ProcessLineMultipleVersionsTest()
        {
            using var testFile =
                TestProjectFile.CreateWithMultiplePackages(new()
                {
                    Tuple.Create("Microsoft.Azure.WebJobs.Extensions.ServiceBus", "5.0.0-beta.3"),
                    Tuple.Create("Microsoft.Azure.WebJobs.Extensions.ServiceBus", "6.0"),
                });

            var vpvm = new CpmUpgradePlugin(new FileSystem(), new LoggerFactory().CreateLogger<CpmUpgradePlugin>());
            vpvm.AddFile(testFile.FullPath);
            await vpvm.ReadAllPackagesAsync();

            var packages = vpvm.GetPackages().ToList();
            packages.Count.ShouldBe(2);
            packages.ShouldContain(new Package("Microsoft.Azure.WebJobs.Extensions.ServiceBus", "5.0.0-beta.3"));
            packages.ShouldContain(new Package("Microsoft.Azure.WebJobs.Extensions.ServiceBus", "6.0"));

            await vpvm.WriteAllUpdatesAsync();
            testFile.Contents.ShouldBe("""
                                       <PackageReference Include="Microsoft.Azure.WebJobs.Extensions.ServiceBus" VersionOverride="5.0.0-beta.3" />
                                       <PackageReference Include="Microsoft.Azure.WebJobs.Extensions.ServiceBus" />

                                       """);
        }

        [Fact]
        public async Task GeneratePackagePropsTest()
        {
            using var testFile =
                TestProjectFile.CreateWithMultiplePackages(new()
                {
                    Tuple.Create("Microsoft.Azure.WebJobs.Extensions.ServiceBus", "5.0.0-beta.3"),
                    Tuple.Create("Microsoft.Azure.WebJobs.Extensions.ServiceBus", "6.0"),
                    Tuple.Create("Other", "3.0"),
                });

            var vpvm = new CpmUpgradePlugin(new FileSystem(), new LoggerFactory().CreateLogger<CpmUpgradePlugin>());
            vpvm.AddFile(testFile.FullPath);
            await vpvm.ReadAllPackagesAsync();
            await vpvm.WriteAllUpdatesAsync();

            var packageProps = vpvm.GeneratePackageProps();

            packageProps.ShouldBe("""
                                  <Project>
                                    <ItemGroup>
                                      <PackageVersion Include="Microsoft.Azure.WebJobs.Extensions.ServiceBus" Version="6.0" />
                                      <PackageVersion Include="Other" Version="3.0" />
                                    </ItemGroup>
                                  </Project>

                                  """);
        }

        [Fact]
        public async Task AdditionalPackageRefMetadataPreserved()
        {
            using var testFile =
                TestProjectFile.CreateWithPackage(
                    "Microsoft.Azure.WebJobs.Extensions.ServiceBus",
                    "5.0.0-beta.3",
                    "GeneratePathProperty=\"true\" PrivateAsset=\"All\" ");

            var vpvm = new CpmUpgradePlugin(new FileSystem(), new LoggerFactory().CreateLogger<CpmUpgradePlugin>());
            vpvm.AddFile(testFile.FullPath);
            await vpvm.ReadAllPackagesAsync();
            var packages = vpvm.GetPackages().ToList();

            packages.Count.ShouldBe(1);
            packages[0].Name.ShouldBe("Microsoft.Azure.WebJobs.Extensions.ServiceBus");
            packages[0].VersionString.ShouldBe("5.0.0-beta.3");

            await vpvm.WriteAllUpdatesAsync();

            testFile.Contents.ShouldBe("""<PackageReference Include="Microsoft.Azure.WebJobs.Extensions.ServiceBus" GeneratePathProperty="true" PrivateAsset="All" />""");
        }

        [Fact]
        public async Task CpmHandlesStarVersion()
        {
            using var testFile =
                TestProjectFile.CreateWithMultiplePackages(new()
                {
                    Tuple.Create("Microsoft.Azure.WebJobs.Extensions.ServiceBus", "5.0.0-beta.3"),
                    Tuple.Create("Microsoft.Azure.WebJobs.Extensions.ServiceBus", "6.*"),
                    Tuple.Create("Other", "3.0"),
                });

            var vpvm = new CpmUpgradePlugin(new FileSystem(), new LoggerFactory().CreateLogger<CpmUpgradePlugin>());
            vpvm.AddFile(testFile.FullPath);
            await vpvm.ReadAllPackagesAsync();
            await vpvm.WriteAllUpdatesAsync();

            var packageProps = vpvm.GeneratePackageProps();

            packageProps.ShouldBe("""
                                  <Project>
                                    <ItemGroup>
                                      <PackageVersion Include="Microsoft.Azure.WebJobs.Extensions.ServiceBus" Version="6.*" />
                                      <PackageVersion Include="Other" Version="3.0" />
                                    </ItemGroup>
                                  </Project>

                                  """);
        }

        [Fact]
        public async Task CpmHanlesMSBuildProperties()
        {
            using var testFile =
                TestProjectFile.CreateWithMultiplePackages(new()
                {
                    Tuple.Create("Microsoft.Azure.WebJobs.Extensions.ServiceBus", "5.0.0-beta.3"),
                    Tuple.Create("Microsoft.Azure.WebJobs.Extensions.ServiceBus", "$(PackageVersion)"),
                    Tuple.Create("Other", "3.0"),
                });

            var vpvm = new CpmUpgradePlugin(new FileSystem(), new LoggerFactory().CreateLogger<CpmUpgradePlugin>());
            vpvm.AddFile(testFile.FullPath);
            await vpvm.ReadAllPackagesAsync();
            await vpvm.WriteAllUpdatesAsync();

            var packageProps = vpvm.GeneratePackageProps();

            packageProps.ShouldBe("""
                                  <Project>
                                    <ItemGroup>
                                      <PackageVersion Include="Microsoft.Azure.WebJobs.Extensions.ServiceBus" Version="$(PackageVersion)" />
                                      <PackageVersion Include="Other" Version="3.0" />
                                    </ItemGroup>
                                  </Project>

                                  """);
        }

        [Fact]
        public async Task  CpmDoesNotAllowWildcardAndMSBuildProperty()
        {
            using var testFile =
                TestProjectFile.CreateWithMultiplePackages(new()
                {
                    Tuple.Create("Microsoft.Azure.WebJobs.Extensions.ServiceBus", "5.*"),
                    Tuple.Create("Microsoft.Azure.WebJobs.Extensions.ServiceBus", "$(PackageVersion)"),
                    Tuple.Create("Other", "3.0"),
                });

            var vpvm = new CpmUpgradePlugin(new FileSystem(), new LoggerFactory().CreateLogger<CpmUpgradePlugin>());
            vpvm.AddFile(testFile.FullPath);
            await vpvm.ReadAllPackagesAsync();
            await vpvm.WriteAllUpdatesAsync();

            Should.Throw<InvalidOperationException>(vpvm.ShowConflicts);
        }
    }
}