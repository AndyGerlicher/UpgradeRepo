using CommandLine;
using Gardener.Core;
using System.Reflection;
using UpgradeRepo.Cpv;
using UpgradeRepo.LegacyCpv;
using UpgradeRepo.Rsp;

namespace UpgradeRepo
{
    public class Program
    {
        private static readonly IFileSystem _fileSystem = new FileSystem();

        public static async Task Main(string[] args)
        {
            Console.WriteLine($"Args: {string.Join(' ', args.Select(x => $"\"{x}\""))}");

            var types = LoadVerbs();
            try
            {
                var result = await Parser.Default.ParseArguments(args, types)
                    .WithParsedAsync(RunAsync);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                Environment.Exit(1);
            }
        }

        private static async Task RunAsync(object obj)
        {
            switch (obj)
            {
                case CpvOptions cpv:
                    if (string.IsNullOrEmpty(cpv.Path))
                    {
                        cpv.Path = Environment.CurrentDirectory;
                    }
                    await RunAsync(new CpvUpgradePlugin(), cpv);
                    break;
                case LegacyCpvOptions legacyCpv:
                    if (string.IsNullOrEmpty(legacyCpv.Path))
                    {
                        legacyCpv.Path = Environment.CurrentDirectory;
                    }
                    await RunAsync(new LegacyCpvPlugin(), legacyCpv);
                    break;
                case BuildRspOptions rspOptions:
                    if (string.IsNullOrEmpty(rspOptions.Path))
                    {
                        rspOptions.Path = Environment.CurrentDirectory;
                    }
                    await RunAsync(new AddRsp(), rspOptions);
                    break;
            }
        }

        private static async Task RunAsync(IUpgradePlugin plugin, ICommandLineOptions options)
        {
            if (!await plugin.CanApplyAsync(options, _fileSystem))
            {
                Console.WriteLine($"{options.Path} does not apply to {plugin}");
                Environment.Exit(1);
            }

            await plugin.ApplyAsync(options, _fileSystem);
        }

        private static void HandleErrors(IEnumerable<Error> obj)
        {
            Console.WriteLine("Error!" + string.Join(", ", obj));
        }

        private static Type[] LoadVerbs()
        {
            return Assembly.GetExecutingAssembly().GetTypes()
                .Where(t => t.GetCustomAttribute<VerbAttribute>() != null).ToArray();
        }
    }

    [Verb("cpv", HelpText = "Onboard to CPV")]
    public class CpvOptions : ICommandLineOptions
    {
        public string Path { get; set; } = Environment.CurrentDirectory;
    }

    [Verb("legacycpv", HelpText = "Upgrade Legacy CPV to Retail")]
    public class LegacyCpvOptions : ICommandLineOptions
    {
        public string Path { get; set; } = Environment.CurrentDirectory;
    }
    
    [Verb("rsp", HelpText = "Add default Directory.Build.rsp")]
    public class BuildRspOptions : ICommandLineOptions
    {
        public string Path { get; set; } = Environment.CurrentDirectory;
    }
}

