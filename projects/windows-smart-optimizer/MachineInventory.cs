using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.ServiceProcess;
using System.Windows.Forms;
using Microsoft.Win32;

internal sealed class MachineInventory
{
    public readonly List<string> Hardware = new List<string>();
    public readonly List<string> Launchers = new List<string>();
    public readonly List<string> Startup = new List<string>();
    public readonly List<string> Tasks = new List<string>();
    public readonly List<string> Services = new List<string>();
    public readonly List<string> Apps = new List<string>();

    public static MachineInventory Scan()
    {
        var result = new MachineInventory();
        result.Hardware.Add(Environment.OSVersion.VersionString);
        result.Hardware.Add("RAM visibile: " + (Telemetry.Read().Total >> 20) + " MiB");
        ReadWmi(result.Hardware, "Win32_Processor", "Name", "NumberOfLogicalProcessors");
        ReadWmi(result.Hardware, "Win32_VideoController", "Name", "DriverVersion");
        ReadWmi(result.Hardware, "Win32_DiskDrive", "Model", "MediaType");
        foreach (string game in FindGames()) result.Launchers.Add(game);

        ReadRegistry(result.Startup, Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run");
        ReadRegistry(result.Startup, Registry.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\Run");
        ScanServices(result.Services);
        ScanApps(result.Apps);
        ScanTasks(result.Tasks);
        return result;
    }

    private static void ReadWmi(List<string> output, string table, string first, string second)
    {
        try
        {
            using (var finder = new ManagementObjectSearcher("SELECT " + first + "," + second + " FROM " + table))
            {
                finder.Options.Timeout = TimeSpan.FromSeconds(3);
                using (var rows = finder.Get())
                    foreach (ManagementObject item in rows)
                        using (item) output.Add(Convert.ToString(item[first]) + "  |  " + Convert.ToString(item[second]));
            }
        }
        catch (ManagementException) { output.Add(table + ": dati non disponibili"); }
        catch (UnauthorizedAccessException) { output.Add(table + ": accesso limitato"); }
        catch (System.Runtime.InteropServices.COMException) { output.Add(table + ": dati non disponibili"); }
    }

    private static void ReadRegistry(List<string> items, RegistryKey hive, string path)
    {
        try
        {
            using (var key = hive.OpenSubKey(path))
                if (key != null)
                    foreach (string name in key.GetValueNames()) items.Add(name + "  |  " + hive.Name);
        }
        catch (UnauthorizedAccessException) { items.Add(hive.Name + ": accesso limitato"); }
        catch (System.Security.SecurityException) { items.Add(hive.Name + ": accesso limitato"); }
    }

    private static void ScanServices(List<string> items)
    {
        try
        {
            foreach (var service in ServiceController.GetServices())
            {
                using (service)
                {
                    try { items.Add(service.DisplayName + "  |  " + service.Status + "  |  non gestito"); }
                    catch (InvalidOperationException) { }
                }
            }
        }
        catch (System.ComponentModel.Win32Exception) { items.Add("Elenco dei servizi non accessibile."); }
        items.Sort(StringComparer.CurrentCultureIgnoreCase);
    }

    private static void ScanApps(List<string> items)
    {
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    string name = process.ProcessName;
                    string family = name == "msedge" || name == "chrome" || name == "firefox" ||
                        name == "brave" || name == "opera" ? "browser" : "processo";
                    items.Add(name + " [" + process.Id + "]  |  " + family + "  |  " +
                        (process.WorkingSet64 >> 20) + " MiB residenti");
                }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception) { }
            }
        }
        items.Sort(StringComparer.CurrentCultureIgnoreCase);
    }

    private static void ScanTasks(List<string> items)
    {
        object scheduler = null, root = null, tasks = null;
        try
        {
            Type type = Type.GetTypeFromProgID("Schedule.Service");
            if (type == null) { items.Add("Utilità di pianificazione non disponibile."); return; }
            scheduler = Activator.CreateInstance(type);
            const System.Reflection.BindingFlags call = System.Reflection.BindingFlags.InvokeMethod;
            const System.Reflection.BindingFlags property = System.Reflection.BindingFlags.GetProperty;
            type.InvokeMember("Connect", call, null, scheduler, new object[0]);
            root = type.InvokeMember("GetFolder", call, null, scheduler, new object[] { "\\" });
            tasks = root.GetType().InvokeMember("GetTasks", call, null, root, new object[] { 0 });
            foreach (object task in (System.Collections.IEnumerable)tasks)
            {
                try { items.Add(Convert.ToString(task.GetType().InvokeMember("Name", property, null, task, null))); }
                finally { Release(task); }
            }
            items.Add("Inventario limitato alle attività visibili nella cartella radice.");
        }
        catch (System.Runtime.InteropServices.COMException) { items.Add("Attività pianificate non accessibili."); }
        catch (System.Reflection.TargetInvocationException) { items.Add("Attività pianificate non accessibili."); }
        catch (InvalidCastException) { items.Add("Attività pianificate non enumerabili."); }
        finally { Release(tasks); Release(root); Release(scheduler); }
    }

    private static void Release(object item)
    {
        if (item != null && System.Runtime.InteropServices.Marshal.IsComObject(item))
            System.Runtime.InteropServices.Marshal.ReleaseComObject(item);
    }

    public static List<string> FindGames()
    {
        var games = new List<string>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (drive.DriveType != DriveType.Fixed) continue;
            string bedrock = Path.Combine(drive.RootDirectory.FullName, @"XboxGames\Minecraft for Windows\Content\Minecraft.Windows.exe");
            if (File.Exists(bedrock)) games.Add(bedrock);
        }
        return games;
    }

    public void AddTabs(TabControl tabs)
    {
        AddList(tabs, "Hardware e GPU", Hardware);
        AddList(tabs, "Giochi rilevati", Launchers);
        AddList(tabs, "Processi e browser", Apps);
        AddList(tabs, "Servizi", Services);
        AddList(tabs, "Avvio automatico", Startup);
        AddList(tabs, "Attività", Tasks);
        AddList(tabs, "Esperto", new List<string> {
            "Riduzione di priorità: solo app secondaria avviata dal booster; originale salvato, scadenza e rollback.",
            "Explorer, browser, servizi e protezioni: nessun intervento applicato.",
            "GPU: sola diagnosi; capacità di modifica e ripristino non verificate.",
            "Piano energetico, affinità e tweak storici: in valutazione."
        });
    }

    private static void AddList(TabControl tabs, string name, List<string> lines)
    {
        var tab = new TabPage(name);
        var list = new ListBox { Dock = DockStyle.Fill, HorizontalScrollbar = true };
        list.Items.AddRange(lines.ToArray());
        tab.Controls.Add(list);
        tabs.TabPages.Add(tab);
    }
}

internal sealed class GamePreferences
{
    public readonly int Level;
    public readonly bool LowerPriority;

    public GamePreferences(int level, bool lowerPriority)
    {
        if (level < 0 || level > 2) throw new ArgumentOutOfRangeException("level");
        Level = level;
        LowerPriority = lowerPriority;
    }

    public static GamePreferences Read(string path)
    {
        if (new FileInfo(path).Length > 128) throw new InvalidDataException("Profilo troppo grande.");
        string[] fields = File.ReadAllLines(path);
        int level;
        bool lower;
        if (fields.Length != 3 || fields[0] != "MatrixGameProfile/1" ||
            !Int32.TryParse(fields[1], out level) || level < 0 || level > 2 || !Boolean.TryParse(fields[2], out lower))
            throw new InvalidDataException("Profilo non valido.");
        return new GamePreferences(level, lower);
    }

    public void Save(string path)
    {
        string folder = Path.GetDirectoryName(path);
        Directory.CreateDirectory(folder);
        string pending = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.WriteAllLines(pending, new[] { "MatrixGameProfile/1", Level.ToString(), LowerPriority.ToString() });
            if (File.Exists(path)) File.Replace(pending, path, null);
            else File.Move(pending, path);
        }
        finally { if (File.Exists(pending)) File.Delete(pending); }
    }

    public static string PathFor(string stateFolder, string game)
    {
        using (var hash = System.Security.Cryptography.SHA256.Create())
        {
            var bytes = hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(game).ToUpperInvariant()));
            return Path.Combine(stateFolder, "profiles", BitConverter.ToString(bytes).Replace("-", "") + ".matrixprofile");
        }
    }
}
