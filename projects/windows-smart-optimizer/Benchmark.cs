using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

internal static class Benchmark
{
    public static int Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--load")
        {
            var timeout = Stopwatch.StartNew();
            double value = 1;
            while (timeout.Elapsed.TotalSeconds < 25)
                for (int i = 0; i < 10000; i++) value = Math.Sqrt(value + i);
            return (int)value;
        }
        if (args.Length != 1) return 2;
        var action = new PriorityAction(args[0]);
        using (var own = Process.GetCurrentProcess())
        using (var foreground = StartLoad(own.MainModule.FileName))
        using (var background = StartLoad(own.MainModule.FileName))
        {
            try
            {
                long mask = own.ProcessorAffinity.ToInt64();
                var core = new IntPtr(mask & -mask);
                foreground.ProcessorAffinity = core;
                background.ProcessorAffinity = core;
                Thread.Sleep(300);
                for (int trial = 1; trial <= 3; trial++)
                {
                    double baseline = Share(foreground, background);
                    action.Apply(background);
                    background.Refresh();
                    if (background.PriorityClass != ProcessPriorityClass.BelowNormal)
                        throw new InvalidOperationException("Priorità non ridotta.");
                    double reduced = Share(foreground, background);
                    action.Restore();
                    background.Refresh();
                    if (background.PriorityClass != ProcessPriorityClass.Normal || action.Pending)
                        throw new InvalidOperationException("Priorità non ripristinata.");
                    Console.WriteLine("{0}: base {1:F1}%, intervento {2:F1}%", trial, baseline, reduced);
                }
            }
            finally
            {
                try { action.Restore(); }
                finally
                {
                    Stop(foreground);
                    Stop(background);
                }
            }
        }
        return 0;
    }

    private static Process StartLoad(string path)
    {
        return Process.Start(new ProcessStartInfo(path, "--load") { UseShellExecute = false, CreateNoWindow = true });
    }

    private static double Share(Process foreground, Process background)
    {
        foreground.Refresh();
        background.Refresh();
        double first = foreground.TotalProcessorTime.TotalMilliseconds;
        double second = background.TotalProcessorTime.TotalMilliseconds;
        Thread.Sleep(1250);
        foreground.Refresh();
        background.Refresh();
        double a = foreground.TotalProcessorTime.TotalMilliseconds - first;
        double b = background.TotalProcessorTime.TotalMilliseconds - second;
        if (a + b <= 0) throw new InvalidOperationException("Campione CPU non disponibile.");
        return 100 * a / (a + b);
    }

    private static void Stop(Process process)
    {
        if (process.HasExited) return;
        process.Kill();
        process.WaitForExit(5000);
    }
}
