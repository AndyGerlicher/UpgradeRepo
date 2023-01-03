using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

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

        public static TestProjectFile CreateWithPackage(string name, string version)
        {
            string contents = @$"<PackageReference Include=""{name}"" Version=""{version}"" />";
            
            return new TestProjectFile(contents);
        }

        public static TestProjectFile CreateWithMultiplePackages(List<Tuple<string, string>> packages)
        {
            var sb = new StringBuilder();
            foreach (var item in packages)
            {
                sb.AppendLine(@$"<PackageReference Include=""{item.Item1}"" Version=""{item.Item2}"" />");
            }
            

            return new TestProjectFile(sb.ToString());
        }
    }
}
