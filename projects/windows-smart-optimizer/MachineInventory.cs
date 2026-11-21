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
    public readonly List<string> Gpu = new List<string>();
    public readonly List<GameEntry> Games = new List<GameEntry>();
    public readonly List<GameEntry> Installed = new List<GameEntry>();
    public readonly List<string> Startup = new List<string>();
    public readonly List<string> Tasks = new List<string>();
    public readonly List<string> Services = new List<string>();
    public readonly List<string> Apps = new List<string>();

    public static MachineInventory Scan()
    {
        var result = new MachineInventory();
        ReadWmi(result.Hardware, "Win32_OperatingSystem", "Caption", "Version");
        result.Hardware.Add("RAM visibile: " + (Telemetry.Read().Total >> 20) + " MiB");
        ReadWmi(result.Hardware, "Win32_Processor", "Name", "NumberOfLogicalProcessors");
        ReadWmi(result.Gpu, "Win32_VideoController", "Name", "DriverVersion");
        AmdDiagnostics.Read(result.Gpu);
        ReadWmi(result.Hardware, "Win32_DiskDrive", "Model", "MediaType");
        result.Games.AddRange(FindGames());
        result.Installed.AddRange(FindInstalledApps());

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
        object scheduler = null, root = null;
        try
        {
            Type type = Type.GetTypeFromProgID("Schedule.Service");
            if (type == null) { items.Add("Utilità di pianificazione non disponibile."); return; }
            scheduler = Activator.CreateInstance(type);
            const System.Reflection.BindingFlags call = System.Reflection.BindingFlags.InvokeMethod;
            type.InvokeMember("Connect", call, null, scheduler, new object[0]);
            root = type.InvokeMember("GetFolder", call, null, scheduler, new object[] { "\\" });
            int budget = 64;
            ScanTaskFolder(root, items, ref budget);
            if (budget == 0 || items.Count >= 512) items.Add("Inventario attività limitato a 64 cartelle o 512 voci.");
        }
        catch (System.Runtime.InteropServices.COMException) { items.Add("Attività pianificate non accessibili."); }
        catch (System.Reflection.TargetInvocationException) { items.Add("Attività pianificate non accessibili."); }
        catch (InvalidCastException) { items.Add("Attività pianificate non enumerabili."); }
        finally { Release(root); Release(scheduler); }
    }

    private static void ScanTaskFolder(object folder, List<string> items, ref int budget)
    {
        if (budget == 0 || items.Count >= 512) return;
        budget--;
        object tasks = null, folders = null;
        const System.Reflection.BindingFlags call = System.Reflection.BindingFlags.InvokeMethod;
        const System.Reflection.BindingFlags property = System.Reflection.BindingFlags.GetProperty;
        try
        {
            tasks = folder.GetType().InvokeMember("GetTasks", call, null, folder, new object[] { 1 });
            foreach (object task in (System.Collections.IEnumerable)tasks)
            {
                try
                {
                    string path = Convert.ToString(task.GetType().InvokeMember("Path", property, null, task, null));
                    string enabled = Convert.ToBoolean(task.GetType().InvokeMember("Enabled", property, null, task, null))
                        ? "abilitata" : "disabilitata";
                    items.Add(path + "  |  " + enabled);
                }
                finally { Release(task); }
                if (items.Count >= 512) return;
            }
            folders = folder.GetType().InvokeMember("GetFolders", call, null, folder, new object[] { 0 });
            foreach (object child in (System.Collections.IEnumerable)folders)
            {
                try { ScanTaskFolder(child, items, ref budget); }
                finally { Release(child); }
                if (budget == 0 || items.Count >= 512) return;
            }
        }
        catch (System.Runtime.InteropServices.COMException) { items.Add("Una cartella delle attività non è accessibile."); }
        catch (System.Reflection.TargetInvocationException) { items.Add("Una cartella delle attività non è accessibile."); }
        finally { Release(folders); Release(tasks); }
    }

    private static void Release(object item)
    {
        if (item != null && System.Runtime.InteropServices.Marshal.IsComObject(item))
            System.Runtime.InteropServices.Marshal.ReleaseComObject(item);
    }

    public static List<GameEntry> FindGames()
    {
        var games = new List<GameEntry>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (drive.DriveType != DriveType.Fixed) continue;
            string executable = Path.Combine(drive.RootDirectory.FullName, @"XboxGames\Minecraft for Windows\Content\Minecraft.Windows.exe");
            if (File.Exists(executable)) games.Add(new GameEntry("Minecraft for Windows", Path.GetDirectoryName(executable), executable));
        }

        var steamRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddSteamRoot(steamRoots, Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath");
        AddSteamRoot(steamRoots, Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath");
        AddSteamRoot(steamRoots, Registry.LocalMachine, @"SOFTWARE\Valve\Steam", "InstallPath");
        foreach (string root in steamRoots) games.AddRange(ScanSteam(root));
        games.Sort((a, b) => StringComparer.CurrentCultureIgnoreCase.Compare(a.Name, b.Name));
        return games;
    }

    private static void AddSteamRoot(HashSet<string> roots, RegistryKey hive, string keyPath, string value)
    {
        try
        {
            using (var key = hive.OpenSubKey(keyPath))
            {
                string path = key == null ? null : key.GetValue(value) as string;
                if (IsLocalPath(path) && Directory.Exists(path)) roots.Add(Path.GetFullPath(path));
            }
        }
        catch (System.Security.SecurityException) { }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        catch (ArgumentException) { }
    }

    private static bool IsLocalPath(string path)
    {
        return !String.IsNullOrEmpty(path) && path.Length >= 3 &&
            Char.IsLetter(path[0]) && path[1] == ':' && (path[2] == '\\' || path[2] == '/');
    }

    public static List<GameEntry> ScanSteam(string root)
    {
        var result = new List<GameEntry>();
        var libraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Path.GetFullPath(root) };
        foreach (string path in VdfValues(Path.Combine(root, "steamapps", "libraryfolders.vdf"), "path"))
        {
            try { if (IsLocalPath(path) && Directory.Exists(path)) libraries.Add(Path.GetFullPath(path)); }
            catch (ArgumentException) { }
            catch (IOException) { }
            catch (NotSupportedException) { }
        }
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string library in libraries)
        {
            string manifests = Path.Combine(library, "steamapps");
            if (!Directory.Exists(manifests)) continue;
            try
            {
                foreach (string manifest in Directory.EnumerateFiles(manifests, "appmanifest_*.acf"))
                {
                    var names = VdfValues(manifest, "name");
                    var dirs = VdfValues(manifest, "installdir");
                    if (names.Count != 1 || dirs.Count != 1) continue;
                    string install = dirs[0];
                    if (install == "." || install == ".." || install.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                        install.IndexOf('/') >= 0 || install.IndexOf('\\') >= 0) continue;
                    string folder = Path.Combine(manifests, "common", install);
                    if (Directory.Exists(folder) && found.Add(folder)) result.Add(new GameEntry(names[0], folder, null));
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return result;
    }

    private static List<string> VdfValues(string file, string key)
    {
        var values = new List<string>();
        try
        {
            if (!File.Exists(file) || new FileInfo(file).Length > 1048576) return values;
            string linePattern = "^\\s*\"" + System.Text.RegularExpressions.Regex.Escape(key) +
                "\"\\s+\"((?:[^\"\\\\]|\\\\.)*)\"\\s*(?://.*)?$";
            foreach (string line in File.ReadLines(file))
            {
                var m = System.Text.RegularExpressions.Regex.Match(line, linePattern,
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
                if (m.Success) values.Add(m.Groups[1].Value.Replace("\\\"", "\"").Replace("\\\\", "\\"));
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return values;
    }

    private static List<GameEntry> FindInstalledApps()
    {
        var apps = new Dictionary<string, GameEntry>(StringComparer.CurrentCultureIgnoreCase);
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using (var root = RegistryKey.OpenBaseKey(hive, view))
                using (var uninstall = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"))
                {
                    if (uninstall == null) continue;
                    foreach (string key in uninstall.GetSubKeyNames())
                    {
                        using (var item = uninstall.OpenSubKey(key))
                        {
                            if (item == null || Object.Equals(item.GetValue("SystemComponent"), 1) ||
                                item.GetValue("ParentKeyName") != null) continue;
                            string name = item.GetValue("DisplayName") as string;
                            if (String.IsNullOrWhiteSpace(name)) continue;
                            string folder = ValidFolder(item.GetValue("InstallLocation") as string);
                            string exe = IconExecutable(item.GetValue("DisplayIcon") as string);
                            AddApp(apps, name, folder, exe);
                        }
                    }
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (System.Security.SecurityException) { }
        }

        object shell = null, appsFolder = null, items = null;
        try
        {
            Type type = Type.GetTypeFromProgID("Shell.Application");
            if (type != null)
            {
                const System.Reflection.BindingFlags call = System.Reflection.BindingFlags.InvokeMethod;
                const System.Reflection.BindingFlags property = System.Reflection.BindingFlags.GetProperty;
                shell = Activator.CreateInstance(type);
                appsFolder = type.InvokeMember("NameSpace", call, null, shell, new object[] { "shell:AppsFolder" });
                if (appsFolder != null)
                {
                    items = appsFolder.GetType().InvokeMember("Items", call, null, appsFolder, null);
                    foreach (object item in (System.Collections.IEnumerable)items)
                    {
                        try
                        {
                            string name = Convert.ToString(item.GetType().InvokeMember("Name", property, null, item, null));
                            if (!String.IsNullOrWhiteSpace(name)) AddApp(apps, name, null, null);
                        }
                        finally { Release(item); }
                    }
                }
            }
        }
        catch (System.Runtime.InteropServices.COMException) { }
        catch (System.Reflection.TargetInvocationException) { }
        catch (InvalidCastException) { }
        finally { Release(items); Release(appsFolder); Release(shell); }
        var entries = new List<GameEntry>(apps.Values);
        entries.Sort((a, b) => StringComparer.CurrentCultureIgnoreCase.Compare(a.Name, b.Name));
        return entries;
    }

    private static void AddApp(Dictionary<string, GameEntry> apps, string name, string folder, string exe)
    {
        GameEntry previous;
        if (!apps.TryGetValue(name, out previous) || (previous.Executable == null && exe != null))
            apps[name] = new GameEntry(name, folder, exe);
    }

    private static string ValidFolder(string raw)
    {
        if (String.IsNullOrWhiteSpace(raw)) return null;
        string path = Environment.ExpandEnvironmentVariables(raw.Trim().Trim('"'));
        return IsLocalPath(path) && Directory.Exists(path) ? path : null;
    }

    private static string IconExecutable(string raw)
    {
        if (String.IsNullOrWhiteSpace(raw)) return null;
        var match = System.Text.RegularExpressions.Regex.Match(raw.Trim(),
            "^\"?(.+?\\.exe)\"?(?:\\s*,\\s*-?\\d+)?$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!match.Success) return null;
        string path = Environment.ExpandEnvironmentVariables(match.Groups[1].Value.Trim());
        string name = Path.GetFileNameWithoutExtension(path);
        if (!IsLocalPath(path) || String.IsNullOrEmpty(name) ||
            name.StartsWith("unins", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("msiexec", StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) return null;
        return path;
    }

    public void AddTabs(TabControl tabs)
    {
        AddList(tabs, "Hardware", Hardware);
        AddList(tabs, "GPU", Gpu);
        AddList(tabs, "Giochi rilevati", Games.ConvertAll(g => g.Name + "  |  " + g.Folder));
        AddList(tabs, "App installate", Installed.ConvertAll(g => g.Name));
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

internal sealed class GameEntry
{
    public readonly string Name, Folder, Executable;
    public GameEntry(string name, string folder, string executable)
    {
        Name = name;
        Folder = folder;
        Executable = executable;
    }
    public override string ToString() { return Name; }
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

internal sealed class GameTree : IDisposable
{
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential,
        CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private struct ProcessEntry
    {
        public uint Size, Usage, ProcessId;
        public UIntPtr Heap;
        public uint Module, Threads, ParentId;
        public int Priority;
        public uint Flags;
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 260)]
        public string Executable;
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern Microsoft.Win32.SafeHandles.SafeFileHandle CreateToolhelp32Snapshot(uint flags, uint process);

    [System.Runtime.InteropServices.DllImport("kernel32.dll", EntryPoint = "Process32FirstW")]
    private static extern bool First(Microsoft.Win32.SafeHandles.SafeFileHandle snapshot, ref ProcessEntry item);

    [System.Runtime.InteropServices.DllImport("kernel32.dll", EntryPoint = "Process32NextW")]
    private static extern bool Next(Microsoft.Win32.SafeHandles.SafeFileHandle snapshot, ref ProcessEntry item);

    private readonly Dictionary<int, Process> children = new Dictionary<int, Process>();
    private readonly string root;
    private readonly int session;

    public GameTree(Process game)
    {
        root = Path.GetDirectoryName(game.MainModule.FileName).TrimEnd('\\') + "\\";
        session = game.SessionId;
    }

    public bool Update(Process game)
    {
        var parents = new Dictionary<int, DateTime>();
        bool mainAlive = !game.HasExited;
        if (mainAlive) parents.Add(game.Id, game.StartTime);
        var stale = new List<int>();
        foreach (var pair in children)
        {
            if (pair.Value.HasExited) { pair.Value.Dispose(); stale.Add(pair.Key); }
            else parents[pair.Key] = pair.Value.StartTime;
        }
        foreach (int id in stale) children.Remove(id);
        if (parents.Count == 0) return false;

        var byParent = new Dictionary<int, List<int>>();
        using (var snapshot = CreateToolhelp32Snapshot(2, 0))
        {
            if (snapshot.IsInvalid) return mainAlive || children.Count > 0;
            var entry = new ProcessEntry {
                Size = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(ProcessEntry))
            };
            if (First(snapshot, ref entry))
                do
                {
                    if (!byParent.ContainsKey((int)entry.ParentId)) byParent.Add((int)entry.ParentId, new List<int>());
                    byParent[(int)entry.ParentId].Add((int)entry.ProcessId);
                } while (Next(snapshot, ref entry));
        }
        var scan = new Queue<int>(parents.Keys);
        while (scan.Count > 0)
        {
            int parent = scan.Dequeue();
            List<int> next;
            if (!byParent.TryGetValue(parent, out next)) continue;
            foreach (int id in next)
            {
                if (parents.ContainsKey(id)) continue;
                Process child = null;
                try
                {
                    child = Process.GetProcessById(id);
                    if (child.HasExited || child.SessionId != session || child.StartTime <= parents[parent] ||
                        !child.MainModule.FileName.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;
                    parents.Add(id, child.StartTime);
                    children.Add(id, child);
                    scan.Enqueue(id);
                    child = null;
                }
                catch (ArgumentException) { }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception) { }
                finally { if (child != null) child.Dispose(); }
            }
        }
        return mainAlive || children.Count > 0;
    }

    public bool Contains(int id) { return children.ContainsKey(id); }

    public void Dispose()
    {
        foreach (Process child in children.Values) child.Dispose();
        children.Clear();
    }
}

internal static class AmdDiagnostics
{
    [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
    private delegate IntPtr Allocator(int bytes);
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct Activity
    {
        public int Size, Core, Memory, Voltage, Utilization, PerformanceLevel, BusSpeed, Lanes, MaxLanes, Reserved;
    }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct Temperature { public int Size, MilliC; }

    [System.Runtime.InteropServices.DefaultDllImportSearchPaths(System.Runtime.InteropServices.DllImportSearchPath.System32)]
    [System.Runtime.InteropServices.DllImport("atiadlxx.dll", CallingConvention = System.Runtime.InteropServices.CallingConvention.StdCall)]
    private static extern int ADL_Main_Control_Create(Allocator allocation, int connected);
    [System.Runtime.InteropServices.DefaultDllImportSearchPaths(System.Runtime.InteropServices.DllImportSearchPath.System32)]
    [System.Runtime.InteropServices.DllImport("atiadlxx.dll")]
    private static extern int ADL_Main_Control_Destroy();
    [System.Runtime.InteropServices.DefaultDllImportSearchPaths(System.Runtime.InteropServices.DllImportSearchPath.System32)]
    [System.Runtime.InteropServices.DllImport("atiadlxx.dll")]
    private static extern int ADL_Adapter_NumberOfAdapters_Get(out int count);
    [System.Runtime.InteropServices.DefaultDllImportSearchPaths(System.Runtime.InteropServices.DllImportSearchPath.System32)]
    [System.Runtime.InteropServices.DllImport("atiadlxx.dll")]
    private static extern int ADL_Adapter_Active_Get(int adapter, out int active);
    [System.Runtime.InteropServices.DefaultDllImportSearchPaths(System.Runtime.InteropServices.DllImportSearchPath.System32)]
    [System.Runtime.InteropServices.DllImport("atiadlxx.dll")]
    private static extern int ADL_Overdrive_Caps(int adapter, out int supported, out int enabled, out int version);
    [System.Runtime.InteropServices.DefaultDllImportSearchPaths(System.Runtime.InteropServices.DllImportSearchPath.System32)]
    [System.Runtime.InteropServices.DllImport("atiadlxx.dll")]
    private static extern int ADL_Overdrive5_CurrentActivity_Get(int adapter, ref Activity activity);
    [System.Runtime.InteropServices.DefaultDllImportSearchPaths(System.Runtime.InteropServices.DllImportSearchPath.System32)]
    [System.Runtime.InteropServices.DllImport("atiadlxx.dll")]
    private static extern int ADL_Overdrive5_Temperature_Get(int adapter, int controller, ref Temperature temperature);

    public static void Read(List<string> output)
    {
        output.Add("Modalità GPU: sola diagnosi. Calibrazione e ripristino driver da verificare.");
        if (!Environment.Is64BitProcess || !File.Exists(Path.Combine(Environment.SystemDirectory, "atiadlxx.dll"))) return;

        var buffers = new List<IntPtr>();
        Allocator allocate = delegate(int bytes)
        {
            if (bytes <= 0) return IntPtr.Zero;
            try
            {
                IntPtr p = System.Runtime.InteropServices.Marshal.AllocHGlobal(bytes);
                buffers.Add(p);
                return p;
            }
            catch (OutOfMemoryException) { return IntPtr.Zero; }
        };
        bool initialized = false;
        try
        {
            if (ADL_Main_Control_Create(allocate, 1) != 0) return;
            initialized = true;
            int count;
            if (ADL_Adapter_NumberOfAdapters_Get(out count) != 0) return;
            for (int i = 0; i < Math.Min(count, 16); i++)
            {
                int active, supported, enabled, version;
                if (ADL_Adapter_Active_Get(i, out active) != 0 || active == 0 ||
                    ADL_Overdrive_Caps(i, out supported, out enabled, out version) != 0 || supported == 0) continue;
                output.Add("AMD ADL: Overdrive " + version + (enabled == 0 ? " (regolazione disabilitata)" : " (regolazione abilitata dal driver)"));
                if (version != 5) continue;
                var activity = new Activity { Size = System.Runtime.InteropServices.Marshal.SizeOf(typeof(Activity)) };
                if (ADL_Overdrive5_CurrentActivity_Get(i, ref activity) == 0 && activity.Utilization >= 0 && activity.Utilization <= 100 &&
                    activity.Core > 0 && activity.Memory > 0)
                    output.Add(String.Format("Core {0:0} MHz  |  Memoria {1:0} MHz  |  Utilizzo {2}%",
                        activity.Core / 100.0, activity.Memory / 100.0, activity.Utilization));
                var temperature = new Temperature { Size = System.Runtime.InteropServices.Marshal.SizeOf(typeof(Temperature)) };
                if (ADL_Overdrive5_Temperature_Get(i, 0, ref temperature) == 0 && temperature.MilliC >= 0 && temperature.MilliC < 150000)
                    output.Add(String.Format("Temperatura rilevata: {0:0.0} °C", temperature.MilliC / 1000.0));
            }
        }
        catch (DllNotFoundException) { output.Add("Driver AMD ADL non accessibile."); }
        catch (EntryPointNotFoundException) { output.Add("Telemetria non disponibile con questa versione del driver AMD."); }
        catch (BadImageFormatException) { output.Add("Architettura del driver AMD incompatibile."); }
        finally
        {
            try { if (initialized) ADL_Main_Control_Destroy(); }
            finally
            {
                GC.KeepAlive(allocate);
                foreach (IntPtr buffer in buffers) System.Runtime.InteropServices.Marshal.FreeHGlobal(buffer);
            }
        }
    }
}
