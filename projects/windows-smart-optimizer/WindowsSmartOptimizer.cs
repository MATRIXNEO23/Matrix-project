using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

internal static class Optimizer
{
    private const string MutexName = @"Local\Matrix.WindowsSmartOptimizer";
    private static string StateFolder
    {
        get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Matrix", "WindowsSmartOptimizer"); }
    }

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length > 0 && (args[0] == "--search-boost" || args[0] == "--search-recover"))
            return ServiceBoost.RunWorker(args, StateFolder);
        if (args.Length > 0 && args[0] == "--test-worker") { Thread.Sleep(60000); return 0; }
        if (args.Length == 1 && args[0] == "--demo-worker") { DemoWindow(); return 0; }
        if (args.Length == 2 && args[0] == "--test-child-owner")
        {
            using (var child = Process.Start(Application.ExecutablePath, "--test-worker"))
            {
                File.WriteAllText(args[1], child.Id.ToString(CultureInfo.InvariantCulture));
                child.WaitForExit();
            }
            return 0;
        }
        if (args.Length == 2 && args[0] == "--self-test")
        {
            try { SelfTest(args[1]); return 0; }
            catch (Exception error) { File.WriteAllText(Path.Combine(args[1], "result.txt"), error.ToString()); return 1; }
        }
        if (args.Length == 3 && args[0] == "--watch")
            return Watch(Int32.Parse(args[1]), Int64.Parse(args[2]), StateFolder, MutexName);

        using (var mutex = new Mutex(false, MutexName))
        {
            bool acquired;
            try { acquired = mutex.WaitOne(0); }
            catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) { MessageBox.Show("Una sessione dell'ottimizzatore è già aperta."); return 1; }
            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                using (var window = new Dashboard(new PriorityAction(StateFolder))) Application.Run(window);
            }
            finally { mutex.ReleaseMutex(); }
        }
        return 0;
    }

    private static void DemoWindow()
    {
        Application.EnableVisualStyles();
        using (var process = Process.GetCurrentProcess())
        using (var window = new Form { Text = "Carico di prova del booster", Size = new Size(400, 155), StartPosition = FormStartPosition.CenterScreen })
        using (var label = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter })
        using (var timer = new System.Windows.Forms.Timer { Interval = 250 })
        {
            window.Controls.Add(label);
            timer.Tick += delegate { process.Refresh(); label.Text = "Priorità Windows: " + process.PriorityClass +
                "\nQuesta finestra resta aperta durante il ripristino."; };
            window.Shown += delegate { timer.Start(); };
            Application.Run(window);
        }
    }

    private static int Watch(int ownerId, long ownerStart, string state, string mutexName)
    {
        Process owner = null;
        try { owner = Process.GetProcessById(ownerId); }
        catch (ArgumentException) { }
        if (owner != null)
        {
            using (owner)
            {
                try
                {
                    if (owner.StartTime.ToFileTimeUtc() == ownerStart)
                    {
                        var transaction = new PriorityAction(state);
                        var power = new PowerPlanAction(state);
                        try
                        {
                            using (var ready = EventWaitHandle.OpenExisting(WatchReadyName(ownerId, ownerStart)))
                                ready.Set();
                        }
                        catch (WaitHandleCannotBeOpenedException) { }
                        while (!owner.WaitForExit(1000))
                        {
                            try { transaction.RestoreExpired(DateTime.UtcNow); }
                            catch (IOException) { }
                            catch (UnauthorizedAccessException) { }
                            catch (System.ComponentModel.Win32Exception) { }
                            try { power.RestoreExpired(DateTime.UtcNow); }
                            catch (IOException) { }
                            catch (UnauthorizedAccessException) { }
                            catch (System.ComponentModel.Win32Exception) { }
                        }
                    }
                }
                catch (InvalidOperationException) { }
            }
        }
        using (var mutex = new Mutex(false, mutexName))
        {
            bool acquired;
            try { acquired = mutex.WaitOne(5000); }
            catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) return 1;
            try
            {
                try { new PriorityAction(state).Restore(); }
                finally
                {
                    try { new PowerPlanAction(state).Restore(); }
                    finally
                    {
                        try { new WindowAction(state).Restore(); }
                        finally { new RelaunchAction(state).Restore(); }
                    }
                }
                return 0;
            }
            catch (Exception) { return 1; }
            finally { mutex.ReleaseMutex(); }
        }
    }

    private static string WatchReadyName(int pid, long start)
    {
        return String.Format(CultureInfo.InvariantCulture, @"Local\Matrix.Optimizer.Ready.{0}.{1}", pid, start);
    }

    public static Process StartWatcher()
    {
        using (var owner = Process.GetCurrentProcess())
        {
            long start = owner.StartTime.ToFileTimeUtc();
            using (var ready = new EventWaitHandle(false, EventResetMode.AutoReset, WatchReadyName(owner.Id, start)))
            {
                var watcher = Process.Start(new ProcessStartInfo(Application.ExecutablePath,
                    String.Format(CultureInfo.InvariantCulture, "--watch {0} {1}", owner.Id, start)) {
                        UseShellExecute = false, CreateNoWindow = true
                    });
                if (watcher != null && ready.WaitOne(3000) && !watcher.HasExited) return watcher;
                if (watcher != null) watcher.Dispose();
                throw new IOException("Il controllo di ripristino non ha confermato la disponibilità.");
            }
        }
    }

    public static Process FindGame(string path)
    {
        if (String.IsNullOrEmpty(path)) return null;
        Process match = null;
        int session;
        using (var current = Process.GetCurrentProcess()) session = current.SessionId;
        foreach (Process process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(path)))
        {
            try
            {
                if (match == null && process.SessionId == session &&
                    ExecutableIdentity.Matches(process.MainModule.FileName, path)) match = process;
            }
            catch (System.ComponentModel.Win32Exception) { }
            catch (InvalidOperationException) { }
            finally { if (match != process) process.Dispose(); }
        }
        return match;
    }

    private static void TestSteamDiscovery(string folder)
    {
        string steam = Path.Combine(folder, "steamapps");
        string common = Path.Combine(steam, "common");
        string installed = Path.Combine(common, "GameFixture");
        Directory.CreateDirectory(installed);
        string real = Path.Combine(steam, "appmanifest_1.acf");
        string bad = Path.Combine(steam, "appmanifest_2.acf");
        try
        {
            File.WriteAllText(real, "\"AppState\"\n{\n\"name\" \"Fixture Steam\"\n\"installdir\" \"GameFixture\"\n}\n");
            File.WriteAllText(bad, "\"name\" \"Percorso non valido\"\n\"installdir\" \"..\"\n");
            var games = MachineInventory.ScanSteam(folder);
            if (games.Count != 1 || games[0].Name != "Fixture Steam" || games[0].Folder != installed || games[0].Executable != null)
                throw new Exception("Libreria Steam sintetica non riconosciuta.");
        }
        finally
        {
            File.Delete(real);
            File.Delete(bad);
            Directory.Delete(installed);
            Directory.Delete(common);
            Directory.Delete(steam);
        }
    }

    private static void SelfTest(string directory)
    {
        TestSteamDiscovery(directory);
        string exe = Application.ExecutablePath;
        string gamePath = Path.Combine(directory, "GameFixture.exe");
        File.Copy(exe, gamePath, false);
        using (Process worker = Process.Start(exe, "--test-worker"))
        using (Process game = Process.Start(gamePath, "--test-worker"))
        {
            try
            {
                using (Process detected = FindGame(gamePath))
                    if (detected == null || detected.Id != game.Id) throw new Exception("Rilevamento gioco fallito.");
                var initial = worker.PriorityClass;
                var sample = Telemetry.Read();
                if (sample.Total == 0 || sample.Available > sample.Total || sample.CommitLimit == 0)
                    throw new Exception("Telemetria non valida.");
                var action = new PriorityAction(directory);
                action.Apply(worker);
                if (worker.PriorityClass != ProcessPriorityClass.BelowNormal) throw new Exception("Azione non applicata.");
                game.Kill();
                if (!game.WaitForExit(5000)) throw new Exception("Il gioco di prova non è uscito.");
                using (Process detected = FindGame(gamePath))
                    if (detected != null) throw new Exception("Rilevamento uscita fallito.");
                action.Restore();
                worker.Refresh();
                if (worker.PriorityClass != initial || action.Pending) throw new Exception("Ripristino non riuscito.");
                action.Apply(worker);
                new PriorityAction(directory).Restore();
                worker.Refresh();
                if (worker.PriorityClass != initial || action.Pending)
                    throw new Exception("Recupero del journal non riuscito.");
                // Il proprietario di prova è una terza istanza, distinta dal processo ottimizzato.
                using (Process owner = Process.Start(exe, "--test-worker"))
                {
                    var ownerStart = owner.StartTime.ToFileTimeUtc();
                    action.Apply(worker);
                    owner.Kill();
                    if (!owner.WaitForExit(5000)) throw new Exception("Arresto del proprietario di prova fallito.");
                    int recovery = Watch(owner.Id, ownerStart, directory, @"Local\Matrix.WindowsSmartOptimizer.Test");
                    worker.Refresh();
                    if (recovery != 0 || worker.PriorityClass != initial || action.Pending)
                        throw new Exception("Ripristino dopo arresto del proprietario non riuscito.");
                }
                action.Apply(worker);
                action.RestoreExpired(DateTime.UtcNow);
                if (!action.Pending) throw new Exception("Il watchdog ha anticipato la scadenza.");
                action.RestoreExpired(DateTime.UtcNow.AddSeconds(16));
                worker.Refresh();
                if (worker.PriorityClass != initial || action.Pending) throw new Exception("Scadenza non rispettata.");
                action.Apply(worker);
                worker.PriorityClass = ProcessPriorityClass.Idle;
                action.Restore();
                worker.Refresh();
                if (worker.PriorityClass != ProcessPriorityClass.Idle || action.Pending)
                    throw new Exception("Modifica concorrente sovrascritta.");
                worker.PriorityClass = initial;
                VerifyJournalFailures(worker, action, directory, initial);
                VerifyGameTree(gamePath, directory);
                File.WriteAllText(Path.Combine(directory, "result.txt"),
                    "OK: inventario Steam sintetico, telemetria, rilevamento/uscita gioco simulato, priorità, ripristino, recupero journal, arresto proprietario, scadenza, modifica concorrente, registri non validi e gioco con processo figlio.");
            }
            finally
            {
                if (!game.HasExited) game.Kill();
                if (!worker.HasExited) worker.Kill();
            }
        }
    }
    private static void VerifyJournalFailures(Process worker, PriorityAction action, string directory, ProcessPriorityClass original)
    {
        string journal = Path.Combine(directory, "priority.pending");
        string missingDirectory = Path.Combine(directory, "fixture-not-a-directory");
        try
        {
            File.WriteAllText(journal, "corrotto");
            bool rejected = false;
            try { action.Restore(); }
            catch (InvalidDataException) { rejected = true; }
            worker.Refresh();
            if (!rejected || File.ReadAllText(journal) != "corrotto" || worker.PriorityClass != original)
                throw new Exception("Registro corrotto non conservato o processo alterato.");
            File.Delete(journal);

            File.WriteAllText(missingDirectory, "fixture");
            rejected = false;
            try { new PriorityAction(missingDirectory).Apply(worker); }
            catch (IOException) { rejected = true; }
            worker.Refresh();
            if (!rejected || worker.PriorityClass != original)
                throw new Exception("Intervento consentito senza journal durabile.");

            string id = worker.Id.ToString(CultureInfo.InvariantCulture);
            long start = worker.StartTime.ToFileTimeUtc();
            string path = worker.MainModule.FileName;
            worker.PriorityClass = ProcessPriorityClass.BelowNormal;
            File.WriteAllLines(journal, new[] { id, (start - 1).ToString(CultureInfo.InvariantCulture), path });
            action.Restore();
            worker.Refresh();
            if (action.Pending || worker.PriorityClass != ProcessPriorityClass.BelowNormal)
                throw new Exception("Il rollback ha alterato una diversa istanza dello stesso PID.");

            File.WriteAllLines(journal, new[] { id, start.ToString(CultureInfo.InvariantCulture), path + ".different" });
            action.Restore();
            worker.Refresh();
            if (action.Pending || worker.PriorityClass != ProcessPriorityClass.BelowNormal)
                throw new Exception("Il rollback ha alterato un eseguibile diverso.");

            File.WriteAllLines(journal, new[] { id, start.ToString(CultureInfo.InvariantCulture), path });
            action.Restore();
            worker.Refresh();
            if (action.Pending || worker.PriorityClass != original)
                throw new Exception("Il rollback del journal precedente è fallito.");
        }
        finally
        {
            if (!worker.HasExited && worker.PriorityClass != original) worker.PriorityClass = original;
            File.Delete(journal);
            File.Delete(missingDirectory);
        }
    }

    private static void VerifyGameTree(string exe, string directory)
    {
        string marker = Path.Combine(directory, "child.pid");
        Process child = null;
        using (var parent = Process.Start(exe, "--test-child-owner \"" + marker + "\""))
        {
            try
            {
                int id = 0;
                var clock = Stopwatch.StartNew();
                while (clock.ElapsedMilliseconds < 5000)
                {
                    try { if (File.Exists(marker) && Int32.TryParse(File.ReadAllText(marker), out id)) break; }
                    catch (IOException) { }
                    Thread.Sleep(50);
                }
                if (id == 0) throw new Exception("Processo figlio non pronto.");
                child = Process.GetProcessById(id);
                using (var tree = new GameTree(parent))
                {
                    if (!tree.Update(parent) || !tree.Contains(id)) throw new Exception("Figlio del gioco non rilevato.");
                    parent.Kill();
                    if (!parent.WaitForExit(5000) || !tree.Update(parent)) throw new Exception("Sessione chiusa prima del figlio.");
                    child.Kill();
                    if (!child.WaitForExit(5000) || tree.Update(parent)) throw new Exception("Uscita dell'albero non rilevata.");
                }
            }
            finally
            {
                if (!parent.HasExited) { parent.Kill(); parent.WaitForExit(5000); }
                if (child != null)
                {
                    if (!child.HasExited) { child.Kill(); child.WaitForExit(5000); }
                    child.Dispose();
                }
                File.Delete(marker);
            }
        }
    }
}

internal static class ExecutableIdentity
{
    [StructLayout(LayoutKind.Sequential)]
    private struct FileId
    {
        public ulong Volume;
        public ulong Low;
        public ulong High;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern Microsoft.Win32.SafeHandles.SafeFileHandle CreateFile(
        string path, uint access, uint sharing, IntPtr security, uint creation, uint flags, IntPtr template);

    [DllImport("kernel32.dll")]
    private static extern bool GetFileInformationByHandleEx(
        Microsoft.Win32.SafeHandles.SafeFileHandle handle, int kind, out FileId id, uint size);

    public static bool Matches(string actual, string selected)
    {
        if (String.Equals(actual, selected, StringComparison.OrdinalIgnoreCase)) return true;
        FileId a, b;
        return TryGetId(actual, out a) && TryGetId(selected, out b) &&
            a.Volume == b.Volume && a.Low == b.Low && a.High == b.High;
    }

    private static bool TryGetId(string path, out FileId id)
    {
        id = new FileId();
        using (var handle = CreateFile(path, 0x80, 7, IntPtr.Zero, 3, 0, IntPtr.Zero))
            return !handle.IsInvalid && GetFileInformationByHandleEx(handle, 18, out id, (uint)Marshal.SizeOf(typeof(FileId)));
    }
}

internal sealed class PriorityAction
{
    private readonly string journal, mutexName;

    public PriorityAction(string folder)
    {
        journal = Path.Combine(Path.GetFullPath(folder), "priority.pending");
        using (var hash = System.Security.Cryptography.SHA256.Create())
            mutexName = @"Local\Matrix.Optimizer.Undo." + Convert.ToBase64String(
                hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes(journal.ToUpperInvariant()))).Replace('/', '_');
    }

    private void Locked(Action work)
    {
        using (var mutex = new Mutex(false, mutexName))
        {
            bool entered;
            try { entered = mutex.WaitOne(3000); }
            catch (AbandonedMutexException) { entered = true; }
            if (!entered) throw new IOException("Registro di ripristino occupato.");
            try { work(); }
            finally { mutex.ReleaseMutex(); }
        }
    }
    public bool Pending { get { return File.Exists(journal); } }
    public string StateFolder { get { return Path.GetDirectoryName(journal); } }

    public void Record(string outcome, Telemetry before, Telemetry after)
    {
        string line = String.Format(CultureInfo.InvariantCulture, "{0:O}\t{1}\t{2}\t{3}\t{4}\t{5}",
            DateTime.UtcNow, outcome, before.Available >> 20, after.Available >> 20, before.Commit >> 20, after.Commit >> 20);
        File.AppendAllText(Path.Combine(Path.GetDirectoryName(journal), "misure.tsv"), line + Environment.NewLine);
    }

    public void Apply(Process process) { Locked(delegate { ApplyCore(process); }); }

    private void ApplyCore(Process process)
    {
        if (Pending) throw new InvalidOperationException("Ripristina prima l'intervento precedente.");
        process.Refresh();
        if (process.HasExited || process.SessionId == 0) throw new InvalidOperationException("Processo non gestibile.");
        IntPtr processHandle = process.Handle; // Mantiene l'identità del processo fino al rilascio.
        if (processHandle == IntPtr.Zero || process.HasExited) throw new InvalidOperationException("Processo terminato.");
        string path = process.MainModule.FileName;
        string name = Path.GetFileNameWithoutExtension(path);
        string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows).TrimEnd('\\') + "\\";
        if (path.StartsWith(windows, StringComparison.OrdinalIgnoreCase) || IsProtectedName(name))
            throw new InvalidOperationException("Sistema o protezione esclusi dall'intervento.");
        var original = process.PriorityClass;
        if (original != ProcessPriorityClass.Normal) return;
        string record = String.Join("\n", process.Id.ToString(CultureInfo.InvariantCulture),
            process.StartTime.ToFileTimeUtc().ToString(CultureInfo.InvariantCulture), path,
            Guid.NewGuid().ToString("D"), ((int)original).ToString(CultureInfo.InvariantCulture),
            ((int)ProcessPriorityClass.BelowNormal).ToString(CultureInfo.InvariantCulture),
            DateTime.UtcNow.AddSeconds(15).Ticks.ToString(CultureInfo.InvariantCulture));
        Directory.CreateDirectory(Path.GetDirectoryName(journal));
        string temporary = journal + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(record);
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                4096, FileOptions.WriteThrough))
            {
                file.Write(bytes, 0, bytes.Length);
                file.Flush(true);
            }
            File.Move(temporary, journal);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        process.PriorityClass = ProcessPriorityClass.BelowNormal;
    }

    public static bool IsProtectedName(string name)
    {
        return String.Equals(name, "explorer", StringComparison.OrdinalIgnoreCase) ||
            String.Equals(name, "MsMpEng", StringComparison.OrdinalIgnoreCase) ||
            String.Equals(name, "NisSrv", StringComparison.OrdinalIgnoreCase) ||
            String.Equals(name, "MpDefenderCoreService", StringComparison.OrdinalIgnoreCase);
    }

    public void Restore() { Locked(delegate { RestoreCore(null); }); }

    public void RestoreExpired(DateTime now) { Locked(delegate { RestoreCore(now); }); }

    private void RestoreCore(DateTime? expireBefore)
    {
        if (!Pending) return;
        var fields = File.ReadAllLines(journal);
        int id;
        long start;
        if ((fields.Length != 3 && fields.Length != 7) || !Int32.TryParse(fields[0], out id) ||
            !Int64.TryParse(fields[1], out start) || !Path.IsPathRooted(fields[2]))
            throw new InvalidDataException("Registro di ripristino non valido.");
        if (fields.Length == 7)
        {
            Guid sessionId;
            int original, applied;
            long deadline;
            if (!Guid.TryParseExact(fields[3], "D", out sessionId) ||
                !Int32.TryParse(fields[4], out original) || original != (int)ProcessPriorityClass.Normal ||
                !Int32.TryParse(fields[5], out applied) || applied != (int)ProcessPriorityClass.BelowNormal ||
                !Int64.TryParse(fields[6], out deadline) || deadline <= 0 || deadline > DateTime.MaxValue.Ticks)
                throw new InvalidDataException("Dati di ripristino non validi.");
            if (expireBefore.HasValue && deadline > expireBefore.Value.Ticks) return;
        }
        else if (expireBefore.HasValue) return;
        Process process;
        try { process = Process.GetProcessById(id); }
        catch (ArgumentException) { File.Delete(journal); return; }
        using (process)
        {
            try
            {
                if (!process.HasExited && process.StartTime.ToFileTimeUtc() == start &&
                    String.Equals(process.MainModule.FileName, fields[2], StringComparison.OrdinalIgnoreCase) &&
                    !IsProtectedName(process.ProcessName))
                {
                    IntPtr processHandle = process.Handle;
                    if (processHandle != IntPtr.Zero && !process.HasExited &&
                        process.PriorityClass == ProcessPriorityClass.BelowNormal)
                        process.PriorityClass = ProcessPriorityClass.Normal;
                }
            }
            catch (InvalidOperationException) { if (!process.HasExited) throw; }
        }
        File.Delete(journal);
    }
}

internal sealed class PowerPlanAction
{
    public static readonly Guid Performance = new Guid("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
    private readonly string journal;
    public bool Pending { get { return File.Exists(journal); } }

    public PowerPlanAction(string folder) { journal = Path.Combine(folder, "power.pending"); }

    [DllImport("powrprof.dll")]
    private static extern uint PowerGetActiveScheme(IntPtr key, out IntPtr scheme);
    [DllImport("powrprof.dll")]
    private static extern uint PowerSetActiveScheme(IntPtr key, ref Guid scheme);
    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);

    public static Guid Read()
    {
        IntPtr pointer;
        uint error = PowerGetActiveScheme(IntPtr.Zero, out pointer);
        if (error != 0) throw new System.ComponentModel.Win32Exception((int)error);
        try { return (Guid)Marshal.PtrToStructure(pointer, typeof(Guid)); }
        finally { LocalFree(pointer); }
    }

    private static void Write(Guid plan)
    {
        uint error = PowerSetActiveScheme(IntPtr.Zero, ref plan);
        if (error != 0) throw new System.ComponentModel.Win32Exception((int)error);
        if (Read() != plan) throw new IOException("Windows non ha confermato il piano energetico richiesto.");
    }

    private void Locked(Action action)
    {
        using (var mutex = new Mutex(false, @"Local\Matrix.Optimizer.PowerUndo"))
        {
            bool entered;
            try { entered = mutex.WaitOne(3000); }
            catch (AbandonedMutexException) { entered = true; }
            if (!entered) throw new IOException("Ripristino del piano energetico occupato.");
            try { action(); }
            finally { mutex.ReleaseMutex(); }
        }
    }

    public void Apply()
    {
        Locked(delegate
        {
            if (Pending) throw new InvalidOperationException("Piano energetico già gestito.");
            Guid original = Read();
            if (original == Performance) return;
            string record = String.Join("\n", original.ToString("D"), Performance.ToString("D"),
                DateTime.UtcNow.AddSeconds(15).Ticks.ToString(CultureInfo.InvariantCulture), Guid.NewGuid().ToString("D"));
            Directory.CreateDirectory(Path.GetDirectoryName(journal));
            string temporary = journal + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                byte[] bytes = System.Text.Encoding.UTF8.GetBytes(record);
                using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                {
                    file.Write(bytes, 0, bytes.Length);
                    file.Flush(true);
                }
                File.Move(temporary, journal);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            if (Read() != original) { File.Delete(journal); throw new IOException("Il piano è cambiato prima dell'intervento."); }
            Write(Performance);
        });
    }

    public void Restore() { Locked(delegate { RestoreCore(null); }); }
    public void RestoreExpired(DateTime now) { Locked(delegate { RestoreCore(now); }); }

    private void RestoreCore(DateTime? now)
    {
        if (!Pending) return;
        string[] fields = File.ReadAllLines(journal);
        Guid original, applied, session;
        long expires;
        if (fields.Length != 4 || !Guid.TryParseExact(fields[0], "D", out original) || original == Guid.Empty ||
            !Guid.TryParseExact(fields[1], "D", out applied) || applied != Performance ||
            !Int64.TryParse(fields[2], out expires) || expires <= 0 || expires > DateTime.MaxValue.Ticks ||
            !Guid.TryParseExact(fields[3], "D", out session)) throw new InvalidDataException("Journal del piano non valido.");
        if (now.HasValue && now.Value.Ticks < expires) return;
        if (Read() == applied) Write(original);
        File.Delete(journal);
    }
}

internal sealed class WindowAction
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Placement
    {
        public int Length, Flags, Show,
            MinX, MinY, MaxX, MaxY, Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll")] private static extern bool GetWindowPlacement(IntPtr window, ref Placement value);
    [DllImport("user32.dll")] private static extern bool SetWindowPlacement(IntPtr window, ref Placement value);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint owner);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, UIntPtr command, IntPtr argument,
        uint flags, uint milliseconds, out UIntPtr result);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool SetProp(IntPtr window, string key, IntPtr value);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetProp(IntPtr window, string key);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr RemoveProp(IntPtr window, string key);

    private readonly string journal;
    public WindowAction(string folder) { journal = Path.Combine(folder, "window.pending"); }
    public bool Pending { get { return File.Exists(journal); } }

    private void Locked(Action action)
    {
        using (var mutex = new Mutex(false, @"Local\Matrix.Optimizer.WindowUndo"))
        {
            bool entered;
            try { entered = mutex.WaitOne(3000); }
            catch (AbandonedMutexException) { entered = true; }
            if (!entered) throw new IOException("Ripristino finestra occupato.");
            try { action(); }
            finally { mutex.ReleaseMutex(); }
        }
    }

    private static bool Owns(IntPtr window, int id)
    {
        uint actual;
        return GetWindowThreadProcessId(window, out actual) != 0 && actual == id;
    }

    public void Apply(Process process)
    {
        Locked(delegate
        {
            if (Pending) throw new InvalidOperationException("Finestra già gestita.");
            process.Refresh();
            if (process.HasExited || process.SessionId == 0 || PriorityAction.IsProtectedName(process.ProcessName))
                throw new InvalidOperationException("Processo non gestibile.");
            string file = process.MainModule.FileName;
            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows).TrimEnd('\\') + "\\";
            if (file.StartsWith(windows, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Finestra di sistema esclusa.");
            IntPtr window = process.MainWindowHandle;
            var entry = new Placement { Length = Marshal.SizeOf(typeof(Placement)) };
            if (window == IntPtr.Zero || !Owns(window, process.Id) || !IsWindowVisible(window) ||
                IsIconic(window) || !GetWindowPlacement(window, ref entry)) return;
            if (entry.Show != 1 && entry.Show != 3 && entry.Show != 9) return;

            string mark = "Matrix.Optimizer." + Guid.NewGuid().ToString("N");
            string record = String.Join("\n", file, mark,
                String.Join(",", Array.ConvertAll(new long[] { process.Id, process.StartTime.ToFileTimeUtc(),
                    window.ToInt64(), entry.Flags, entry.Show, entry.MinX, entry.MinY, entry.MaxX, entry.MaxY,
                    entry.Left, entry.Top, entry.Right, entry.Bottom }, x => x.ToString(CultureInfo.InvariantCulture))));
            Directory.CreateDirectory(Path.GetDirectoryName(journal));
            string temporary = journal + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                byte[] bytes = System.Text.Encoding.UTF8.GetBytes(record);
                using (var fileStream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                {
                    fileStream.Write(bytes, 0, bytes.Length);
                    fileStream.Flush(true);
                }
                File.Move(temporary, journal);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            if (process.HasExited || !Owns(window, process.Id) || !SetProp(window, mark, new IntPtr(1)))
            {
                File.Delete(journal);
                throw new IOException("Impossibile confermare l'identità della finestra.");
            }
            UIntPtr result;
            if (SendMessageTimeout(window, 0x0112, new UIntPtr(0xf020), IntPtr.Zero, 0x0002, 500, out result) == IntPtr.Zero)
                throw new IOException("Risposta dalla finestra non confermata. Registro conservato per il ripristino.");
            if (!IsIconic(window))
            {
                RemoveProp(window, mark);
                File.Delete(journal);
                throw new IOException("La finestra non si è minimizzata.");
            }
        });
    }

    public void Restore()
    {
        Locked(delegate
        {
            if (!Pending) return;
            string[] lines = File.ReadAllLines(journal);
            Guid mark;
            if (lines.Length != 3 || !Path.IsPathRooted(lines[0]) ||
                !lines[1].StartsWith("Matrix.Optimizer.", StringComparison.Ordinal) ||
                !Guid.TryParseExact(lines[1].Substring("Matrix.Optimizer.".Length), "N", out mark))
                throw new InvalidDataException("Journal finestra non valido.");
            string[] fields = lines[2].Split(',');
            long[] values = new long[13];
            if (fields.Length != values.Length) throw new InvalidDataException("Posizione finestra non valida.");
            for (int i = 0; i < values.Length; i++)
                if (!Int64.TryParse(fields[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out values[i]))
                    throw new InvalidDataException("Posizione finestra non valida.");
            if (values[0] <= 0 || values[0] > Int32.MaxValue || values[2] == 0 ||
                (values[4] != 1 && values[4] != 3 && values[4] != 9))
                throw new InvalidDataException("Identità finestra non valida.");

            int id = (int)values[0];
            var handle = new IntPtr(values[2]);
            if (Owns(handle, id) && GetProp(handle, lines[1]) == new IntPtr(1))
            {
                Process process = null;
                try { process = Process.GetProcessById(id); }
                catch (ArgumentException) { }
                using (process)
                {
                    if (process != null && !process.HasExited && process.StartTime.ToFileTimeUtc() == values[1] &&
                        String.Equals(process.MainModule.FileName, lines[0], StringComparison.OrdinalIgnoreCase))
                    {
                        if (IsIconic(handle))
                        {
                            var entry = new Placement { Length = Marshal.SizeOf(typeof(Placement)), Flags = checked((int)values[3]),
                                Show = values[4] == 3 ? 3 : 4, MinX = checked((int)values[5]), MinY = checked((int)values[6]),
                                MaxX = checked((int)values[7]), MaxY = checked((int)values[8]), Left = checked((int)values[9]),
                                Top = checked((int)values[10]), Right = checked((int)values[11]), Bottom = checked((int)values[12]) };
                            if (!SetWindowPlacement(handle, ref entry)) throw new IOException("Posizione originale non ripristinata.");
                        }
                        RemoveProp(handle, lines[1]);
                    }
                }
            }
            File.Delete(journal);
        });
    }
}

internal sealed class RelaunchAction
{
    private readonly string journal;
    public RelaunchAction(string folder) { journal = Path.Combine(folder, "relaunch.pending"); }
    public bool Pending { get { return File.Exists(journal); } }

    private void Locked(Action operation)
    {
        using (var mutex = new Mutex(false, @"Local\Matrix.Optimizer.RelaunchUndo"))
        {
            bool taken;
            try { taken = mutex.WaitOne(3000); }
            catch (AbandonedMutexException) { taken = true; }
            if (!taken) throw new IOException("Registro di riapertura occupato.");
            try { operation(); }
            finally { mutex.ReleaseMutex(); }
        }
    }

    public static bool Eligible(string file, bool demo)
    {
        if (String.IsNullOrWhiteSpace(file) || file.Length < 3 || !Char.IsLetter(file[0]) ||
            file[1] != ':' || (file[2] != '\\' && file[2] != '/') || !File.Exists(file) ||
            !String.Equals(Path.GetExtension(file), ".exe", StringComparison.OrdinalIgnoreCase)) return false;
        using (var identity = System.Security.Principal.WindowsIdentity.GetCurrent())
            if (new System.Security.Principal.WindowsPrincipal(identity).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator))
                return false;
        string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows).TrimEnd('\\') + "\\";
        if (file.StartsWith(windows, StringComparison.OrdinalIgnoreCase)) return false;
        string name = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
        if (PriorityAction.IsProtectedName(name) || name == "msedge" || name == "chrome" ||
            name == "firefox" || name == "opera" || name == "brave" || name == "vivaldi") return false;
        bool same = ExecutableIdentity.Matches(file, Application.ExecutablePath);
        return demo == same;
    }

    public bool Apply(Process process, bool demo)
    {
        bool sent = false;
        Locked(delegate
        {
            if (Pending) throw new InvalidOperationException("Riapertura precedente in attesa.");
            process.Refresh();
            if (process.HasExited || process.SessionId == 0 || process.MainWindowHandle == IntPtr.Zero) return;
            string file = process.MainModule.FileName;
            if (!Eligible(file, demo)) throw new InvalidOperationException("Questa app non può essere chiusa dal boost.");
            string record = String.Join("\n", file, demo ? "demo" : "standard",
                process.Id.ToString(CultureInfo.InvariantCulture),
                process.StartTime.ToFileTimeUtc().ToString(CultureInfo.InvariantCulture));
            Directory.CreateDirectory(Path.GetDirectoryName(journal));
            string pending = journal + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                byte[] bytes = System.Text.Encoding.UTF8.GetBytes(record);
                using (var stream = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }
                File.Move(pending, journal);
            }
            finally { if (File.Exists(pending)) File.Delete(pending); }
            if (process.HasExited || !process.CloseMainWindow())
            {
                File.Delete(journal);
                return;
            }
            sent = true;
        });
        return sent;
    }

    public bool Restore()
    {
        bool relaunched = false;
        Locked(delegate
        {
            if (!Pending) return;
            string[] fields = File.ReadAllLines(journal);
            int originalPid;
            long originalStart;
            if (fields.Length != 4 || (fields[1] != "demo" && fields[1] != "standard") ||
                !Int32.TryParse(fields[2], out originalPid) || originalPid <= 0 ||
                !Int64.TryParse(fields[3], out originalStart) || originalStart <= 0 ||
                !Eligible(fields[0], fields[1] == "demo"))
                throw new InvalidDataException("Il journal di riapertura non è valido o l'eseguibile non è più disponibile.");

            using (Process existing = Optimizer.FindGame(fields[0]))
            {
                if (existing != null)
                {
                    File.Delete(journal);
                    return;
                }
            }
            using (Process launched = Process.Start(new ProcessStartInfo(fields[0], fields[1] == "demo" ? "--demo-worker" : "") {
                UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(fields[0])
            }))
            {
                if (launched == null) throw new IOException("L'app secondaria non è stata riaperta.");
                relaunched = true;
            }
            File.Delete(journal);
        });
        return relaunched;
    }
}

internal sealed class Telemetry
{
    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length, Load;
        public ulong Total, Available, PageTotal, PageFree, VirtualTotal, VirtualFree, ExtendedFree;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PerformanceInfo
    {
        public uint Size;
        public UIntPtr CommitTotal, CommitLimit, CommitPeak, PhysicalTotal, PhysicalAvailable, SystemCache,
            KernelTotal, KernelPaged, KernelNonpaged, PageSize;
        public uint Handles, Processes, Threads;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
    [DllImport("psapi.dll", SetLastError = true)]
    private static extern bool GetPerformanceInfo(out PerformanceInfo info, uint size);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out long idle, out long kernel, out long user);

    public ulong Total, Available, Commit, CommitLimit, Cache;
    public long Idle, Cpu;

    public static Telemetry Read()
    {
        var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf(typeof(MemoryStatus)) };
        PerformanceInfo perf;
        long idle, kernel, user;
        if (!GlobalMemoryStatusEx(ref memory) ||
            !GetPerformanceInfo(out perf, (uint)Marshal.SizeOf(typeof(PerformanceInfo))) ||
            !GetSystemTimes(out idle, out kernel, out user))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        ulong page = perf.PageSize.ToUInt64();
        return new Telemetry {
            Total = memory.Total, Available = memory.Available, Commit = perf.CommitTotal.ToUInt64() * page,
            CommitLimit = perf.CommitLimit.ToUInt64() * page, Cache = perf.SystemCache.ToUInt64() * page,
            Idle = idle, Cpu = kernel + user
        };
    }
}

internal sealed class IoCounters : IDisposable
{
    [StructLayout(LayoutKind.Explicit, Size = 16)]
    private struct CounterValue
    {
        [FieldOffset(0)] public uint Status;
        [FieldOffset(8)] public double Value;
    }

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhOpenQuery(string source, IntPtr userData, out IntPtr query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhAddEnglishCounter(IntPtr query, string path, IntPtr userData, out IntPtr counter);
    [DllImport("pdh.dll")]
    private static extern uint PdhCollectQueryData(IntPtr query);
    [DllImport("pdh.dll")]
    private static extern uint PdhGetFormattedCounterValue(IntPtr counter, uint format, IntPtr type, out CounterValue value);
    [DllImport("pdh.dll")]
    private static extern uint PdhCloseQuery(IntPtr query);

    private IntPtr query, disk, pageReads;

    public IoCounters()
    {
        if (PdhOpenQuery(null, IntPtr.Zero, out query) != 0) return;
        if (PdhAddEnglishCounter(query, @"\PhysicalDisk(_Total)\Disk Bytes/sec", IntPtr.Zero, out disk) != 0)
            disk = IntPtr.Zero;
        if (PdhAddEnglishCounter(query, @"\Memory\Page Reads/sec", IntPtr.Zero, out pageReads) != 0)
            pageReads = IntPtr.Zero;
        PdhCollectQueryData(query);
    }

    public string Read()
    {
        if (query == IntPtr.Zero || PdhCollectQueryData(query) != 0) return "Contatori disco e paging non disponibili";
        return "Disco  " + Format(disk, 1048576, "MiB/s") + "     Letture per hard fault  " + Format(pageReads, 1, "/s");
    }

    private static string Format(IntPtr counter, double scale, string unit)
    {
        CounterValue value;
        if (counter == IntPtr.Zero || PdhGetFormattedCounterValue(counter, 512, IntPtr.Zero, out value) != 0 ||
            value.Status > 1 || Double.IsNaN(value.Value) || Double.IsInfinity(value.Value)) return "—";
        return String.Format("{0:0.0} {1}", value.Value / scale, unit);
    }

    public void Dispose() { if (query != IntPtr.Zero) PdhCloseQuery(query); query = IntPtr.Zero; }
}

internal sealed class Dashboard : Form
{
    private const int BoostHotkeyId = 1;
    private const int HotkeyMessage = 0x0312;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr window, int id);

    private readonly PriorityAction action;
    private readonly PowerPlanAction power;
    private readonly WindowAction windowAction;
    private readonly RelaunchAction relaunch;
    private readonly IoCounters io = new IoCounters();
    private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
    private readonly Label metrics = new Label(), status = new Label(), gameLabel = new Label();
    private readonly CheckBox enabled = new CheckBox(), allow = new CheckBox(), shortcut = new CheckBox();
    private bool shortcutRegistered;
    private readonly ComboBox profile = new ComboBox();
    private readonly ResourceTrend trend = new ResourceTrend();
    private Process optional, game, watcher, serviceWorker;
    private EventWaitHandle serviceStop;
    private GameTree gameTree;
    private string gamePath;
    private Telemetry previous;
    private bool waitingAfterRestore;
    private bool allowPower, powerTried, allowWindow, windowTried, priorityTried;
    private bool optionalDemo, relaunchTried;
    private ulong? ramBeforeClose;
    private string relaunchGame;
    private MachineInventory scanned;
    private string scanError;
    private bool loadingPreferences, automaticSession;
    private AutomaticGames automatic = new AutomaticGames(false, new string[0]);
    private int busySamples;
    private Telemetry beforeAction;
    private readonly Stopwatch actionTime = new Stopwatch();

    public Dashboard(PriorityAction action)
    {
        this.action = action;
        power = new PowerPlanAction(action.StateFolder);
        windowAction = new WindowAction(action.StateFolder);
        relaunch = new RelaunchAction(action.StateFolder);
        Text = "Windows Smart Optimizer";
        ClientSize = new Size(700, 632);
        MinimumSize = new Size(710, 672);
        BackColor = Color.FromArgb(23, 27, 35);
        ForeColor = Color.FromArgb(233, 237, 245);
        Font = new Font("Segoe UI", 10);
        StartPosition = FormStartPosition.CenterScreen;

        var title = new Label { Text = "Windows Smart Optimizer", Font = new Font("Segoe UI", 20, FontStyle.Bold) };
        Add(title, 24, 20, 650, 42);
        Add(metrics, 24, 80, 650, 60);
        Add(trend, 24, 144, 650, 24);
        Add(gameLabel, 24, 177, 650, 28);
        Add(Button("Scegli gioco", ChooseGame), 24, 212, 140, 34);
        Add(Button("Avvia app secondaria", StartOptional), 175, 212, 190, 34);
        Add(Button("Ripristina", Restore), 377, 212, 125, 34);
        Add(Button("Processi e servizi", Inventory), 514, 212, 160, 34);
        profile.DropDownStyle = ComboBoxStyle.DropDownList;
        profile.Items.AddRange(new object[] { "Conservativo", "Bilanciato", "Aggressivo" });
        profile.SelectedIndex = 0;
        Add(profile, 24, 262, 145, 28);
        enabled.Text = "Game Booster attivo";
        Add(enabled, 184, 262, 195, 28);
        allow.Text = "Riduci priorità app secondaria";
        Add(allow, 382, 262, 290, 28);
        shortcut.Text = "Usa Ctrl+Alt+F10 per attivare o disattivare il boost";
        Add(shortcut, 24, 300, 650, 28);
        shortcut.CheckedChanged += delegate { UpdateShortcut(); };
        enabled.CheckedChanged += delegate
        {
            if (!enabled.Checked) { StopManagedService(); Recover("Boost spento. Stato originale ripristinato."); }
        };
        allow.CheckedChanged += delegate
        {
            if (!allow.Checked && action.Pending)
            {
                try { action.Restore(); status.Text = "Priorità originale ripristinata."; }
                catch (Exception error) { status.Text = "Ripristino priorità in attesa: " + error.Message; }
            }
        };
        var activate = Button("Attiva Boost", ActivateBoost);
        activate.BackColor = Color.FromArgb(23, 114, 102);
        activate.Font = new Font("Segoe UI", 13, FontStyle.Bold);
        Add(activate, 24, 342, 316, 48);
        Add(Button("Disattiva e ripristina", DeactivateBoost), 352, 342, 322, 48);
        Add(Button("Analisi del PC", ShowInventory), 24, 450, 140, 34);
        Add(Button("Ripristino d'emergenza", Restore), 178, 450, 166, 34);
        Add(Button("Profilo di questo PC", ApplyPcProfile), 356, 450, 166, 34);
        Add(Button("Modalità Esperto", ShowExpert), 534, 450, 140, 34);
        Add(Button("Importa profilo", ImportProfile), 24, 392, 140, 34);
        Add(Button("Esporta profilo", ExportProfile), 175, 392, 140, 34);
        Add(Button("Libreria giochi", ShowGameLibrary), 326, 392, 145, 34);
        Add(Button("Azioni Boost", ShowBoostActions), 482, 392, 192, 34);
        Add(Button("Avvio automatico", ShowAutomaticGames), 24, 494, 158, 34);
        Add(status, 24, 550, 650, 62);
        profile.SelectedIndexChanged += delegate { SavePreferences(); };
        allow.CheckedChanged += delegate { SavePreferences(); };
        gameLabel.Text = "Gioco non configurato";
        status.Text = "Scegli un gioco e avvia un'app secondaria da questa finestra.";
        bool recovering = action.Pending || power.Pending || windowAction.Pending || relaunch.Pending;
        if (recovering) Recover("Intervento precedente ripristinato.");
        try
        {
            automatic = AutomaticGames.Read(AutomaticPath);
            if (recovering && automatic.Enabled)
            {
                automatic = new AutomaticGames(false, automatic.Paths);
                status.Text = "Avvio automatico sospeso dopo il recupero. Riattivalo dalla lista quando vuoi.";
            }
        }
        catch (Exception error) { status.Text = "Avvio automatico disattivato: " + error.Message; }
        timer.Interval = 3000;
        timer.Tick += delegate { Sample(); };
        Shown += delegate { Sample(); timer.Start(); ScanMachine(); };
        FormClosing += delegate {
            timer.Stop();
            ReleaseShortcut();
            StopManagedService();
            Recover("Priorità ripristinata.");
        };
        FormClosed += delegate {
            timer.Dispose();
            io.Dispose();
            if (optional != null) optional.Dispose();
            if (game != null) game.Dispose();
            if (gameTree != null) gameTree.Dispose();
            if (watcher != null) watcher.Dispose();
        };
    }

    private void ApplyPcProfile(object sender, EventArgs args)
    {
        if (!Recover("Stato originale ripristinato.")) return;
        enabled.Checked = false;
        allowPower = false;
        allowWindow = false;
        SetPreferences(new GamePreferences(0, false));
        SavePreferences();
        status.Text = "Profilo PC: Conservativo. Mantiene le impostazioni Windows. I confronti di Minecraft non hanno mostrato un guadagno ripetibile.";
    }

    private void ShowExpert(object sender, EventArgs args)
    {
        using (var dialog = new Form { Text = "Modalità Esperto — valori del PC", Size = new Size(1080, 610), StartPosition = FormStartPosition.CenterParent })
        using (var list = ExpertSettings.CreateView())
        {
            dialog.Controls.Add(list);
            dialog.ShowDialog(this);
        }
    }

    private void ActivateBoost(object sender, EventArgs args)
    {
        if (gamePath == null) { status.Text = "Scegli prima il gioco."; return; }
        if (!Recover("Ripristino precedente verificato.")) return;
        waitingAfterRestore = false;
        powerTried = false;
        windowTried = false;
        priorityTried = false;
        relaunchTried = false;
        enabled.Checked = true;
        status.Text = "Boost attivo. Si applicano il profilo e le autorizzazioni correnti.";
    }

    private void DeactivateBoost(object sender, EventArgs args)
    {
        StopManagedService();
        waitingAfterRestore = true;
        enabled.Checked = false;
        Recover("Boost spento. Priorità ripristinata.");
        if (!action.Pending && beforeAction == null) status.Text = "Boost spento.";
    }

    private void ScanMachine()
    {
        ThreadPool.QueueUserWorkItem(delegate
        {
            MachineInventory snapshot = null;
            string failure = null;
            try { snapshot = MachineInventory.Scan(); }
            catch (Exception error) { failure = error.Message; }
            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    scanned = snapshot;
                    scanError = failure;
                    var known = snapshot == null ? null : snapshot.Games.FindAll(item => item.Executable != null);
                    if (known != null && gamePath == null && known.Count == 1)
                    {
                        gamePath = known[0].Executable;
                        LoadPreferences();
                        gameLabel.Text = "Gioco rilevato: " + Path.GetFileName(gamePath);
                    }
                });
            }
            catch (InvalidOperationException) { } // Dashboard chiusa durante la scansione.
        });
    }

    private void ShowInventory(object sender, EventArgs args)
    {
        if (scanned == null)
        {
            status.Text = scanError ?? "Scansione del PC in corso.";
            return;
        }
        using (var dialog = new Form { Text = "Analisi del PC", Size = new Size(895, 550), StartPosition = FormStartPosition.CenterParent })
        using (var tabs = new TabControl { Dock = DockStyle.Fill })
        {
            scanned.AddTabs(tabs);
            dialog.Controls.Add(tabs);
            dialog.ShowDialog(this);
        }
    }


    private string PreferencesPath { get { return GamePreferences.PathFor(action.StateFolder, gamePath); } }

    private GamePreferences CurrentPreferences()
    {
        return new GamePreferences(profile.SelectedIndex, allow.Checked, allowPower, allowWindow);
    }

    private void SetPreferences(GamePreferences preferences)
    {
        loadingPreferences = true;
        try
        {
            profile.SelectedIndex = preferences.Level;
            allow.Checked = preferences.LowerPriority;
            allowPower = preferences.HighPerformance;
            allowWindow = preferences.Minimize;
        }
        finally { loadingPreferences = false; }
    }

    private void LoadPreferences()
    {
        SetPreferences(new GamePreferences(0, false));
        try { if (File.Exists(PreferencesPath)) SetPreferences(GamePreferences.Read(PreferencesPath)); }
        catch (IOException) { status.Text = "Profilo non leggibile. Impostazioni conservative."; }
        catch (UnauthorizedAccessException) { status.Text = "Profilo non accessibile. Impostazioni conservative."; }
    }

    private void SavePreferences()
    {
        if (loadingPreferences || gamePath == null) return;
        try { CurrentPreferences().Save(PreferencesPath); }
        catch (IOException) { status.Text = "Profilo locale non salvato."; }
        catch (UnauthorizedAccessException) { status.Text = "Profilo locale non accessibile."; }
    }

    private void ImportProfile(object sender, EventArgs args)
    {
        if (gamePath == null) { status.Text = "Scegli prima il gioco."; return; }
        using (var dialog = new OpenFileDialog { Filter = "Profilo Matrix (*.matrixprofile)|*.matrixprofile" })
        {
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                var imported = GamePreferences.Read(dialog.FileName);
                if (!Recover("Ripristino precedente verificato.")) return;
                enabled.Checked = false;
                SetPreferences(imported);
                status.Text = "Profilo importato. Il boost resta spento.";
                SavePreferences();
            }
            catch (Exception error) { status.Text = "Importazione: " + error.Message; }
        }
    }

    private void ExportProfile(object sender, EventArgs args)
    {
        if (gamePath == null) { status.Text = "Scegli prima il gioco."; return; }
        using (var dialog = new SaveFileDialog { Filter = "Profilo Matrix (*.matrixprofile)|*.matrixprofile", AddExtension = true, OverwritePrompt = true })
        {
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            try { CurrentPreferences().Save(dialog.FileName); status.Text = "Profilo esportato."; }
            catch (Exception error) { status.Text = "Esportazione: " + error.Message; }
        }
    }

    private void ShowGameLibrary(object sender, EventArgs args)
    {
        string selected = SelectCatalogExecutable("Libreria giochi", true);
        if (selected != null) SetGame(selected);
    }

    private string SelectCatalogExecutable(string heading, bool gamesOnly)
    {
        var catalog = new System.Collections.Generic.List<GameEntry>();
        using (var dialog = new Form { Text = heading, Size = new Size(650, 515), StartPosition = FormStartPosition.CenterParent })
        using (var list = new ListBox { Dock = DockStyle.Fill })
        using (var top = new Panel { Dock = DockStyle.Top, Height = 57 })
        using (var searchLabel = new Label { Text = "Cerca per nome", Dock = DockStyle.Top })
        using (var search = new TextBox { Dock = DockStyle.Bottom })
        using (var controls = new Panel { Dock = DockStyle.Bottom, Height = 42 })
        using (var select = new Button { Text = "Usa selezione", Dock = DockStyle.Left, Width = 155 })
        using (var browse = new Button { Text = "Scegli un file .exe", Dock = DockStyle.Right, Width = 155 })
        using (var inventoryTimer = new System.Windows.Forms.Timer { Interval = 250 })
        {
            bool manual = false;
            if (scanned == null) searchLabel.Text = (scanError ?? "Inventario in preparazione") + " • puoi scegliere un file";
            Action refresh = delegate
            {
                list.BeginUpdate();
                try
                {
                    list.Items.Clear();
                    foreach (GameEntry item in catalog)
                        if (item.Name.IndexOf(search.Text.Trim(), StringComparison.CurrentCultureIgnoreCase) >= 0) list.Items.Add(item);
                    if (list.Items.Count > 0) list.SelectedIndex = 0;
                }
                finally { list.EndUpdate(); }
                select.Enabled = list.SelectedItem != null;
            };
            Action populate = delegate
            {
                if (scanned == null)
                {
                    if (scanError != null) { searchLabel.Text = scanError; inventoryTimer.Stop(); }
                    return;
                }

                var names = new System.Collections.Generic.HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
                foreach (GameEntry item in scanned.Games)
                    if (names.Add(item.Name)) catalog.Add(item);
                if (!gamesOnly)
                    foreach (GameEntry item in scanned.Installed)
                        if (names.Add(item.Name)) catalog.Add(item);
                catalog.Sort((a, b) => StringComparer.CurrentCultureIgnoreCase.Compare(a.Name, b.Name));
                searchLabel.Text = "Cerca per nome  •  " + catalog.Count + " voci";
                inventoryTimer.Stop();
                refresh();
            };
            inventoryTimer.Tick += delegate { populate(); };
            if (scanned == null) inventoryTimer.Start();
            else populate();
            search.TextChanged += delegate { refresh(); };
            select.Click += delegate { if (list.SelectedItem != null) dialog.DialogResult = DialogResult.OK; };
            list.DoubleClick += delegate { if (list.SelectedItem != null) dialog.DialogResult = DialogResult.OK; };
            browse.Click += delegate { manual = true; dialog.DialogResult = DialogResult.OK; };
            top.Controls.Add(search);
            top.Controls.Add(searchLabel);
            controls.Controls.Add(select);
            controls.Controls.Add(browse);
            dialog.Controls.Add(list);
            dialog.Controls.Add(controls);
            dialog.Controls.Add(top);
            dialog.AcceptButton = select;
            refresh();
            if (dialog.ShowDialog(this) != DialogResult.OK) return null;
            if (manual) return SelectExe("Scegli il file eseguibile");
            var chosen = (GameEntry)list.SelectedItem;
            return chosen.Executable ?? SelectExe("Conferma l'eseguibile di " + chosen.Name, chosen.Folder);
        }
    }

    private void StopManagedService()
    {
        if (serviceStop != null) serviceStop.Set();
    }

    private void StartSearchBoost()
    {
        if (game == null || game.HasExited || !enabled.Checked || profile.SelectedIndex == 0 || waitingAfterRestore)
        {
            MessageBox.Show(this, "Avvia il gioco e attiva il boost con un profilo operativo.", "Windows Search");
            return;
        }
        if (serviceWorker != null && !serviceWorker.HasExited) return;
        if (MessageBox.Show(this, "L'indicizzazione di Windows Search sarà sospesa. Le ricerche possono rallentare. Il servizio tornerà attivo al ripristino o all'uscita dal gioco.",
            "Intervento su Windows Search", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        try
        {
            if (serviceWorker != null) { serviceWorker.Dispose(); serviceWorker = null; }
            if (serviceStop != null) serviceStop.Dispose();
            string token = Guid.NewGuid().ToString("N");
            serviceStop = ServiceBoost.CreateStopSignal(token);
            using (var owner = Process.GetCurrentProcess()) serviceWorker = ServiceBoost.RunElevated(owner, game, token);
        }
        catch (Exception error) { status.Text = "Windows Search: " + error.Message; }
    }

    private void ShowBoostActions(object sender, EventArgs args)
    {
        using (var dialog = new Form { Text = "Azioni Boost", Size = new Size(780, 490), StartPosition = FormStartPosition.CenterParent })
        using (var details = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true })
        using (var powerOption = new CheckBox { Text = "Prova il piano Prestazioni elevate (15 secondi, opt-in)", Dock = DockStyle.Top, Height = 33 })
        using (var windowOption = new CheckBox { Text = "Minimizza e ripristina la finestra secondaria", Dock = DockStyle.Top, Height = 33 })
        using (var restart = new CheckBox { Text = "Chiudi e riapri l'app secondaria in questa sessione", Dock = DockStyle.Top, Height = 33 })
        using (var current = new Label { Dock = DockStyle.Bottom, Height = 42, Padding = new Padding(8) })
        using (var update = new System.Windows.Forms.Timer { Interval = 500 })
        using (var actionOption = new CheckBox { Text = "Consenti riduzione temporanea della priorità", Dock = DockStyle.Top, Height = 33 })
        using (var demo = new Button { Text = "Apri finestra di prova", Dock = DockStyle.Bottom, Height = 35 })
        using (var services = new Panel { Dock = DockStyle.Bottom, Height = 37 })
        using (var searchPause = new Button { Text = "Pausa Windows Search", Width = 205, Dock = DockStyle.Left })
        using (var searchRestore = new Button { Text = "Ripristina servizio", Width = 172, Dock = DockStyle.Right })
        {
            actionOption.Checked = allow.Checked;
            actionOption.CheckedChanged += delegate { allow.Checked = actionOption.Checked; };
            powerOption.Checked = allowPower;
            powerOption.CheckedChanged += delegate
            {
                allowPower = powerOption.Checked;
                SavePreferences();
                if (!allowPower && power.Pending)
                {
                    try { power.Restore(); }
                    catch (Exception error) { status.Text = "Ripristino energia in attesa: " + error.Message; }
                }
            };
            windowOption.Checked = allowWindow;
            windowOption.CheckedChanged += delegate
            {
                allowWindow = windowOption.Checked;
                SavePreferences();
                if (!allowWindow && windowAction.Pending)
                {
                    try { windowAction.Restore(); }
                    catch (Exception error) { status.Text = "Ripristino finestra in attesa: " + error.Message; }
                }
            };
            restart.Checked = relaunchGame != null && String.Equals(relaunchGame, gamePath, StringComparison.OrdinalIgnoreCase);
            restart.CheckedChanged += delegate
            {
                if (!restart.Checked) { relaunchGame = null; if (relaunch.Pending) Recover("Ripristino richiesto."); return; }
                try
                {
                    if (gamePath == null || optional == null || optional.HasExited ||
                        !RelaunchAction.Eligible(optional.MainModule.FileName, optionalDemo))
                        throw new InvalidOperationException("Scegli il gioco e avvia un'app secondaria compatibile, oppure usa la finestra di prova.");
                    if (MessageBox.Show(dialog, "L'app riceverà una richiesta normale di chiusura. Al ripristino verrà aperta una nuova istanza. Salva prima il lavoro: documenti e finestre interne della vecchia istanza potrebbero non tornare.",
                        "Autorizza questa app", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                    { restart.Checked = false; return; }
                    relaunchGame = gamePath;
                }
                catch (Exception error) { restart.Checked = false; MessageBox.Show(dialog, error.Message); }
            };
            details.Columns.Add("Categoria", 144);
            details.Columns.Add("Azione", 250);
            details.Columns.Add("Stato", 280);
            var live = new ListViewItem(new[] { "Processi", "Riduci priorità app secondaria", "In attesa" });
            details.Items.Add(live);
            var windowRow = new ListViewItem(new[] { "Finestre", "Minimizza e ripristina", "Disabilitata" });
            details.Items.Add(windowRow);
            var restartRow = new ListViewItem(new[] { "App secondaria", "Chiudi e riapri dopo il boost", "Disabilitata" });
            details.Items.Add(restartRow);
            var searchRow = new ListViewItem(new[] { "Servizi Windows", "Pausa indicizzazione Windows Search", "Facoltativa" });
            details.Items.Add(searchRow);
            services.Controls.Add(searchPause);
            services.Controls.Add(searchRestore);
            searchPause.Click += delegate { StartSearchBoost(); };
            searchRestore.Click += delegate
            {
                if (serviceWorker != null && !serviceWorker.HasExited) { StopManagedService(); return; }
                if (!File.Exists(Path.Combine(action.StateFolder, "search.pending"))) return;
                try
                {
                    if (serviceWorker != null) serviceWorker.Dispose();
                    serviceWorker = Process.Start(new ProcessStartInfo(Application.ExecutablePath, "--search-recover") {
                        UseShellExecute = true, Verb = "runas"
                    });
                }
                catch (Exception error) { status.Text = "Ripristino servizio: " + error.Message; }
            };
            details.Items.Add(new ListViewItem(new[] { "Terze parti", "Servizi e attività in background", "Solo inventario" }));
            details.Items.Add(new ListViewItem(new[] { "Browser", "Pausa / recupero sessione", "Da collaudare" }));
            details.Items.Add(new ListViewItem(new[] { "Shell", "Explorer", "Recupero non verificato" }));
            var energy = new ListViewItem(new[] { "Energia", "Prestazioni elevate temporaneo", "Disabilitato" });
            details.Items.Add(energy);
            details.Items.Add(new ListViewItem(new[] { "GPU", "Calibrazione", "Solo diagnosi" }));
            details.Items.Add(new ListViewItem(new[] { "Sicurezza", "Defender e firewall", "Protetti" }));

            Action updateStatus = delegate
            {
                string state = !allow.Checked ? "Disabilitata" : !enabled.Checked ? "Boost spento" :
                    profile.SelectedIndex == 0 ? "Conservativo: solo anteprima" :
                    action.Pending ? "Priorità ridotta" : "In attesa delle condizioni";
                live.SubItems[2].Text = state;
                bool workerActive = serviceWorker != null && !serviceWorker.HasExited;
                bool pendingSearch = File.Exists(Path.Combine(action.StateFolder, "search.pending"));
                searchRow.SubItems[2].Text = pendingSearch ? (workerActive ? "Intervento o ripristino in corso" : "Ripristino richiesto") :
                    workerActive ? "Verifica in corso" : "Facoltativa";
                if (pendingSearch && workerActive)
                {
                    try
                    {
                        using (var controller = new System.ServiceProcess.ServiceController(ServiceBoost.Search))
                            if (controller.Status == System.ServiceProcess.ServiceControllerStatus.Stopped)
                                searchRow.SubItems[2].Text = "Arrestato · ripristino disponibile";
                    }
                    catch (InvalidOperationException) { }
                    catch (System.ComponentModel.Win32Exception) { }
                }
                searchPause.Enabled = !workerActive && !pendingSearch;
                searchRestore.Enabled = workerActive || pendingSearch;
                restartRow.SubItems[2].Text = relaunch.Pending ?
                    (optional != null && optional.HasExited ? "App chiusa; riapertura in attesa" : "Chiusura richiesta; app ancora attiva") :
                    relaunchGame == null ? "Disabilitata" : "Autorizzata per questa sessione";
                windowRow.SubItems[2].Text = windowAction.Pending ? "Richiesta inviata; ripristino in attesa" :
                    !allowWindow ? "Disabilitata" : "Pronta con una finestra secondaria";
                energy.SubItems[2].Text = power.Pending ? "Piano applicato, ripristino in attesa" :
                    !allowPower ? "Disabilitato" : powerTried ? "Prova conclusa o piano già attivo" : "Pronto per la prova";
                if (optional == null || optional.HasExited)
                {
                    current.Text = "Apri una finestra di prova per vedere la priorità cambiare e tornare al valore originale.";
                    return;
                }
                try
                {
                    optional.Refresh();
                    current.Text = optional.ProcessName + "  •  Priorità Windows: " + optional.PriorityClass;
                }
                catch (InvalidOperationException) { current.Text = "App secondaria terminata."; }
                catch (System.ComponentModel.Win32Exception) { current.Text = "Priorità attuale non leggibile."; }
            };
            update.Tick += delegate { updateStatus(); };
            updateStatus();
            update.Start();
            demo.Click += delegate {
                if (optional != null && !optional.HasExited)
                {
                    MessageBox.Show(dialog, "C'è già un'app secondaria. Chiudila prima di avviare la prova.");
                    return;
                }
                if (!Recover("Pronto per la prova.")) return;
                if (optional != null) { optional.Dispose(); optional = null; }
                try
                {
                    optional = Process.Start(new ProcessStartInfo(Application.ExecutablePath, "--demo-worker") { UseShellExecute = false });
                    optionalDemo = true;
                    relaunchGame = null;
                    status.Text = "Finestra di prova avviata. Con Minecraft attivo, consenti l'intervento e scegli il profilo.";
                    dialog.Close();
                }
                catch (Exception error) { MessageBox.Show(dialog, error.Message); }
            };
            dialog.Controls.Add(details);
            dialog.Controls.Add(current);
            dialog.Controls.Add(services);
            dialog.Controls.Add(demo);
            dialog.Controls.Add(restart);
            dialog.Controls.Add(windowOption);
            dialog.Controls.Add(powerOption);
            dialog.Controls.Add(actionOption);
            dialog.ShowDialog(this);
        }
    }

    private string AutomaticPath { get { return Path.Combine(action.StateFolder, "automatic.games"); } }

    private void ShowAutomaticGames(object sender, EventArgs args)
    {
        using (var dialog = new Form { Text = "Avvio automatico del boost", Size = new Size(780, 520), StartPosition = FormStartPosition.CenterParent })
        using (var layout = new TableLayoutPanel { RowCount = 5, ColumnCount = 1, Dock = DockStyle.Fill, Padding = new Padding(10) })
        using (var list = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, IntegralHeight = false })
        using (var master = new CheckBox { Text = "Attiva il boost per i programmi spuntati", Dock = DockStyle.Fill })
        using (var hint = new Label { Text = "Spunta le caselle nella lista. L'ottimizzatore deve restare aperto.", Dock = DockStyle.Fill })
        using (var detail = new Label { Dock = DockStyle.Fill, AutoEllipsis = true })
        using (var controls = new Panel { Dock = DockStyle.Fill })
        using (var more = new Button { Text = "Aggiungi eseguibile", Dock = DockStyle.Left, Width = 155 })
        using (var save = new Button { Text = "Salva", Dock = DockStyle.Right, Width = 105 })
        using (var cancel = new Button { Text = "Annulla", Dock = DockStyle.Right, Width = 105, DialogResult = DialogResult.Cancel })
        using (var fillTimer = new System.Windows.Forms.Timer { Interval = 250 })
        {
            master.Checked = automatic.Enabled;
            var rows = new System.Collections.Generic.Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            Action<string, string, bool> add = delegate(string name, string path, bool checkedState)
            {
                if (String.IsNullOrEmpty(path) || ExecutableIdentity.Matches(path, Application.ExecutablePath)) return;
                int index;
                if (rows.TryGetValue(path, out index))
                {
                    if (checkedState) list.SetItemChecked(index, true);
                    return;
                }
                try { new AutomaticGames(false, new[] { path }); }
                catch (ArgumentException) { return; }
                catch (InvalidDataException) { return; }
                rows.Add(path, list.Items.Add(new GameEntry(name, null, path), checkedState));
            };

            foreach (string path in automatic.Paths)
            {
                var known = scanned == null ? null : scanned.Games.Find(item => String.Equals(item.Executable, path, StringComparison.OrdinalIgnoreCase));
                add(known == null ? Path.GetFileName(path) : known.Name, path, true);
            }
            Action fill = delegate
            {
                if (scanned == null)
                {
                    hint.Text = scanError == null ? "Scansione in corso. Puoi già aggiungere un eseguibile." : "Scansione non riuscita. Puoi aggiungere un eseguibile.";
                    if (scanError != null) fillTimer.Stop();
                    return;
                }
                foreach (GameEntry item in scanned.Games) add(item.Name, item.Executable, false);
                foreach (GameEntry item in scanned.Installed) add(item.Name, item.Executable, false);
                hint.Text = list.Items.Count == 0 ? "Nessun eseguibile trovato. Usa Aggiungi eseguibile." :
                    list.Items.Count + " programmi. Clicca la casella a sinistra del nome.";
                fillTimer.Stop();
            };
            fillTimer.Tick += delegate { fill(); };
            fill();
            if (scanned == null && scanError == null) fillTimer.Start();
            list.SelectedIndexChanged += delegate
            {
                var selected = list.SelectedItem as GameEntry;
                detail.Text = selected == null ? "" : selected.Executable;
            };
            more.Click += delegate
            {
                string path = SelectCatalogExecutable("Aggiungi all'avvio automatico", false);
                if (path == null) return;
                add(Path.GetFileName(path), path, true);
                int index;
                if (rows.TryGetValue(path, out index)) list.SelectedIndex = index;
                else MessageBox.Show(dialog, "Questo eseguibile non può essere usato per l'avvio automatico.");
            };
            save.Click += delegate
            {
                try
                {
                    var selected = new System.Collections.Generic.List<string>();
                    foreach (GameEntry row in list.CheckedItems) selected.Add(row.Executable);
                    var next = new AutomaticGames(master.Checked, selected);
                    if (next.Enabled && next.Paths.Count == 0) throw new InvalidDataException("Spunta almeno un programma.");
                    if (automaticSession && (!next.Enabled || !next.Paths.Exists(path =>
                        String.Equals(path, gamePath, StringComparison.OrdinalIgnoreCase))))
                    {
                        DeactivateBoost(this, EventArgs.Empty);
                        if (action.Pending || power.Pending || windowAction.Pending || relaunch.Pending) return;
                    }
                    next.Save(AutomaticPath);
                    automatic = next;
                    status.Text = next.Enabled ? "Avvio automatico pronto per " + next.Paths.Count + " programmi." : "Avvio automatico disattivato.";
                    dialog.DialogResult = DialogResult.OK;
                }
                catch (Exception error) { MessageBox.Show(dialog, error.Message); }
            };
            controls.Controls.Add(more);
            controls.Controls.Add(cancel);
            controls.Controls.Add(save);
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            layout.Controls.Add(master, 0, 0);
            layout.Controls.Add(hint, 0, 1);
            layout.Controls.Add(list, 0, 2);
            layout.Controls.Add(detail, 0, 3);
            layout.Controls.Add(controls, 0, 4);
            dialog.Controls.Add(layout);
            dialog.AcceptButton = save;
            dialog.CancelButton = cancel;
            dialog.ShowDialog(this);
        }
    }

    private void UpdateShortcut()
    {
        if (!shortcut.Checked) { ReleaseShortcut(); return; }
        if (shortcutRegistered) return;
        const uint altControlNoRepeat = 0x4003;
        shortcutRegistered = RegisterHotKey(Handle, BoostHotkeyId, altControlNoRepeat, (uint)Keys.F10);
        if (!shortcutRegistered)
        {
            shortcut.Checked = false;
            status.Text = "Ctrl+Alt+F10 non disponibile. Usa la spunta Game Booster.";
        }
    }

    private void ReleaseShortcut()
    {
        if (!shortcutRegistered) return;
        UnregisterHotKey(Handle, BoostHotkeyId);
        shortcutRegistered = false;
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == HotkeyMessage && message.WParam.ToInt64() == BoostHotkeyId && shortcutRegistered)
        {
            if (game == null || (game.HasExited && gameTree == null))
            {
                status.Text = "Scorciatoia ignorata: nessun gioco configurato è in esecuzione.";
                return;
            }
            if (enabled.Checked)
            {
                enabled.Checked = false;
                if (!action.Pending) status.Text = "Game Booster spento.";
            }
            else
            {
                waitingAfterRestore = false;
                enabled.Checked = true;
                status.Text = "Game Booster attivo per il gioco. Si applicano il profilo e le spunte correnti.";
            }
            return;
        }
        base.WndProc(ref message);
    }

    private void Add(Control control, int x, int y, int width, int height)
    {
        control.SetBounds(x, y, width, height);
        Controls.Add(control);
    }

    private static Button Button(string text, EventHandler handler)
    {
        var button = new Button { Text = text, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(42, 52, 70) };
        button.Click += handler;
        return button;
    }

    private string SelectExe(string title, string folder = null)
    {
        using (var picker = new OpenFileDialog {
            Filter = "Eseguibili Windows (*.exe)|*.exe", Title = title, InitialDirectory = folder ?? String.Empty
        })
            return picker.ShowDialog(this) == DialogResult.OK ? Path.GetFullPath(picker.FileName) : null;
    }

    private void ChooseGame(object sender, EventArgs args)
    {
        string selected = SelectCatalogExecutable("Scegli gioco o applicazione", false);
        if (selected != null) SetGame(selected);
    }

    private void SetGame(string selected)
    {
        if (optional != null && !optional.HasExited &&
            ExecutableIdentity.Matches(optional.MainModule.FileName, selected))
        {
            status.Text = "Il gioco deve essere diverso dall'app secondaria.";
            return;
        }
        StopManagedService();
        if (!Recover("Gioco configurato.")) return;
        if (game != null) { game.Dispose(); game = null; }
        if (gameTree != null) { gameTree.Dispose(); gameTree = null; }
        enabled.Checked = false;
        automaticSession = false;
        waitingAfterRestore = false;
        gamePath = selected;
        LoadPreferences();
        gameLabel.Text = "In attesa di " + Path.GetFileName(gamePath);
    }

    private void StartOptional(object sender, EventArgs args)
    {
        if (optional != null && !optional.HasExited) { status.Text = "L'app secondaria è già attiva."; return; }
        string selected = SelectCatalogExecutable("Avvia app secondaria", false);
        if (selected == null) return;
        string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows).TrimEnd('\\') + "\\";
        if (selected.StartsWith(windows, StringComparison.OrdinalIgnoreCase) ||
            (gamePath != null && ExecutableIdentity.Matches(selected, gamePath)) ||
            ExecutableIdentity.Matches(selected, Application.ExecutablePath))
        {
            status.Text = "Scegli un'app utente distinta dal gioco e dall'ottimizzatore.";
            return;
        }
        try
        {
            if (!Recover("App secondaria pronta.")) return;
            if (optional != null) { optional.Dispose(); optional = null; }
            optionalDemo = false;
            relaunchGame = null;
            optional = Process.Start(new ProcessStartInfo(selected) {
                UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(selected)
            });
            status.Text = "App secondaria avviata: " + Path.GetFileName(selected);
        }
        catch (Exception error) { status.Text = error.Message; }
    }

    private void Inventory(object sender, EventArgs args)
    {
        using (var dialog = new Form { Text = "Processi e servizi", Size = new Size(920, 580), StartPosition = FormStartPosition.CenterParent })
        using (var tabs = new TabControl { Dock = DockStyle.Fill })
        {
            dialog.Controls.Add(tabs);
            var processes = new TabPage("Processi");
            var services = new TabPage("Servizi");
            tabs.TabPages.Add(processes);
            tabs.TabPages.Add(services);
            var list = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true };
            processes.Controls.Add(list);
            list.Columns.Add("Nome", 220);
            list.Columns.Add("PID", 65);
            list.Columns.Add("Categoria", 188);
            list.Columns.Add("RAM residente (MiB)", 168);
            list.Columns.Add("Privata impegnata (MiB)", 190);
            string root = Environment.GetFolderPath(Environment.SpecialFolder.Windows).TrimEnd('\\') + "\\";
            list.BeginUpdate();
            try
            {
                foreach (Process process in Process.GetProcesses())
                {
                    using (process)
                    {
                        try
                        {
                            string category = "Da valutare";
                            if (game != null && process.Id == game.Id) category = "Gioco";
                            else if (gameTree != null && gameTree.Contains(process.Id)) category = "Figlio del gioco";
                            else if (optional != null && process.Id == optional.Id) category = "App secondaria avviata";
                            else if (process.Id == 4 || process.SessionId == 0) category = "Sistema o servizio";
                            else
                            {
                                try
                                {
                                    if (process.MainModule.FileName.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                                        category = "Percorso di Windows";
                                    else category = "Processo utente";
                                }
                                catch (System.ComponentModel.Win32Exception) { }
                            }
                            list.Items.Add(new ListViewItem(new[] { process.ProcessName, process.Id.ToString(), category,
                                (process.WorkingSet64 >> 20).ToString(), (process.PrivateMemorySize64 >> 20).ToString() }));
                        }
                        catch (InvalidOperationException) { }
                        catch (System.ComponentModel.Win32Exception)
                        {
                            list.Items.Add(new ListViewItem(new[] { process.Id.ToString(), process.Id.ToString(), "Accesso limitato", "—", "—" }));
                        }
                    }
                }
            }
            finally { list.EndUpdate(); }

            var serviceList = new ListView { Dock = DockStyle.Fill, View = View.Details };
            services.Controls.Add(serviceList);
            serviceList.Columns.Add("Nome", 640);
            serviceList.Columns.Add("Stato", 155);
            try
            {
                foreach (var service in System.ServiceProcess.ServiceController.GetServices())
                {
                    using (service)
                    {
                        try { serviceList.Items.Add(new ListViewItem(new[] { service.DisplayName, service.Status.ToString() })); }
                        catch (InvalidOperationException) { }
                    }
                }
            }
            catch (System.ComponentModel.Win32Exception)
            {
                serviceList.Items.Add(new ListViewItem("Elenco non accessibile."));
            }
            dialog.ShowDialog(this);
        }
    }

    private void Restore(object sender, EventArgs args)
    {
        StopManagedService();
        waitingAfterRestore = true;
        Recover("Stato originale ripristinato. Pausa fino alla prossima sessione di gioco.");
    }

    private bool Recover(string message)
    {
        if (!action.Pending && !power.Pending && !windowAction.Pending && !relaunch.Pending) return true;
        try
        {
            bool opened = false;
            try { action.Restore(); }
            finally
            {
                try { power.Restore(); }
                finally
                {
                    try { windowAction.Restore(); }
                    finally { opened = relaunch.Restore(); }
                }
            }
            actionTime.Reset();
            ramBeforeClose = null;
            status.Text = opened ? "App secondaria riaperta. " + message : message;
            if (beforeAction != null)
            {
                var after = Telemetry.Read();
                status.Text += String.Format("\nRAM disponibile: {0} → {1} MiB (variazione di sistema).",
                    beforeAction.Available >> 20, after.Available >> 20);
                try { action.Record("ripristinato", beforeAction, after); }
                catch (IOException) { status.Text += "\nRegistro misure non disponibile."; }
                catch (UnauthorizedAccessException) { status.Text += "\nRegistro misure non disponibile."; }
                beforeAction = null;
            }
            return true;
        }
        catch (Exception error) { status.Text = "Ripristino in attesa: " + error.Message; return false; }
    }

    private void EnsureWatcher()
    {
        if (watcher != null && !watcher.HasExited) return;
        if (watcher != null) watcher.Dispose();
        watcher = Optimizer.StartWatcher();
        if (watcher == null || watcher.HasExited) throw new InvalidOperationException("Ripristino automatico non disponibile.");
    }

    private void Sample()
    {
        try
        {
            Telemetry now = Telemetry.Read();
            int load = (int)((now.Total - now.Available) * 100 / now.Total);
            trend.AddSample(Math.Max(0, Math.Min(100, load)));
            string cpu = previous == null || now.Cpu <= previous.Cpu ? "—" :
                String.Format("{0:0}%", 100.0 * (1 - (double)(now.Idle - previous.Idle) / (now.Cpu - previous.Cpu)));
            metrics.Text = String.Format("RAM disponibile  {0} MiB / {1} MiB     CPU  {2}\nMemoria impegnata  {3} / {4} MiB     Cache di sistema  {5} MiB",
                now.Available >> 20, now.Total >> 20, cpu, now.Commit >> 20, now.CommitLimit >> 20, now.Cache >> 20);
            metrics.Text += "\n" + io.Read();
            double cpuLoad = previous == null || now.Cpu <= previous.Cpu ? 0 :
                100 * (1 - (double)(now.Idle - previous.Idle) / (now.Cpu - previous.Cpu));
            busySamples = cpuLoad >= 70 ? Math.Min(2, busySamples + 1) : 0;
            previous = now;

            if (game != null && !(gameTree == null ? !game.HasExited : gameTree.Update(game)))
            {
                StopManagedService();
                if (automaticSession) enabled.Checked = false;
                automaticSession = false;
                game.Dispose();
                game = null;
                if (gameTree != null) { gameTree.Dispose(); gameTree = null; }
                waitingAfterRestore = false;
                powerTried = false;
                windowTried = false;
                priorityTried = false;
                relaunchTried = false;
                relaunchGame = null;
                busySamples = 0;
                if (!Recover("Gioco terminato. Priorità originale ripristinata.")) return;
            }
            if (game == null)
            {
                string detectedPath;
                game = automatic.Detect(out detectedPath);
                if (game != null && optional != null && !optional.HasExited &&
                    ExecutableIdentity.Matches(detectedPath, optional.MainModule.FileName))
                {
                    game.Dispose();
                    game = null;
                    detectedPath = null;
                }
                if (game != null)
                {
                    if (!Recover("Stato originale ripristinato.")) { game.Dispose(); game = null; return; }
                    gamePath = detectedPath;
                    LoadPreferences();
                    waitingAfterRestore = false;
                    automaticSession = true;
                    enabled.Checked = true;
                    status.Text = "Boost automatico: " + Path.GetFileName(gamePath);
                }
                else game = Optimizer.FindGame(gamePath);
                if (game != null)
                {
                    try { gameTree = new GameTree(game); gameTree.Update(game); }
                    catch (System.ComponentModel.Win32Exception) { }
                    catch (InvalidOperationException) { }
                }
            }
            timer.Interval = game == null ? 3000 : 1000;
            if (gamePath != null) gameLabel.Text = (game == null ? "In attesa di " : automaticSession ? "Sessione automatica: " : "Gioco attivo: ") +
                Path.GetFileName(gamePath);

            bool sessionReady = game != null && enabled.Checked && !waitingAfterRestore;
            if (!sessionReady || profile.SelectedIndex == 0) StopManagedService();
            if (!sessionReady) { Recover("Stato originale ripristinato."); return; }
            bool requestedRelaunch = relaunchGame != null && String.Equals(gamePath, relaunchGame, StringComparison.OrdinalIgnoreCase) &&
                profile.SelectedIndex != 0;
            if (requestedRelaunch && !relaunchTried && optional != null && !optional.HasExited && optional.MainWindowHandle != IntPtr.Zero)
            {
                relaunchTried = true;
                action.Restore();
                windowAction.Restore();
                EnsureWatcher();
                if (relaunch.Apply(optional, optionalDemo))
                {
                    ramBeforeClose = now.Available;
                    status.Text = "Chiusura richiesta. L'app sarà riaperta al ripristino se è uscita.";
                }
            }
            if (relaunch.Pending && ramBeforeClose.HasValue && optional != null && optional.HasExited)
            {
                status.Text = String.Format("App chiusa. RAM disponibile di sistema: {0} → {1} MiB. Riapertura al ripristino.",
                    ramBeforeClose.Value >> 20, now.Available >> 20);
                ramBeforeClose = null;
            }
            if (windowAction.Pending && (!allowWindow || profile.SelectedIndex == 0 || watcher == null || watcher.HasExited))
                windowAction.Restore();
            if (!requestedRelaunch && allowWindow && !windowTried && profile.SelectedIndex != 0 && optional != null && !optional.HasExited &&
                optional.MainWindowHandle != IntPtr.Zero)
            {
                windowTried = true;
                EnsureWatcher();
                windowAction.Apply(optional);
                if (windowAction.Pending) status.Text = "Finestra secondaria minimizzata; sarà ripristinata alla fine del boost.";
            }
            if (power.Pending)
            {
                if (profile.SelectedIndex == 0 || !allowPower || watcher == null || watcher.HasExited) power.Restore();
                else power.RestoreExpired(DateTime.UtcNow);
            }
            if (allowPower && !powerTried && profile.SelectedIndex != 0)
            {
                powerTried = true;
                try
                {
                    EnsureWatcher();
                    power.Apply();
                    status.Text = power.Pending ? "Piano Prestazioni elevate applicato per 15 secondi." : "Piano già attivo.";
                }
                catch (Exception error)
                {
                    try { power.Restore(); } catch (Exception) { }
                    status.Text = "Piano energetico: " + error.Message;
                }
            }
            if (requestedRelaunch) return;
            if (!allow.Checked || optional == null || optional.HasExited)
            {
                if (action.Pending) action.Restore();
                return;
            }
            if (profile.SelectedIndex == 0)
            {
                if (!Recover("Priorità ripristinata.")) return;
                status.Text = "Conservativo: simulazione. Ridurrebbe la priorità dell'app secondaria.";
                return;
            }
            if (!action.Pending && profile.SelectedIndex == 1 && busySamples < 2)
            {
                status.Text = "Bilanciato: attende due rilevazioni consecutive con CPU almeno al 70%.";
                return;
            }
            if (actionTime.IsRunning && !action.Pending)
            {
                actionTime.Reset();
                beforeAction = null;
                status.Text = "Priorità ripristinata dal watchdog. Le altre azioni seguono la sessione.";
                return;
            }
            if (action.Pending)
            {
                EnsureWatcher();
                if (actionTime.Elapsed.TotalSeconds >= 15)
                {
                    action.Restore();
                    actionTime.Reset();
                    if (beforeAction != null) action.Record("ripristinato", beforeAction, now);
                    beforeAction = null;
                    status.Text = "Priorità ripristinata dopo 15 secondi. Le altre azioni seguono la sessione.";
                }
                return;
            }
            if (priorityTried) return;
            EnsureWatcher();
            priorityTried = true;
            action.Apply(optional);
            if (action.Pending) actionTime.Restart();
            beforeAction = action.Pending ? now : null;
            status.Text = action.Pending ? "App secondaria a priorità inferiore. Ripristino alla fine del gioco." :
                "Priorità lasciata invariata: l'app secondaria non è a priorità normale.";
        }
        catch (Exception error)
        {
            string reason = error.Message;
            if (action.Pending || beforeAction != null) allow.Checked = false;
            if (Recover("Priorità ripristinata.")) status.Text = "Monitor: " + reason;
        }
    }
}

internal sealed class ResourceTrend : Control
{
    private readonly System.Collections.Generic.Queue<int> samples = new System.Collections.Generic.Queue<int>();

    public ResourceTrend() { DoubleBuffered = true; }

    public void AddSample(int load)
    {
        if (samples.Count == 60) samples.Dequeue();
        samples.Enqueue(load);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Color.FromArgb(38, 44, 56));
        if (samples.Count < 2) return;
        var points = new PointF[samples.Count];
        int i = 0;
        foreach (int value in samples)
        {
            points[i] = new PointF(Width * i / (float)(points.Length - 1), Height - 2 - (Height - 4) * value / 100f);
            i++;
        }
        using (var pen = new Pen(Color.FromArgb(105, 211, 180), 2)) e.Graphics.DrawLines(pen, points);
    }
}
