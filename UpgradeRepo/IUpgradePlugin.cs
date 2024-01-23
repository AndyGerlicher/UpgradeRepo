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
        Task<bool> CanApplyAsync(OperateContext context);

        Task<bool> ApplyAsync(OperateContext context);
    }

    internal interface ICommandLineOptions
    {
        [Option('p', "path")]
        string Path { get; set; }
    }
}
