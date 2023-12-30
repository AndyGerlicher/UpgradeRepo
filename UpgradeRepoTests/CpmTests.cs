using System.Xml;
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
        public async Task GetNameVersionTest()
        {
            string line = """<PackageReference Include="Microsoft.Azure.WebJobs.Extensions.ServiceBus" Version="5.0.0-beta.3" />""";
            var fs = TestProjectFile.GetRepo(line);
            var vpvm = new CpmUpgradePlugin(fs, new LoggerFactory().CreateLogger<CpmUpgradePlugin>());
            await vpvm.ApplyAsync(null, fs);

            var packages = vpvm.GetPackages().ToList();
            packages.Count.ShouldBe(1);
            packages[0].Name.ShouldBe("Microsoft.Azure.WebJobs.Extensions.ServiceBus");
            packages[0].VersionString.ShouldBe("5.0.0-beta.3");
        }

        [Fact]
        public async Task RemoveVersionTest()
        {
            string line = """<PackageReference Include="Microsoft.Azure.WebJobs.Extensions.ServiceBus" Version="5.0.0-beta.3"/>""";

            var fs = TestProjectFile.GetRepo(line);
            var vpvm = new CpmUpgradePlugin(fs, new LoggerFactory().CreateLogger<CpmUpgradePlugin>());
            await vpvm.ApplyAsync(null, fs);

            var line2 = fs.Files.First().Value;
            line2.ShouldBe("""<PackageReference Include="Microsoft.Azure.WebJobs.Extensions.ServiceBus"/>""");
        }

        [Fact]
        public async Task UpdatePackageReferenceTestLineTest()
        {
            var fs = TestProjectFile.CreateWithPackage("Microsoft.Azure.WebJobs.Extensions.ServiceBus", "5.0.0-beta.3");
            
            var vpvm = new CpmUpgradePlugin(fs, new LoggerFactory().CreateLogger<CpmUpgradePlugin>());
            await vpvm.ApplyAsync(null, fs);
            var packages = vpvm.GetPackages().ToList();
            
            packages.Count.ShouldBe(1);
            packages[0].Name.ShouldBe("Microsoft.Azure.WebJobs.Extensions.ServiceBus");
            packages[0].VersionString.ShouldBe("5.0.0-beta.3");
            
            await vpvm.WriteAllUpdatesAsync();

            fs.Files.First().Value.ShouldBe("""<PackageReference Include="Microsoft.Azure.WebJobs.Extensions.ServiceBus" />""");
        }

        [Fact]
        public async Task ProcessLineMultiplePackagesTest()
        {
            var fs = TestProjectFile.CreateWithMultiplePackages(new()
            {
                Tuple.Create("Microsoft.Azure.WebJobs.Extensions.ServiceBus", "5.0.0-beta.3"),
                Tuple.Create("Other", "3.5")
            });

            var vpvm = new CpmUpgradePlugin(fs, new LoggerFactory().CreateLogger<CpmUpgradePlugin>());
            await vpvm.ApplyAsync(null, fs);
            var packages = vpvm.GetPackages().ToList();

            packages.Count.ShouldBe(2);
            packages.ShouldContain(new Package("Microsoft.Azure.WebJobs.Extensions.ServiceBus", "5.0.0-beta.3"));
            packages.ShouldContain(new Package("Other", "3.5"));

            await vpvm.WriteAllUpdatesAsync();
            fs.Files.First().Value.ShouldBe("""
                                               <Project>
                                                   <PackageReference Include="Microsoft.Azure.WebJobs.Extensions.ServiceBus" />
                                                   <PackageReference Include="Other" />
                                               </Project>

                                               """);
        }

        [Fact]
        public async Task ProcessLineMultipleVersionsTest()
        {
            var fs =
                TestProjectFile.CreateWithMultipleFilesSamePackage(
                    "Microsoft.Azure.WebJobs.Extensions.ServiceBus",
                    ["5.0.0-beta.3", "6.0"]);

            var vpvm = new CpmUpgradePlugin(fs, new LoggerFactory().CreateLogger<CpmUpgradePlugin>());
            await vpvm.ApplyAsync(null, fs);
            var packages = vpvm.GetPackages().ToList();

            packages.Count.ShouldBe(2);
            packages.ShouldContain(new Package("Microsoft.Azure.WebJobs.Extensions.ServiceBus", "5.0.0-beta.3"));
            packages.ShouldContain(new Package("Microsoft.Azure.WebJobs.Extensions.ServiceBus", "6.0"));

            fs.Files["projfile0.csproj"].ShouldBe("""<PackageReference Include="Microsoft.Azure.WebJobs.Extensions.ServiceBus" VersionOverride="5.0.0-beta.3" />""");
            fs.Files["projfile1.csproj"].ShouldBe("""<PackageReference Include="Microsoft.Azure.WebJobs.Extensions.ServiceBus" />""");
        }

        [Fact]
        public async Task GeneratePackagePropsTest()
        {
            var fs =
                TestProjectFile.CreateWithMultiplePackages(new()
                {
                    Tuple.Create("Microsoft.Azure.WebJobs.Extensions.ServiceBus", "5.0.0-beta.3"),
                    Tuple.Create("Microsoft.Azure.WebJobs.Extensions.ServiceBus", "6.0"),
                    Tuple.Create("Other", "3.0"),
                });

            var vpvm = new CpmUpgradePlugin(fs, new LoggerFactory().CreateLogger<CpmUpgradePlugin>());
            await vpvm.ApplyAsync(null, fs);
            var packages = vpvm.GetPackages().ToList();

            fs.Files[CpmUpgradePlugin.DirectoryPackagesProps].ShouldBe("""
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
            var fs =
                TestProjectFile.CreateWithPackage(
                    "Microsoft.Azure.WebJobs.Extensions.ServiceBus",
                    "5.0.0-beta.3",
                    "GeneratePathProperty=\"true\" PrivateAsset=\"All\" ");

            var vpvm = new CpmUpgradePlugin(fs, new LoggerFactory().CreateLogger<CpmUpgradePlugin>());
            await vpvm.ApplyAsync(null, fs);
            var packages = vpvm.GetPackages().ToList();

            packages.Count.ShouldBe(1);
            packages[0].Name.ShouldBe("Microsoft.Azure.WebJobs.Extensions.ServiceBus");
            packages[0].VersionString.ShouldBe("5.0.0-beta.3");

            await vpvm.WriteAllUpdatesAsync();

            fs.Files.First().Value.ShouldBe("""<PackageReference Include="Microsoft.Azure.WebJobs.Extensions.ServiceBus" GeneratePathProperty="true" PrivateAsset="All" />""");
        }

        [Fact]
        public async Task CpmHandlesStarVersion()
        {
            var fs =
                TestProjectFile.CreateWithMultiplePackages(new()
                {
                    Tuple.Create("Microsoft.Azure.WebJobs.Extensions.ServiceBus", "5.0.0-beta.3"),
                    Tuple.Create("Microsoft.Azure.WebJobs.Extensions.ServiceBus", "6.*"),
                    Tuple.Create("Other", "3.0"),
                });

            var vpvm = new CpmUpgradePlugin(fs, new LoggerFactory().CreateLogger<CpmUpgradePlugin>());
            await vpvm.ApplyAsync(null, fs);
            var packages = vpvm.GetPackages().ToList();

            fs.Files[CpmUpgradePlugin.DirectoryPackagesProps].ShouldBe("""
                                                                       <Project>
                                                                         <ItemGroup>
                                                                           <PackageVersion Include="Microsoft.Azure.WebJobs.Extensions.ServiceBus" Version="6.*" />
                                                                           <PackageVersion Include="Other" Version="3.0" />
                                                                         </ItemGroup>
                                                                       </Project>

                                                                       """);
        }

        [Fact]
        public async Task CpmHandlesRangeVersion()
        {
            var fs =
                TestProjectFile.CreateWithMultiplePackages(new()
                {
                    Tuple.Create("Microsoft.Azure.WebJobs.Extensions.ServiceBus", "5.0.0-beta.3"),
                    Tuple.Create("Microsoft.Azure.WebJobs.Extensions.ServiceBus", "[6.*-)"),
                    Tuple.Create("Other", "3.0"),
                });

            var vpvm = new CpmUpgradePlugin(fs, new LoggerFactory().CreateLogger<CpmUpgradePlugin>());
            await vpvm.ApplyAsync(null, fs);
            var packages = vpvm.GetPackages().ToList();

            fs.Files[CpmUpgradePlugin.DirectoryPackagesProps].ShouldBe("""
                                                                       <Project>
                                                                         <ItemGroup>
                                                                           <PackageVersion Include="Microsoft.Azure.WebJobs.Extensions.ServiceBus" Version="[6.*-)" />
                                                                           <PackageVersion Include="Other" Version="3.0" />
                                                                         </ItemGroup>
                                                                       </Project>

                                                                       """);
        }

        [Fact]
        public async Task CpmMaxConsidersVersionType1()
        {
            var fs =
                TestProjectFile.CreateWithMultiplePackages(new()
                {
                    Tuple.Create("Microsoft.Azure.WebJobs.Extensions.ServiceBus", "5.0.0-beta.3"),
                    Tuple.Create("Microsoft.Azure.WebJobs.Extensions.ServiceBus", "[6.*-)"),
                    Tuple.Create("Microsoft.Azure.WebJobs.Extensions.ServiceBus", "$(MSBuild)"),
                    Tuple.Create("Other", "3.0"),
                });

            var vpvm = new CpmUpgradePlugin(fs, new LoggerFactory().CreateLogger<CpmUpgradePlugin>());
            await vpvm.ApplyAsync(null, fs);
            var packages = vpvm.GetPackages().ToList();

            fs.Files[CpmUpgradePlugin.DirectoryPackagesProps].ShouldBe("""
                                                                       <Project>
                                                                         <ItemGroup>
                                                                           <PackageVersion Include="Microsoft.Azure.WebJobs.Extensions.ServiceBus" Version="$(MSBuild)" />
                                                                           <PackageVersion Include="Other" Version="3.0" />
                                                                         </ItemGroup>
                                                                       </Project>

                                                                       """);
        }

        [Fact]
        public async Task CpmMaxConsidersVersionType2()
        {
            var fs =
                TestProjectFile.CreateWithMultiplePackages(new()
                {
                    Tuple.Create("Microsoft.Azure.WebJobs.Extensions.ServiceBus", "5.0.0-beta.3"),
                    Tuple.Create("Microsoft.Azure.WebJobs.Extensions.ServiceBus", "[6.*-)"),
                    Tuple.Create("Other", "3.0"),
                });

            var vpvm = new CpmUpgradePlugin(fs, new LoggerFactory().CreateLogger<CpmUpgradePlugin>());
            await vpvm.ApplyAsync(null, fs);
            var packages = vpvm.GetPackages().ToList();

            fs.Files[CpmUpgradePlugin.DirectoryPackagesProps].ShouldBe("""
                                                                       <Project>
                                                                         <ItemGroup>
                                                                           <PackageVersion Include="Microsoft.Azure.WebJobs.Extensions.ServiceBus" Version="[6.*-)" />
                                                                           <PackageVersion Include="Other" Version="3.0" />
                                                                         </ItemGroup>
                                                                       </Project>

                                                                       """);
        }


        [Fact]
        public async Task CpmHanlesMSBuildProperties()
        {
            var fs =
                TestProjectFile.CreateWithMultiplePackages(new()
                {
                    Tuple.Create("Microsoft.Azure.WebJobs.Extensions.ServiceBus", "5.0.0-beta.3"),
                    Tuple.Create("Microsoft.Azure.WebJobs.Extensions.ServiceBus", "$(PackageVersion)"),
                    Tuple.Create("Other", "3.0"),
                });

            var vpvm = new CpmUpgradePlugin(fs, new LoggerFactory().CreateLogger<CpmUpgradePlugin>());
            await vpvm.ApplyAsync(null, fs);
            var packages = vpvm.GetPackages().ToList();

            fs.Files[CpmUpgradePlugin.DirectoryPackagesProps].ShouldBe("""
                                                                       <Project>
                                                                         <ItemGroup>
                                                                           <PackageVersion Include="Microsoft.Azure.WebJobs.Extensions.ServiceBus" Version="$(PackageVersion)" />
                                                                           <PackageVersion Include="Other" Version="3.0" />
                                                                         </ItemGroup>
                                                                       </Project>

                                                                       """);
        }

        [Fact]
        public async Task PackageVersionsSpansMultipleLinesTest()
        {
            var contents = """
                           <PackageReference Include="Microsoft.Azure.WebJobs.Extensions.ServiceBus">
                               <Version>5.0.0-beta.3</Version>
                               <IncludeAssets>all</IncludeAssets>
                           </PackageReference>
                           """;

            var fs = TestProjectFile.CreateProjectFile(contents);

            var vpvm = new CpmUpgradePlugin(fs, new LoggerFactory().CreateLogger<CpmUpgradePlugin>());
            await vpvm.ApplyAsync(null, fs);
            var packages = vpvm.GetPackages().ToList();

            vpvm.GetPackages().Count().ShouldBe(1);

            fs.Files.First().Value.ShouldBe("""
                                            <PackageReference Include="Microsoft.Azure.WebJobs.Extensions.ServiceBus">
                                                <IncludeAssets>all</IncludeAssets>
                                            </PackageReference>
                                            """);

            fs.Files[CpmUpgradePlugin.DirectoryPackagesProps].ShouldBe("""
                                                                       <Project>
                                                                         <ItemGroup>
                                                                           <PackageVersion Include="Microsoft.Azure.WebJobs.Extensions.ServiceBus" Version="5.0.0-beta.3" />
                                                                         </ItemGroup>
                                                                       </Project>

                                                                       """);
        }

        [Fact]
        public async Task PackageVersionsSpansMultipleLinesWithVersionOverrideTest()
        {
            var contents =
                """
                <PackageReference Include="Microsoft.Azure.WebJobs.Extensions.ServiceBus">
                    <Version>5.0.0-beta.3</Version>
                    <IncludeAssets>all</IncludeAssets>
                </PackageReference>
                """;

            var contents2 =
                """
                <PackageReference Include="Microsoft.Azure.WebJobs.Extensions.ServiceBus" >
                    <Version>6.0</Version>
                    <IncludeAssets>all</IncludeAssets>
                </PackageReference>
                """;

            var fs = TestProjectFile.GetRepoMultipleFiles([contents, contents2]);

            var vpvm = new CpmUpgradePlugin(fs, new LoggerFactory().CreateLogger<CpmUpgradePlugin>());
            await vpvm.ApplyAsync(null, fs);
            var packages = vpvm.GetPackages().ToList();
            vpvm.GetPackages().Count().ShouldBe(2);

            fs.Files["projfile0.csproj"].ShouldBe("""
                                                  <PackageReference Include="Microsoft.Azure.WebJobs.Extensions.ServiceBus">
                                                      <VersionOverride>5.0.0-beta.3</VersionOverride>
                                                      <IncludeAssets>all</IncludeAssets>
                                                  </PackageReference>
                                                  """);

            fs.Files["projfile1.csproj"].ShouldBe("""
                                                  <PackageReference Include="Microsoft.Azure.WebJobs.Extensions.ServiceBus" >
                                                      <IncludeAssets>all</IncludeAssets>
                                                  </PackageReference>
                                                  """);

            

            fs.Files[CpmUpgradePlugin.DirectoryPackagesProps].ShouldBe("""
                                                                       <Project>
                                                                         <ItemGroup>
                                                                           <PackageVersion Include="Microsoft.Azure.WebJobs.Extensions.ServiceBus" Version="6.0" />
                                                                         </ItemGroup>
                                                                       </Project>

                                                                       """);
        }

        [Fact]
        public async Task PackageVersionsSpansMultipleLinesVersionAsAttributeTest()
        {
            var contents = """
                           <PackageReference Include="Microsoft.Azure.WebJobs.Extensions.ServiceBus" Version="5.0.0-beta.3">
                               <IncludeAssets>all</IncludeAssets>
                           </PackageReference>
                           """;

            var fs = TestProjectFile.CreateProjectFile(contents);

            var vpvm = new CpmUpgradePlugin(fs, new LoggerFactory().CreateLogger<CpmUpgradePlugin>());
            await vpvm.ApplyAsync(null, fs);
            var packages = vpvm.GetPackages().ToList();

            vpvm.GetPackages().Count().ShouldBe(1);

            fs.Files.First().Value.ShouldBe("""
                                            <PackageReference Include="Microsoft.Azure.WebJobs.Extensions.ServiceBus">
                                                <IncludeAssets>all</IncludeAssets>
                                            </PackageReference>
                                            """);

            fs.Files[CpmUpgradePlugin.DirectoryPackagesProps].ShouldBe("""
                                                                       <Project>
                                                                         <ItemGroup>
                                                                           <PackageVersion Include="Microsoft.Azure.WebJobs.Extensions.ServiceBus" Version="5.0.0-beta.3" />
                                                                         </ItemGroup>
                                                                       </Project>

                                                                       """);
        }

        [Fact]
        public async Task DuplicatePackageReferenesRemovedTest()
        {
            var fs = TestProjectFile.CreateWithMultiplePackages([
                Tuple.Create("Microsoft.Azure.WebJobs.Extensions.ServiceBus", "5.0.0-beta.3"),
                Tuple.Create("Microsoft.Azure.WebJobs.Extensions.ServiceBus", "5.0.0-beta.3"),
            ]);

            var vpvm = new CpmUpgradePlugin(fs, new LoggerFactory().CreateLogger<CpmUpgradePlugin>());
            await vpvm.ApplyAsync(null, fs);
            var packages = vpvm.GetPackages().ToList();

            fs.Files.First().Value.ShouldBe("""
                                            <Project>
                                                <PackageReference Include="Microsoft.Azure.WebJobs.Extensions.ServiceBus" />
                                            </Project>
                                            
                                            """);

            fs.Files[CpmUpgradePlugin.DirectoryPackagesProps].ShouldBe("""
                                                                       <Project>
                                                                         <ItemGroup>
                                                                           <PackageVersion Include="Microsoft.Azure.WebJobs.Extensions.ServiceBus" Version="5.0.0-beta.3" />
                                                                         </ItemGroup>
                                                                       </Project>
                                                                       
                                                                       """);
        }

        [Fact]
        public async Task ThrowsWhenInvalidXml()
        {
            var contents = """
                           <PackageReference Include="Microsoft.Azure.WebJobs.Extensions.ServiceBus" Version="5.0.0-beta.3" />
                           <badxml
                           """;

            var fs = TestProjectFile.CreateProjectFile(contents);

            var vpvm = new CpmUpgradePlugin(fs, new LoggerFactory().CreateLogger<CpmUpgradePlugin>());
            await vpvm.ApplyAsync(null!, fs);
            var packages = vpvm.GetPackages().ToList();

            packages.Count.ShouldBe(0);
        }

        [Fact]
        public async Task RemoveUnnecessaryPackageReferenceOpenCloseTags()
        {
            var contents =
                """
                <PackageReference Include="Microsoft.Azure.WebJobs.Extensions.ServiceBus">
                    <Version>5.0.0-beta.3</Version>
                </PackageReference>
                """;

            var contents2 =
                """
                <PackageReference Include="Microsoft.Azure.WebJobs.Extensions.ServiceBus" >
                    <Version>6.0</Version>
                </PackageReference>
                """;

            var fs = TestProjectFile.GetRepoMultipleFiles([contents, contents2]);

            var vpvm = new CpmUpgradePlugin(fs, new LoggerFactory().CreateLogger<CpmUpgradePlugin>());
            await vpvm.ApplyAsync(null, fs);
            var packages = vpvm.GetPackages().ToList();
            vpvm.GetPackages().Count().ShouldBe(2);

            fs.Files["projfile0.csproj"].ShouldBe("""
                                                  <PackageReference Include="Microsoft.Azure.WebJobs.Extensions.ServiceBus">
                                                      <VersionOverride>5.0.0-beta.3</VersionOverride>
                                                  </PackageReference>
                                                  """);

            fs.Files["projfile1.csproj"].ShouldBe("""
                                                  <PackageReference Include="Microsoft.Azure.WebJobs.Extensions.ServiceBus" />
                                                  """);



            fs.Files[CpmUpgradePlugin.DirectoryPackagesProps].ShouldBe("""
                                                                       <Project>
                                                                         <ItemGroup>
                                                                           <PackageVersion Include="Microsoft.Azure.WebJobs.Extensions.ServiceBus" Version="6.0" />
                                                                         </ItemGroup>
                                                                       </Project>

                                                                       """);
        }

        [Fact]
        public async Task EnsureEofNewLineBehavior()
        {
            var contents0 =
                """
                <PackageReference Include="Microsoft.Azure.WebJobs.Extensions.ServiceBus">
                    <Version>5.0.0-beta.3</Version>
                </PackageReference>
                
                
                """;

            var contents1 =
                """
                <PackageReference Include="Microsoft.Azure.WebJobs.Extensions.ServiceBus" >
                    <Version>6.0</Version>
                </PackageReference>
                
                """;


            var expected0 = """
                            <PackageReference Include="Microsoft.Azure.WebJobs.Extensions.ServiceBus">
                                <VersionOverride>5.0.0-beta.3</VersionOverride>
                            </PackageReference>


                            """;

            var expected1 = """
                            <PackageReference Include="Microsoft.Azure.WebJobs.Extensions.ServiceBus" />

                            """;

            contents0.ReplaceLineEndings("\r");
            contents1.ReplaceLineEndings("\r\n");

            expected0.ReplaceLineEndings("\r");
            expected1.ReplaceLineEndings("\r\n");

            var fs = TestProjectFile.GetRepoMultipleFiles([contents0, contents1]);

            var vpvm = new CpmUpgradePlugin(fs, new LoggerFactory().CreateLogger<CpmUpgradePlugin>());
            await vpvm.ApplyAsync(null, fs);
            var packages = vpvm.GetPackages().ToList();
            vpvm.GetPackages().Count().ShouldBe(2);

            fs.Files["projfile0.csproj"].ShouldBe(expected0);
            fs.Files["projfile1.csproj"].ShouldBe(expected1);

            fs.Files[CpmUpgradePlugin.DirectoryPackagesProps].ShouldBe("""
                                                                       <Project>
                                                                         <ItemGroup>
                                                                           <PackageVersion Include="Microsoft.Azure.WebJobs.Extensions.ServiceBus" Version="6.0" />
                                                                         </ItemGroup>
                                                                       </Project>

                                                                       """);
        }
    }
}