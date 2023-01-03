using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommandLine;
using Gardener.Core;

namespace UpgradeRepo
{
    internal interface IUpgradePlugin
    {
        Task<bool> CanApplyAsync(ICommandLineOptions options, IFileSystem fileSystem);

        Task<bool> ApplyAsync(ICommandLineOptions options, IFileSystem fileSystem);
    }

    internal interface ICommandLineOptions
    {
        [Option('p', "path")]
        string Path { get; set; }
    }
}
