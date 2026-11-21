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
        if (args.Length > 0 && args[0] == "--test-worker") { Thread.Sleep(60000); return 0; }
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

    private static int Watch(int ownerId, long ownerStart, string state, string mutexName)
    {
        Process owner = null;
        try { owner = Process.GetProcessById(ownerId); }
        catch (ArgumentException) { }
        if (owner != null)
        {
            using (owner)
            {
                try { if (owner.StartTime.ToFileTimeUtc() == ownerStart) owner.WaitForExit(); }
                catch (InvalidOperationException) { }
            }
        }
        using (var mutex = new Mutex(false, mutexName))
        {
            bool acquired;
            try { acquired = mutex.WaitOne(5000); }
            catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) return 1;
            try { new PriorityAction(state).Restore(); return 0; }
            catch (Exception) { return 1; }
            finally { mutex.ReleaseMutex(); }
        }
    }

    public static Process StartWatcher()
    {
        using (var owner = Process.GetCurrentProcess())
            return Process.Start(new ProcessStartInfo(Application.ExecutablePath,
                String.Format(CultureInfo.InvariantCulture, "--watch {0} {1}", owner.Id, owner.StartTime.ToFileTimeUtc())) {
                    UseShellExecute = false, CreateNoWindow = true
                });
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
                    String.Equals(process.MainModule.FileName, path, StringComparison.OrdinalIgnoreCase)) match = process;
            }
            catch (System.ComponentModel.Win32Exception) { }
            catch (InvalidOperationException) { }
            finally { if (match != process) process.Dispose(); }
        }
        return match;
    }

    private static void SelfTest(string directory)
    {
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
                File.WriteAllText(Path.Combine(directory, "result.txt"),
                    "OK: telemetria, rilevamento/uscita gioco simulato, priorità, ripristino, recupero journal e arresto del proprietario.");
            }
            finally
            {
                if (!game.HasExited) game.Kill();
                if (!worker.HasExited) worker.Kill();
            }
        }
    }
}

internal sealed class PriorityAction
{
    private readonly string journal;
    public PriorityAction(string folder) { journal = Path.Combine(folder, "priority.pending"); }
    public bool Pending { get { return File.Exists(journal); } }

    public void Record(string outcome, Telemetry before, Telemetry after)
    {
        string line = String.Format(CultureInfo.InvariantCulture, "{0:O}\t{1}\t{2}\t{3}\t{4}\t{5}",
            DateTime.UtcNow, outcome, before.Available >> 20, after.Available >> 20, before.Commit >> 20, after.Commit >> 20);
        File.AppendAllText(Path.Combine(Path.GetDirectoryName(journal), "misure.tsv"), line + Environment.NewLine);
    }

    public void Apply(Process process)
    {
        if (Pending) throw new InvalidOperationException("Ripristina prima l'intervento precedente.");
        string path = process.MainModule.FileName;
        var original = process.PriorityClass;
        if (original != ProcessPriorityClass.Normal) return;
        string record = String.Join("\n", process.Id.ToString(CultureInfo.InvariantCulture),
            process.StartTime.ToFileTimeUtc().ToString(CultureInfo.InvariantCulture), path);
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

    public void Restore()
    {
        if (!Pending) return;
        var fields = File.ReadAllLines(journal);
        int id;
        long start;
        if (fields.Length != 3 || !Int32.TryParse(fields[0], out id) ||
            !Int64.TryParse(fields[1], out start) || !Path.IsPathRooted(fields[2]))
            throw new InvalidDataException("Registro di ripristino non valido.");
        Process process;
        try { process = Process.GetProcessById(id); }
        catch (ArgumentException) { File.Delete(journal); return; }
        using (process)
        {
            try
            {
                if (!process.HasExited && process.StartTime.ToFileTimeUtc() == start &&
                    String.Equals(process.MainModule.FileName, fields[2], StringComparison.OrdinalIgnoreCase) &&
                    process.PriorityClass == ProcessPriorityClass.BelowNormal)
                    process.PriorityClass = ProcessPriorityClass.Normal;
            }
            catch (InvalidOperationException) { if (!process.HasExited) throw; }
        }
        File.Delete(journal);
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
    private readonly PriorityAction action;
    private readonly IoCounters io = new IoCounters();
    private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
    private readonly Label metrics = new Label(), status = new Label(), gameLabel = new Label();
    private readonly CheckBox enabled = new CheckBox(), allow = new CheckBox();
    private readonly ComboBox profile = new ComboBox();
    private readonly ResourceTrend trend = new ResourceTrend();
    private Process optional, game, watcher;
    private string gamePath;
    private Telemetry previous;
    private bool waitingAfterRestore;
    private int busySamples;
    private Telemetry beforeAction;
    private readonly Stopwatch actionTime = new Stopwatch();

    public Dashboard(PriorityAction action)
    {
        this.action = action;
        Text = "Windows Smart Optimizer";
        ClientSize = new Size(700, 385);
        MinimumSize = new Size(710, 425);
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
        Add(status, 24, 309, 650, 58);
        gameLabel.Text = "Gioco non configurato";
        status.Text = "Scegli un gioco e avvia un'app secondaria da questa finestra.";
        if (action.Pending) Recover("Intervento precedente ripristinato.");
        timer.Interval = 3000;
        timer.Tick += delegate { Sample(); };
        Shown += delegate { Sample(); timer.Start(); };
        FormClosing += delegate {
            timer.Stop();
            Recover("Priorità ripristinata.");
        };
        FormClosed += delegate {
            timer.Dispose();
            io.Dispose();
            if (optional != null) optional.Dispose();
            if (game != null) game.Dispose();
            if (watcher != null) watcher.Dispose();
        };
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

    private string SelectExe(string title)
    {
        using (var picker = new OpenFileDialog { Filter = "Eseguibili Windows (*.exe)|*.exe", Title = title })
            return picker.ShowDialog(this) == DialogResult.OK ? Path.GetFullPath(picker.FileName) : null;
    }

    private void ChooseGame(object sender, EventArgs args)
    {
        string selected = SelectExe("Scegli l'eseguibile del gioco");
        if (selected == null) return;
        if (optional != null && !optional.HasExited &&
            String.Equals(optional.MainModule.FileName, selected, StringComparison.OrdinalIgnoreCase))
        {
            status.Text = "Il gioco deve essere diverso dall'app secondaria.";
            return;
        }
        if (!Recover("Gioco configurato.")) return;
        if (game != null) { game.Dispose(); game = null; }
        gamePath = selected;
        gameLabel.Text = "In attesa di " + Path.GetFileName(gamePath);
    }

    private void StartOptional(object sender, EventArgs args)
    {
        if (optional != null && !optional.HasExited) { status.Text = "L'app secondaria è già attiva."; return; }
        string selected = SelectExe("Avvia un'app secondaria per questa sessione");
        if (selected == null) return;
        string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows).TrimEnd('\\') + "\\";
        if (selected.StartsWith(windows, StringComparison.OrdinalIgnoreCase) ||
            String.Equals(selected, gamePath, StringComparison.OrdinalIgnoreCase) ||
            String.Equals(selected, Application.ExecutablePath, StringComparison.OrdinalIgnoreCase))
        {
            status.Text = "Scegli un'app utente distinta dal gioco e dall'ottimizzatore.";
            return;
        }
        try
        {
            if (!Recover("App secondaria pronta.")) return;
            if (optional != null) { optional.Dispose(); optional = null; }
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
        waitingAfterRestore = true;
        Recover("Priorità ripristinata. Pausa fino alla prossima sessione di gioco.");
    }

    private bool Recover(string message)
    {
        if (!action.Pending) return true;
        try
        {
            action.Restore();
            actionTime.Reset();
            status.Text = message;
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

            if (game != null && game.HasExited)
            {
                game.Dispose();
                game = null;
                waitingAfterRestore = false;
                busySamples = 0;
                if (!Recover("Gioco terminato. Priorità originale ripristinata.")) return;
            }
            if (game == null) game = Optimizer.FindGame(gamePath);
            timer.Interval = game == null ? 3000 : 1000;
            if (gamePath != null) gameLabel.Text = (game == null ? "In attesa di " : "Gioco attivo: ") + Path.GetFileName(gamePath);

            bool canAct = game != null && enabled.Checked && allow.Checked && !waitingAfterRestore &&
                optional != null && !optional.HasExited;
            if (!canAct) { Recover("Priorità originale ripristinata."); return; }
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
            if (watcher == null || watcher.HasExited)
            {
                if (watcher != null) watcher.Dispose();
                watcher = Optimizer.StartWatcher();
                if (watcher == null || watcher.HasExited) throw new InvalidOperationException("Ripristino automatico non disponibile.");
            }
            if (action.Pending)
            {
                if (actionTime.Elapsed.TotalSeconds >= 15)
                {
                    waitingAfterRestore = true;
                    Recover("Intervento concluso dopo 15 secondi. Priorità ripristinata.");
                }
                return;
            }
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
