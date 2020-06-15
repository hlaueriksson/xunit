using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

[Target(
    BuildTarget.TestFx,
    BuildTarget.Build
)]
public static class TestFx
{
    public static async Task OnExecute(BuildContext context)
    {
        context.BuildStep("Running .NET Framework tests");

        if (context.NeedMono)
        {
            context.WriteLineColor(ConsoleColor.Yellow, $"Skipping xUnit.net v1 tests on non-Windows machines.");
            Console.WriteLine();
        }
        else
        {
            var v1Folder = Path.Combine(context.BaseFolder, "src", "xunit.v1.tests", "bin", context.ConfigurationText, "net45");
            var v1OutputFileName = Path.Combine(context.TestOutputFolder, "xunit.v1.tests-net45");
            await context.Exec(context.ConsoleRunner32Exe, $"{v1Folder}/xunit.v1.tests.dll -appdomains denied {context.TestFlagsNonParallel} -xml \"{v1OutputFileName}.xml\" -html \"{v1OutputFileName}.html\"", workingDirectory: v1Folder);
        }

        if (context.NeedMono)
        {
            context.WriteLineColor(ConsoleColor.Yellow, $"Skipping xUnit.net v2 tests on non-Windows machines.");
            Console.WriteLine();
        }
        else
        {
            var v2Folder = Path.Combine(context.BaseFolder, "src", "xunit.v2.tests", "bin", context.ConfigurationText, "net452");
            var v2OutputFileName = Path.Combine(context.TestOutputFolder, "xunit.v2.tests-net452");
            await context.Exec(context.ConsoleRunner32Exe, $"{v2Folder}/xunit.v2.tests.dll -appdomains denied {context.TestFlagsParallel} -xml \"{v2OutputFileName}.xml\" -html \"{v2OutputFileName}.html\"", workingDirectory: v2Folder);
        }

        var netFxSubpath = Path.Combine("bin", context.ConfigurationText, "net4");
        var v3TestExes =
            Directory.GetFiles(context.BaseFolder, "xunit.v3.*.tests.exe", SearchOption.AllDirectories)
                .Where(x => x.Contains(netFxSubpath))
                .OrderBy(x => x);

        foreach (var v3TestExe in v3TestExes)
        {
            var folder = Path.GetDirectoryName(v3TestExe);
            var outputFileName = Path.Combine(context.TestOutputFolder, Path.GetFileNameWithoutExtension(v3TestExe) + "-" + Path.GetFileName(folder));

            await context.Exec(v3TestExe, $"{context.TestFlagsParallel} -xml \"{outputFileName}.xml\" -html \"{outputFileName}.html\"", workingDirectory: folder);
        }
    }
}
