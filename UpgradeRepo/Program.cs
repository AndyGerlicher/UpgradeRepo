using CommandLine;
using Gardener.Core;
using System.Reflection;
using Microsoft.Extensions.Logging;
using NLog.Config;
using NLog.Targets;
using UpgradeRepo.LegacyCpv;
using UpgradeRepo.Rsp;
using NLog.Extensions.Logging;
using UpgradeRepo.Cpm;

namespace UpgradeRepo
{
    public class Program
    {
        private static readonly IFileSystem _fileSystem = new FileSystem();
        private static ILogger? _logger;
        private static ILoggerFactory? _loggerFactory;

        public static async Task Main(string[] args)
        {
            _loggerFactory = LoggerFactory.Create(builder => builder.AddNLog(GetLoggingConfiguration()));
            _logger = _loggerFactory.CreateLogger<Program>();

            _logger.LogInformation($@"Args: {string.Join(' ', args.Select(x => $"\"{x}\""))}");

            var types = LoadVerbs();
            try
            {
                var result = await Parser.Default.ParseArguments(args, types)
                    .WithParsedAsync(RunAsync);
            }
            catch (Exception e)
            {
                _logger.LogError(e.ToString());
                Environment.Exit(1);
            }
        }

        private static async Task RunAsync(object obj)
        {
            switch (obj)
            {
                case CpmOptions cpm:
                    if (string.IsNullOrEmpty(cpm.Path))
                    {
                        cpm.Path = Environment.CurrentDirectory;
                    }
                    else
                    {
                        Environment.CurrentDirectory = cpm.Path;
                    }
                    await RunAsync(new CpmUpgradePlugin(_loggerFactory!.CreateLogger<LegacyCpvPlugin>()), cpm);
                    break;
                case LegacyCpvOptions legacyCpv:
                    if (string.IsNullOrEmpty(legacyCpv.Path))
                    {
                        legacyCpv.Path = Environment.CurrentDirectory;
                    }
                    else
                    {
                        Environment.CurrentDirectory = legacyCpv.Path;
                    }
                    await RunAsync(new LegacyCpvPlugin(_loggerFactory!.CreateLogger<LegacyCpvPlugin>()), legacyCpv);
                    break;
                case BuildRspOptions rspOptions:
                    if (string.IsNullOrEmpty(rspOptions.Path))
                    {
                        rspOptions.Path = Environment.CurrentDirectory;
                    }
                    else
                    {
                        Environment.CurrentDirectory = rspOptions.Path;
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

        private static LoggingConfiguration GetLoggingConfiguration()
        {
            var config = new LoggingConfiguration();

            static string OptionalLayout(string layout, string prefix, string suffix)
                => $@"${{when:when=length('{layout}') > 0:inner={prefix}{layout}{suffix}:else=}}";

            const string DateLayout = @"${date:format=HH\:mm\:ss.fff}";
            const string LoggerNameLayout = @"${logger:shortName=true}";
            const string ExceptionLayout = @"${exception:format=tostring}";
            const string MessageLayout = @"${message}";
            const string ScopeLayout = @"${ndlc}";

            var consoleTarget = new ColoredConsoleTarget
            {
                Layout = @$"{DateLayout} [{LoggerNameLayout}]{OptionalLayout(ScopeLayout, "[", "]")} {MessageLayout}{OptionalLayout(ExceptionLayout, " ", string.Empty)}",
            };

            // Override the default color for error (yellow) and warn (magenta)
            consoleTarget.RowHighlightingRules.Add(new ConsoleRowHighlightingRule(condition: "level >= LogLevel.Error", foregroundColor: ConsoleOutputColor.Red, backgroundColor: ConsoleOutputColor.NoChange));
            consoleTarget.RowHighlightingRules.Add(new ConsoleRowHighlightingRule(condition: "level == LogLevel.Warn", foregroundColor: ConsoleOutputColor.Yellow, backgroundColor: ConsoleOutputColor.NoChange));
            config.AddTarget("console", consoleTarget);
            config.LoggingRules.Add(new LoggingRule("*", NLog.LogLevel.Debug, consoleTarget));

            return config;
        }
    }

    [Verb("cpm", HelpText = "Onboard to CPM")]
    public class CpmOptions : ICommandLineOptions
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

