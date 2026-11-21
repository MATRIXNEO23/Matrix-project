using System;
using System.IO;
using System.ServiceProcess;

internal static class ServiceAdapterProbe
{
    private static int Main(string[] args)
    {
        Guid id;
        const string prefix = "MatrixOptimizerTest-";
        if (args.Length != 2 || !args[0].StartsWith(prefix, StringComparison.Ordinal) ||
            !Guid.TryParseExact(args[0].Substring(prefix.Length), "N", out id) ||
            Path.GetFileName(Path.GetFullPath(args[1])) != args[0]) return 2;
        try
        {
            var adapter = new ServiceBoost(args[1], args[0]);
            using (var service = new ServiceController(args[0]))
            {
                try
                {
                    adapter.Pause();
                    service.Refresh();
                    if (service.Status != ServiceControllerStatus.Stopped || !File.Exists(Path.Combine(args[1], "search.pending")))
                        throw new Exception("Stop o scrittura anticipata del journal non verificati.");
                    new ServiceBoost(args[1], args[0]).Restore();
                    service.Refresh();
                    if (service.Status != ServiceControllerStatus.Running || File.Exists(Path.Combine(args[1], "search.pending")))
                        throw new Exception("Riavvio o rimozione del journal non verificati.");
                }
                finally { adapter.Restore(); }
            }
            Console.WriteLine("Adapter app: arresto SCM, journal e riavvio verificati sul servizio temporaneo.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error.Message); return 1; }
    }
}
