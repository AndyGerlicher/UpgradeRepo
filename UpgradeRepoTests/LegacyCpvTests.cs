using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Gardener.Core;
using Gardener.Core.Json;
using Gardener.Core.MSBuild;
using Microsoft.Extensions.Logging;
using Moq;
using Shouldly;
using UpgradeRepo.LegacyCpv;

namespace UpgradeRepoTests
{
    public class LegacyCpvTests
    {
        [Theory]
        [InlineData("<Sdk Name=\"Microsoft.Build.CentralPackageVersions\" />")]
        [InlineData("<Sdk Name=\"Microsoft.Build.CentralPackageVersions\" Version=\"1.0-pre\"/>")]
        [InlineData("<Sdk Name=\"Microsoft.Build.CentralPackageVersions\"   Version=\"2.0.0\" />")]
        public void LegacyCpvRemoveFeatureTest(string sdkDeclaration)
        {
            string mockDbTargetsPath = @"c:\temp\Directory.Build.targets";
            string mockDbPropsPath = @"c:\temp\Directory.Build.props";

            var dbTargetsXmlFormat = @"<?xml version=""1.0"" encoding=""utf-8""?>
<!-- This targets file is included by Microsoft.Common.targets and is therefore included in each *proj file -->
<Project>
  {0}
  <!-- Comment -->
  <PropertyGroup Condition="" '$(TestProjectType)' == 'UnitTest' or '$(IsTestProject)' == 'true' "">
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>
</Project>";

            var expectedDbTargetsXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<!-- This targets file is included by Microsoft.Common.targets and is therefore included in each *proj file -->
<Project>
  <!-- Comment -->
  <PropertyGroup Condition="" '$(TestProjectType)' == 'UnitTest' or '$(IsTestProject)' == 'true' "">
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>
</Project>";

            string dbPropsXml = @"<Project>
  <PropertyGroup>
    <PlatformTarget>AnyCPU</PlatformTarget>

    <!-- NuProj projects do not support PackageReference out-of-the-box but we use them anyway, enable central package versions for them as well -->
    <EnableCentralPackageVersions Condition=""'$(MSBuildProjectExtension)' == '.nuproj'"">true</EnableCentralPackageVersions>
  </PropertyGroup>
</Project>";
            string expectedDbPropsXml = @"<Project>
  <PropertyGroup>
    <PlatformTarget>AnyCPU</PlatformTarget>

    <!-- NuProj projects do not support PackageReference out-of-the-box but we use them anyway, enable central package versions for them as well -->
  </PropertyGroup>
</Project>"; ;

            var mockFS = new Mock<IFileSystem>();

            mockFS.Setup(_ => _.ReadAllTextAsync(mockDbTargetsPath))
                .ReturnsAsync(string.Format(dbTargetsXmlFormat, sdkDeclaration));
            mockFS.Setup(_ => _.FileExistsAsync(mockDbTargetsPath)).ReturnsAsync(true);
            mockFS.Setup(_ => _.ReadAllTextAsync(mockDbPropsPath)).ReturnsAsync(dbPropsXml);
            mockFS.Setup(_ => _.FileExistsAsync(mockDbPropsPath)).ReturnsAsync(true);

            var p = new LegacyCpvPlugin(new LoggerFactory().CreateLogger<LegacyCpvPlugin>());
            var file = MSBuildFile.ReadAsync(mockFS.Object, mockDbTargetsPath).Result;
            var file2 = MSBuildFile.ReadAsync(mockFS.Object, mockDbPropsPath).Result;
            var result = p.DisableLegacyFeature(file, file2);

            mockFS.VerifyAll();
            result.ShouldBeTrue();
            file.Content.ShouldBe(expectedDbTargetsXml);
            file2.Content.ShouldBe(expectedDbPropsXml);
        }

        [Fact]
        public void RemoveGloblJsonSdkTest()
        {
            string mockFilePath = @"c:\temp\global.json";
            var globalJson = @"{
  ""sdk"": {
    ""version"": ""7.0.100""
  },
  ""msbuild-sdks"": {
    ""Microsoft.Build.CentralPackageVersions"": ""2.1.3"",
    ""Microsoft.Build.NoTargets"": ""3.3.0"",
    ""Microsoft.Build.Traversal"": ""3.0.54"",
    ""MSBuild.NpmRestore"": ""1.0.5"",
    ""Microsoft.Build.CentralPackageVersions"": ""2.1.3""
  }
}";
            var expectedGlobalJson = @"{
  ""sdk"": {
    ""version"": ""7.0.100""
  },
  ""msbuild-sdks"": {
    ""Microsoft.Build.NoTargets"": ""3.3.0"",
    ""Microsoft.Build.Traversal"": ""3.0.54"",
    ""MSBuild.NpmRestore"": ""1.0.5"",
  }
}";
            var mockFS = new Mock<IFileSystem>();
            mockFS.Setup(_ => _.ReadAllTextAsync(mockFilePath)).ReturnsAsync(globalJson);
            var p = new LegacyCpvPlugin(new LoggerFactory().CreateLogger<LegacyCpvPlugin>());
            var file = GlobalJsonFile.ReadAsync(mockFS.Object, mockFilePath).Result;
            var result = p.RemoveSdkFromGlobalJson(file);

            mockFS.VerifyAll();
            result.ShouldBeTrue();
            file.Content.ShouldBe(expectedGlobalJson);
        }

        [Fact]
        public void EnableFeatureTest()
        {
            string mockFilePath = @"c:\temp\doesnotexist\Directory.Build.props";
            string xml = @"<Project>
  <PropertyGroup>
    <!--
      Enlistment root is based off of wherever this file is.  Be sure not to set this property anywhere else.
    -->
    <EnlistmentRoot>$(MSBuildThisFileDirectory.TrimEnd('\\'))</EnlistmentRoot>

    <Configuration Condition="" '$(Configuration)' == '' "">Debug</Configuration>
    <Platform Condition="" '$(Platform)' == '' "">x64</Platform>
  </PropertyGroup>
  <PropertyGroup>
    <PlatformTarget>AnyCPU</PlatformTarget>
  </PropertyGroup>
</Project>";
            string expectedXml = @"<Project>
  <PropertyGroup>
    <!--
      Enlistment root is based off of wherever this file is.  Be sure not to set this property anywhere else.
    -->
    <EnlistmentRoot>$(MSBuildThisFileDirectory.TrimEnd('\\'))</EnlistmentRoot>

    <Configuration Condition="" '$(Configuration)' == '' "">Debug</Configuration>
    <Platform Condition="" '$(Platform)' == '' "">x64</Platform>

    <!-- Enable Central Package Management unless the project is using packages.config or is a project that does not support PackageReference -->
    <ManagePackageVersionsCentrally Condition=""'$(ManagePackageVersionsCentrally)' == ''
      And !Exists('$(MSBuildProjectDirectory)\packages.config')
      And '$(MSBuildProjectExtension)' != '.vcxproj'
      And '$(MSBuildProjectExtension)' != '.ccproj'
      And '$(MSBuildProjectExtension)' != '.nuproj'"">true</ManagePackageVersionsCentrally>
  </PropertyGroup>
  <PropertyGroup>
    <PlatformTarget>AnyCPU</PlatformTarget>
  </PropertyGroup>
</Project>";

            var mockFS = new Mock<IFileSystem>();

            mockFS.Setup(_ => _.ReadAllTextAsync(mockFilePath)).ReturnsAsync(xml);
            mockFS.Setup(_ => _.FileExistsAsync(mockFilePath)).ReturnsAsync(true);
            var p = new LegacyCpvPlugin(new LoggerFactory().CreateLogger<LegacyCpvPlugin>());
            var file = MSBuildFile.ReadAsync(mockFS.Object, mockFilePath).Result;
            p.EnableCpmFeature(file);

            file.Content.ShouldBe(expectedXml);
        }

        [Fact]
        public void FixPackageReferenceUpdateTest()
        {
            string mockPackagesPropsFile = @"c:\temp\doesnotexist.props";
            string xml = @"<Project>
  <PropertyGroup>
    <!-- These are used to keep packages which should upgrade together consistent -->
    <MSBuildPackagesVersion>17.5.0-preview-22555-01</MSBuildPackagesVersion>
    <NugetPackagesVersion>6.3.1</NugetPackagesVersion>
  </PropertyGroup>

  <ItemGroup Label=""Package Versions used by this repository"">
    <PackageReference Update=""Antlr"" Version=""3.5.0.2"" />
    <PackageReference Update=""Azure.Core"" Version=""1.25.0"" />
  </ItemGroup>
</Project>
";
            string expectedXml = @"<Project>
  <PropertyGroup>
    <!-- These are used to keep packages which should upgrade together consistent -->
    <MSBuildPackagesVersion>17.5.0-preview-22555-01</MSBuildPackagesVersion>
    <NugetPackagesVersion>6.3.1</NugetPackagesVersion>
  </PropertyGroup>

  <ItemGroup Label=""Package Versions used by this repository"">
    <PackageVersion Include=""Antlr"" Version=""3.5.0.2"" />
    <PackageVersion Include=""Azure.Core"" Version=""1.25.0"" />
  </ItemGroup>
</Project>
";

            var mockFS = new Mock<IFileSystem>();

            mockFS.Setup(_ => _.ReadAllTextAsync(mockPackagesPropsFile)).ReturnsAsync(xml);
            mockFS.Setup(_ => _.FileExistsAsync(mockPackagesPropsFile)).ReturnsAsync(true);
            var p = new LegacyCpvPlugin(new LoggerFactory().CreateLogger<LegacyCpvPlugin>());
            var file = MSBuildFile.ReadAsync(mockFS.Object, mockPackagesPropsFile).Result;

            p.FixPackageReferenceUpdate(file);
            file.Content.ShouldBe(expectedXml);
        }
    }
}
