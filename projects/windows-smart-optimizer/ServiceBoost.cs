using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Principal;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

internal sealed class ServiceBoost
{
    public const string Search = "WSearch";
    public const string Printing = "Spooler";
    public const string Prefetch = "SysMain";
    private static readonly string[] Managed = {
        Search, Printing, Prefetch, "BITS", "FDResPub", "iphlpsvc", "PcaSvc", "TrkWks", "VaultSvc"
    };
    public const int ObservedIObitServices = 0x1FA;
    private readonly string folder;
    private readonly string service;
    private readonly string journal;

    public ServiceBoost(string directory, string name)
    {
        Guid fixture;
        if (Array.IndexOf(Managed, name) < 0 && !(name.StartsWith("MatrixOptimizerTest-", StringComparison.Ordinal) &&
            Guid.TryParseExact(name.Substring("MatrixOptimizerTest-".Length), "N", out fixture)))
            throw new InvalidOperationException("Servizio fuori dall'elenco consentito.");
        folder = directory;
        service = name;
        journal = Path.Combine(directory, name == Search || Array.IndexOf(Managed, name) < 0 ?
            "search.pending" : "service-" + name + ".pending");
    }

    public static bool Pending(string directory)
    {
        foreach (string name in Managed)
            if (File.Exists(new ServiceBoost(directory, name).journal)) return true;
        return false;
    }

    public static string PausedServices(string directory)
    {
        var paused = new List<string>();
        foreach (string name in Managed)
        {
            if (!File.Exists(new ServiceBoost(directory, name).journal)) continue;
            try
            {
                using (var controller = new ServiceController(name))
                    if (controller.Status == ServiceControllerStatus.Stopped) paused.Add(name);
            }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
        }
        return String.Join(", ", paused);
    }

    private static void RestoreManaged(string directory)
    {
        var errors = new List<Exception>();
        for (int i = Managed.Length - 1; i >= 0; i--)
        {
            try { new ServiceBoost(directory, Managed[i]).Restore(); }
            catch (Exception error) { errors.Add(error); }
        }
        if (errors.Count > 0) throw new AggregateException("Ripristino dei servizi incompleto.", errors);
    }

    private bool CanRestoreStartupType(int startup)
    {
        return startup == 3 || (startup == 2 && Array.IndexOf(Managed, service) >= 0);
    }

    private static void DemandAdministrator()
    {
        using (var identity = WindowsIdentity.GetCurrent())
            if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
                throw new UnauthorizedAccessException("Servono privilegi amministrativi per il servizio.");
    }

    private int StartupType()
    {
        using (var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\" + service))
        {
            object value = key == null ? null : key.GetValue("Start");
            if (!(value is int)) throw new IOException("Tipo di avvio non disponibile.");
            return (int)value;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Status
    {
        public uint Type, State, Controls, Win32Exit, ServiceExit, Checkpoint, Hint;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr OpenSCManager(string machine, string database, uint rights);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr OpenService(IntPtr manager, string name, uint rights);
    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool ControlService(IntPtr target, uint command, out Status status);
    [DllImport("advapi32.dll")]
    private static extern bool CloseServiceHandle(IntPtr handle);

    private static void StopOnly(string name)
    {
        IntPtr manager = OpenSCManager(null, null, 1);
        if (manager == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            IntPtr handle = OpenService(manager, name, 0x20);
            if (handle == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            try
            {
                Status state;
                // SCM respinge l'arresto se nel frattempo parte un servizio dipendente.
                if (!ControlService(handle, 1, out state))
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            }
            finally { CloseServiceHandle(handle); }
        }
        finally { CloseServiceHandle(manager); }
    }

    public void Pause()
    {
        DemandAdministrator();
        if (File.Exists(journal)) throw new IOException("Ripristino del servizio già in attesa.");
        int original = StartupType();
        if (!CanRestoreStartupType(original)) throw new InvalidOperationException("Tipo di avvio non compatibile.");
        using (var controller = new ServiceController(service))
        {
            if (controller.Status != ServiceControllerStatus.Running || !controller.CanStop)
                throw new InvalidOperationException("Il servizio deve essere avviato e arrestabile.");
            foreach (var dependent in controller.DependentServices)
                using (dependent)
                    if (dependent.Status != ServiceControllerStatus.Stopped)
                        throw new InvalidOperationException("Un servizio dipendente è in uso.");

            Directory.CreateDirectory(folder);
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes("MatrixService/1\n" + service + "\n" + original);
            string staging = journal + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(staging, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }
                File.Move(staging, journal);
            }
            finally { if (File.Exists(staging)) File.Delete(staging); }
            try { StopOnly(service); }
            catch (System.ComponentModel.Win32Exception)
            {
                File.Delete(journal);
                throw;
            }
            controller.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(15));
        }
    }

    public void Restore()
    {
        if (!File.Exists(journal)) return;
        DemandAdministrator();
        string[] record = File.ReadAllLines(journal);
        int saved;
        if (record.Length != 3 || record[0] != "MatrixService/1" || record[1] != service ||
            !Int32.TryParse(record[2], out saved) || !CanRestoreStartupType(saved))
            throw new InvalidDataException("Il journal del servizio non è valido.");
        if (StartupType() == saved)
        {
            using (var controller = new ServiceController(service))
            {
                TimeSpan timeout = TimeSpan.FromSeconds(15);
                if (controller.Status == ServiceControllerStatus.StopPending)
                    controller.WaitForStatus(ServiceControllerStatus.Stopped, timeout);
                controller.Refresh();
                if (controller.Status == ServiceControllerStatus.Stopped) controller.Start();
                controller.Refresh();
                if (controller.Status == ServiceControllerStatus.StartPending)
                    controller.WaitForStatus(ServiceControllerStatus.Running, timeout);
                controller.Refresh();
                if (controller.Status != ServiceControllerStatus.Running)
                    throw new IOException("Ripristino del servizio non confermato.");
            }
        }
        File.Delete(journal);
    }

    private static string Channel(string id) { return @"Local\Matrix.Optimizer.Search." + id; }

    public static Process RunElevated(Process owner, Process game, string token, int selection)
    {
        string args = String.Format(CultureInfo.InvariantCulture, "--search-boost {0} {1} {2} {3} {4} {5}",
            owner.Id, owner.StartTime.ToFileTimeUtc(), game.Id, game.StartTime.ToFileTimeUtc(), token, selection);
        return Process.Start(new ProcessStartInfo(Application.ExecutablePath, args) { UseShellExecute = true, Verb = "runas" });
    }

    public static EventWaitHandle CreateStopSignal(string token)
    {
        return new EventWaitHandle(false, EventResetMode.ManualReset, Channel(token));
    }

    internal static int RunWorker(string[] args, string state)
    {
        try
        {
            if (args.Length == 1 && args[0] == "--search-recover")
            {
                using (var mutex = new Mutex(false, @"Global\Matrix.Optimizer.WindowsSearch"))
                {
                    bool taken;
                    try { taken = mutex.WaitOne(3000); }
                    catch (AbandonedMutexException) { taken = true; }
                    if (!taken) return 1;
                    try { RestoreManaged(state); return 0; }
                    finally { mutex.ReleaseMutex(); }
                }
            }
            Guid token;
            int selection;
            if (args.Length != 7 || !Guid.TryParseExact(args[5], "N", out token) ||
                !Int32.TryParse(args[6], out selection) || selection < 1 || selection >= (1 << Managed.Length)) return 2;
            using (var owner = Process.GetProcessById(Int32.Parse(args[1])))
            using (var game = Process.GetProcessById(Int32.Parse(args[3])))
            using (var self = Process.GetCurrentProcess())
            using (var stop = EventWaitHandle.OpenExisting(Channel(args[5])))
            using (var mutex = new Mutex(false, @"Global\Matrix.Optimizer.WindowsSearch"))
            {
                long ownerTime = Int64.Parse(args[2]), gameTime = Int64.Parse(args[4]);
                if (owner.HasExited || game.HasExited || owner.SessionId != self.SessionId || game.SessionId != self.SessionId ||
                    owner.StartTime.ToFileTimeUtc() != ownerTime || game.StartTime.ToFileTimeUtc() != gameTime ||
                    !ExecutableIdentity.Matches(owner.MainModule.FileName, Application.ExecutablePath) || stop.WaitOne(0)) return 2;
                bool taken;
                try { taken = mutex.WaitOne(0); }
                catch (AbandonedMutexException) { taken = true; }
                if (!taken) return 1;
                try
                {
                    try
                    {
                        RestoreManaged(state);
                        for (int i = 0; i < Managed.Length && !game.HasExited && !owner.HasExited && !stop.WaitOne(0); i++)
                        {
                            if ((selection & (1 << i)) == 0) continue;
                            try { new ServiceBoost(state, Managed[i]).Pause(); }
                            catch (InvalidOperationException) { }
                            catch (System.ComponentModel.Win32Exception) { }
                        }
                        while (!stop.WaitOne(500) && !owner.HasExited && !game.HasExited) { }
                    }
                    finally { RestoreManaged(state); }
                    return 0;
                }
                finally { mutex.ReleaseMutex(); }
            }
        }
        catch (Exception error)
        {
            MessageBox.Show(error.Message, "Servizi Boost — sospensione e ripristino", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return 1;
        }
    }
}
