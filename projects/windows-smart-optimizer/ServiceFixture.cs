using System;
using System.ServiceProcess;

internal sealed class ServiceFixture : ServiceBase
{
    private bool running;

    private ServiceFixture(string name)
    {
        ServiceName = name;
        CanStop = true;
        AutoLog = false;
    }

    protected override void OnStart(string[] args) { running = true; }
    protected override void OnStop() { running = false; }

    private static int Main(string[] args)
    {
        if (args.Length != 1) return 2;
        if (args[0] == "--self-test")
        {
            using (var fixture = new ServiceFixture("MatrixOptimizerSelfTest"))
            {
                fixture.OnStart(new string[0]);
                if (!fixture.running) return 1;
                fixture.OnStop();
                return fixture.running ? 1 : 0;
            }
        }

        Guid token;
        const string prefix = "MatrixOptimizerTest-";
        if (!args[0].StartsWith(prefix, StringComparison.Ordinal) ||
            !Guid.TryParseExact(args[0].Substring(prefix.Length), "N", out token)) return 2;
        using (var fixture = new ServiceFixture(args[0])) Run(fixture);
        return 0;
    }
}
