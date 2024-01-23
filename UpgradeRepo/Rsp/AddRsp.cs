using Gardener.Core;

namespace UpgradeRepo.Rsp
{
    internal class AddRsp : IUpgradePlugin
    {
        private const string rspFileName = "Directory.Build.rsp";
        public async Task<bool> CanApplyAsync(OperateContext context)
        {
            return !await context.FileSystem.FileExistsAsync(Path.Combine(context.Options.Path, rspFileName));
        }

        public async Task<bool> ApplyAsync(OperateContext context)
        {
            string defaultContents = @"-ConsoleLoggerParameters:Verbosity=Minimal;Summary;ForceNoAlign
-MaxCPUCount
-NodeReuse:false
-Restore
-Property:NuGetInteractive=True
-graphBuild
-terminallogger
";
            
            string rspFilePath = Path.Combine(context.Options.Path, rspFileName);
            Console.WriteLine($"Writing {rspFilePath}");
            await context.FileSystem.WriteAllTextAsync(rspFilePath, defaultContents);

            return true;
        }
    }
}
