using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using Gardener.Core;
using Moq;
using UpgradeRepo.Cpm;

namespace UpgradeRepoTests
{
    internal class TestProjectFile : IDisposable
    {
        public string FullPath { get; }

        public string Contents => File.ReadAllText(FullPath);

        public TestProjectFile(string contents)
        {
            FullPath = Path.GetTempFileName();
            File.WriteAllText(FullPath, contents);
        }

        public void Dispose()
        {
            File.Delete(FullPath);
        }

        public static TestFileSystem CreateProjectFile(string contents)
        {
            return GetRepo(contents);
        }

        public static TestFileSystem CreateWithPackage(string name, string version, string additionalMetadata = "")
        {
            string contents = $"""<PackageReference Include="{name}" Version="{version}" {additionalMetadata}/>""";
            
            return GetRepo(contents);
        }
        public static TestFileSystem CreateWithMultipleFilesSamePackage(string packageName, List<string> versions)
        {
            var testFs = new TestFileSystem();
            for (int i = 0; i < versions.Count; i++)
            {
                var filePath = $"projfile{i}.csproj";

                testFs.WriteAllTextAsync(filePath,
                    $"""<PackageReference Include="{packageName}" Version="{versions[i]}" />""");
            }

            return testFs;
        }

        public static TestFileSystem CreateWithMultiplePackages(List<Tuple<string, string>> packages)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<Project>");
            foreach (var item in packages)
            {
                sb.AppendLine($"""    <PackageReference Include="{item.Item1}" Version="{item.Item2}" />""");
            }
            sb.AppendLine("</Project>");

            return GetRepo(sb.ToString());
        }

        public static TestFileSystem GetRepoMultipleFiles(List<string> projFileContents, string? dbPropsContents = null)
        {
            var testFs = new TestFileSystem();
            for (int i = 0; i < projFileContents.Count; i++)
            {
                var filePath = $"projfile{i}.csproj";

                testFs.WriteAllTextAsync(filePath, projFileContents[i]);
            }

            return testFs;

        }

        public static TestFileSystem GetRepo(string projFileContents, string? dbPropsContents = null)
        {
            var filePath = "projfile1.csproj";

            var testFs = new TestFileSystem();
            testFs.WriteAllTextAsync(filePath, projFileContents);

            if (!string.IsNullOrEmpty(dbPropsContents))
            {
                testFs.WriteAllTextAsync(CpmUpgradePlugin.DirectoryBuildProps, dbPropsContents);
            }

            return testFs;
        }
    }

    public class TestRepo
    {
        public IFileSystem FileSystem { get; }

        public Dictionary<string, string> Files { get; } = new();

        public TestRepo(string projFileContents, string dbPropsContents = null)
        {
            var filePath = "projfile1.csproj";
            var bytes = UTF8Encoding.UTF8.GetBytes(projFileContents);

            var mockFS = new Mock<IFileSystem>();

            if (string.IsNullOrEmpty(dbPropsContents))
            {
                mockFS.Setup(_ => _.FileExistsAsync(CpmUpgradePlugin.DirectoryBuildProps)).ReturnsAsync(true);

            }

            mockFS.Setup(_ => _.ReadAllTextAsync(CpmUpgradePlugin.DirectoryBuildProps)).ReturnsAsync(dbPropsContents);

            mockFS.Setup(_ => _.EnumerateFiles(new[] { "*.*proj", "*.targets", "*.props" })).ReturnsAsync(new[] { filePath });
            mockFS.Setup(_ => _.ReadAllTextAsync(filePath)).ReturnsAsync(projFileContents);
            mockFS.Setup(_ => _.ReadAllBytesAsync(filePath)).ReturnsAsync(bytes);

            mockFS.Setup(_ => _.WriteAllTextAsync(filePath, It.IsAny<string>())).Returns(Task.CompletedTask);
            mockFS.Setup(_ => _.WriteAllTextAsync(CpmUpgradePlugin.DirectoryBuildProps, It.IsAny<string>())).Returns(Task.CompletedTask);
            mockFS.Setup(_ => _.WriteAllTextAsync(CpmUpgradePlugin.DirectoryPackagesProps, It.IsAny<string>())).Returns(Task.CompletedTask);

            FileSystem = mockFS.Object;
        }
    }

    public class TestFileSystem : IFileSystem
    {
        public Dictionary<string, string> Files { get; } = new();
        public Task<bool> FileExistsAsync(string path)
        {
            return Task.FromResult(Files.ContainsKey(path));
        }

        public Task<string> ReadAllTextAsync(string path)
        {
            return Task.FromResult(Files[path]);
        }

        public Task<byte[]> ReadAllBytesAsync(string path)
        {
            return Task.FromResult(Encoding.UTF8.GetBytes(Files[path]));
        }

        public Task WriteAllTextAsync(string path, string content)
        {
            Files[path] = content;
            return Task.CompletedTask;
        }

        public Task WriteAllBytesAsync(string path, byte[] bytes)
        {
            Files[path] = Encoding.UTF8.GetString(bytes);
            return Task.CompletedTask;
        }

        public Task DeleteFileAsync(string path)
        {
            Files.Remove(path);
            return Task.CompletedTask;
        }

        public Task RenameFileAsync(string path, string newName)
        {
            throw new NotImplementedException();
        }

        public Task<IReadOnlyCollection<string>> EnumerateFiles(IReadOnlyCollection<string> searchPatterns)
        {
            var items = Files.Keys;
            var result = new List<string>();
            static string ConvertSearchPatternToRegex(string searchPattern)
            {
                string regex = searchPattern.Replace(".", "\\.").Replace("*", ".*").Replace("?", ".");
                return $"^{regex}$";
            }

            var regex = new Regex(ConvertSearchPatternToRegex(string.Join("|", searchPatterns)), RegexOptions.Compiled);
            if (items != null)
            {
                foreach (var item in items)
                {
                    if (regex.IsMatch(item))
                    {
                        result.Add(item);
                    }
                }
            }

            return Task.FromResult((IReadOnlyCollection<string>)result.AsReadOnly());
        }
    }
}
