using NuGet.Versioning;
using UpgradeRepo.Cpv;
using Shouldly;

namespace UpgradeRepoTests
{
    public class CpvTests
    {
        private readonly Func<Package, string> _defaultResolver = _ => string.Empty;

        [Fact]
        public void GetNameVersionTest()
        {
            string line =
                @"<PackageReference Include=""Microsoft.Azure.WebJobs.Extensions.ServiceBus"" Version=""5.0.0-beta.3""";

            var package = ProjectFileHelpers.GetPackageFromLine(line);

            package.Name.ShouldBe("Microsoft.Azure.WebJobs.Extensions.ServiceBus");
            package.Version.ToString().ShouldBe("5.0.0-beta.3");
        }

        [Fact]
        public void RemoveVersionTest()
        {
            string line =
                @"<PackageReference Include=""Microsoft.Azure.WebJobs.Extensions.ServiceBus"" Version=""5.0.0-beta.3""/>";

            var line2 = ProjectFileHelpers.UpdateVersion(line, _defaultResolver);

            line2.ShouldBe(@"<PackageReference Include=""Microsoft.Azure.WebJobs.Extensions.ServiceBus""/>");
        }

        [Fact]
        public void UpdatePackageReferenceTestLineTest()
        {
            using var testFile =
                TestProjectFile.CreateWithPackage("Microsoft.Azure.WebJobs.Extensions.ServiceBus", "5.0.0-beta.3");

            var vpvm = new CpvUpgradePlugin();
            vpvm.AddFile(testFile.FullPath);
            vpvm.ReadAllPackages();
            var packages = vpvm.GetPackages().ToList();
            
            packages.Count.ShouldBe(1);
            packages[0].Name.ShouldBe("Microsoft.Azure.WebJobs.Extensions.ServiceBus");
            packages[0].Version.ToString().ShouldBe("5.0.0-beta.3");
            
            vpvm.WriteAllUpdates();
            
            testFile.Contents.ShouldBe(@"<PackageReference Include=""Microsoft.Azure.WebJobs.Extensions.ServiceBus"" />");
        }

        [Fact]
        public void ProcessLineMultiplePackagesTest()
        {
            using var testFile =
                TestProjectFile.CreateWithMultiplePackages(new()
                {
                    Tuple.Create("Microsoft.Azure.WebJobs.Extensions.ServiceBus", "5.0.0-beta.3"),
                    Tuple.Create("Other", "3.5")
                });

            var vpvm = new CpvUpgradePlugin();
            vpvm.AddFile(testFile.FullPath);
            vpvm.ReadAllPackages();
            
            var packages = vpvm.GetPackages().ToList();
            packages.Count.ShouldBe(2);
            packages.ShouldContain(new Package("Microsoft.Azure.WebJobs.Extensions.ServiceBus", new NuGetVersion("5.0.0-beta.3")));
            packages.ShouldContain(new Package("Other", new NuGetVersion("3.5")));

            vpvm.WriteAllUpdates();
            testFile.Contents.ShouldBe(@"<PackageReference Include=""Microsoft.Azure.WebJobs.Extensions.ServiceBus"" />
<PackageReference Include=""Other"" />
");
        }

        [Fact]
        public void ProcessLineMultipleVersionsTest()
        {
            using var testFile =
                TestProjectFile.CreateWithMultiplePackages(new()
                {
                    Tuple.Create("Microsoft.Azure.WebJobs.Extensions.ServiceBus", "5.0.0-beta.3"),
                    Tuple.Create("Microsoft.Azure.WebJobs.Extensions.ServiceBus", "6.0"),
                });

            var vpvm = new CpvUpgradePlugin();
            vpvm.AddFile(testFile.FullPath);
            vpvm.ReadAllPackages();

            var packages = vpvm.GetPackages().ToList();
            packages.Count.ShouldBe(2);
            packages.ShouldContain(new Package("Microsoft.Azure.WebJobs.Extensions.ServiceBus", new NuGetVersion("5.0.0-beta.3")));
            packages.ShouldContain(new Package("Microsoft.Azure.WebJobs.Extensions.ServiceBus", new NuGetVersion("6.0")));

            vpvm.WriteAllUpdates();
            testFile.Contents.ShouldBe(@"<PackageReference Include=""Microsoft.Azure.WebJobs.Extensions.ServiceBus"" />
<PackageReference Include=""Microsoft.Azure.WebJobs.Extensions.ServiceBus"" VersionOverride=""6.0"" />
");
        }

        [Fact]
        public void GeneratePackagePropsTest()
        {
            using var testFile =
                TestProjectFile.CreateWithMultiplePackages(new()
                {
                    Tuple.Create("Microsoft.Azure.WebJobs.Extensions.ServiceBus", "5.0.0-beta.3"),
                    Tuple.Create("Microsoft.Azure.WebJobs.Extensions.ServiceBus", "6.0"),
                    Tuple.Create("Other", "3.0"),
                });

            var vpvm = new CpvUpgradePlugin();
            vpvm.AddFile(testFile.FullPath);
            vpvm.ReadAllPackages();
            vpvm.WriteAllUpdates();

            var packageProps = vpvm.GeneratePackageProps();

            packageProps.ShouldBe(@"<Project>
  <ItemGroup>
    <PackageVersion Include=""Microsoft.Azure.WebJobs.Extensions.ServiceBus"" Version=""5.0.0-beta.3"" />
    <PackageVersion Include=""Other"" Version=""3.0"" />
  </ItemGroup>
</Project>
");
        }
    }
}