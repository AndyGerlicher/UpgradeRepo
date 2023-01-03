using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Gardener.Core;

namespace UpgradeRepo.Rsp
{
    internal class AddRsp : IUpgradePlugin
    {
        private const string rspFileName = "Directory.Build.rsp";
        public async Task<bool> CanApplyAsync(ICommandLineOptions options, IFileSystem fileSystem)
        {
            return !await fileSystem.FileExistsAsync(Path.Combine(options.Path, rspFileName));
        }

        public async Task<bool> ApplyAsync(ICommandLineOptions options, IFileSystem fileSystem)
        {
            string defaultContents = @"-ConsoleLoggerParameters:Verbosity=Minimal;Summary;ForceNoAlign
-MaxCPUCount
-NodeReuse:false
-Restore
-Property:NuGetInteractive=True
";
            
            string rspFilePath = Path.Combine(options.Path, rspFileName);
            Console.WriteLine($"Writing {rspFilePath}");
            await fileSystem.WriteAllTextAsync(rspFilePath, defaultContents);

            return true;
        }
    }
}
