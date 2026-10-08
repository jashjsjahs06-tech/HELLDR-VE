using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Text;
using System.Windows.Forms;

namespace HellDrive;

public sealed class MainForm : Form
{
    private readonly ConfigStore store = new();
    private readonly BackupManager backups = new();
    private readonly OptimizationLogger logger = new();
    private readonly ProcessOptimizer optimizer = new();
    private readonly AppSettings settings;
    private readonly StatsMonitor stats = new();

    private Process? gameProcess;
    private List<int> suspendedIds = new();
    private string priorityBefore = "Normal";

    private readonly TextBox gamePath = new();
    private readonly ComboBox profile = new();
    private readonly ComboBox preset = new();
    private readonly ComboBox priority = new();
    private readonly TextBox backgroundProcesses = new();
    private readonly CheckBox manageBackground = new();
    private readonly CheckBox backup = new();
    private readonly Button browse = new();
    private readonly Button launch = new();
    private readonly Button restore = new();
    private readonly Button performance = new();
    private readonly Button saveProfile = new();
    private readonly Button newProfile = new();
    private readonly Label status = new();
    private readonly Label cpuValue = new();
    private readonly Label ramValue = new();
    private readonly Label gpuValue = new();
    private readonly Label tempValue = new();
    private readonly Label fpsValue = new();
    private readonly ListBox logList = new();
    private readonly System.Windows.Forms.Timer statsTimer = new() { Interval = 1000 };
    private bool loadingProfile;

    public MainForm()
    {
        settings = store.Load();
        EnsureDefaults();
        Text = "HELLDRIVE // EXTREME GAME PERFORMANCE";
        ClientSize = new Size(1120, 720);
        MinimumSize = new Size(1000, 650);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(8, 8, 10);
        ForeColor = Color.Gainsboro;
        Font = new Font("Segoe UI", 9.5F);
        DoubleBuffered = true;

        BuildUi();
        LoadProfile(settings.LastProfile.Length > 0 ? settings.LastProfile : settings.Profiles[0].Name);
        statsTimer.Tick += (_, _) => UpdateStats();
        FormClosing += (_, _) => { if (gameProcess != null && !gameProcess.HasExited) RestoreActiveChanges(); stats.Dispose(); };
    }

    private void EnsureDefaults()
    {
        if (settings.Profiles.Count > 0) return;
        var p = new GameProfile { Name = "Default", Preset = PresetLevel.Balanced, Priority = "Above Normal" };
        ProfileEngine.ApplyPreset(p, PresetLevel.Balanced);
        settings.Profiles.Add(p);
        store.Save(settings);
    }

    private void BuildUi()
    {
        Controls.Add(new HeaderPanel { Dock = DockStyle.Top, Height = 105 });

        var left = new Panel { Location = new Point(28, 125), Size = new Size(650, 550), BackColor = Color.FromArgb(14, 14, 18) };
        var right = new Panel { Location = new Point(700, 125), Size = new Size(390, 550), BackColor = Color.FromArgb(14, 14, 18) };
        Controls.Add(left); Controls.Add(right);

        AddLabel(left, "GAME EXECUTABLE", 22, 20);
        gamePath.Location = new Point(22, 45); gamePath.Size = new Size(510, 32); StyleTextBox(gamePath); left.Controls.Add(gamePath);
        browse.Text = "SELECT"; browse.Location = new Point(540, 43); browse.Size = new Size(90, 36); StyleButton(browse); browse.Click += Browse_Click; left.Controls.Add(browse);

        AddLabel(left, "GAME PROFILE", 22, 92);
        profile.Location = new Point(22, 117); profile.Size = new Size(200, 32); StyleCombo(profile); profile.SelectedIndexChanged += (_, _) => { if (!loadingProfile && profile.SelectedItem is string n) LoadProfile(n); }; left.Controls.Add(profile);
        newProfile.Text = "+ NEW"; newProfile.Location = new Point(232, 115); newProfile.Size = new Size(90, 36); StyleButton(newProfile); newProfile.Click += (_, _) => CreateProfile(); left.Controls.Add(newProfile);
        saveProfile.Text = "SAVE"; saveProfile.Location = new Point(330, 115); saveProfile.Size = new Size(90, 36); StyleButton(saveProfile); saveProfile.Click += (_, _) => SaveCurrentProfile(); left.Controls.Add(saveProfile);

        AddLabel(left, "PRESET", 22, 166);
        preset.Items.AddRange(Enum.GetNames<PresetLevel>()); preset.Location = new Point(22, 191); preset.Size = new Size(180, 32); StyleCombo(preset); preset.SelectedIndexChanged += (_, _) => { if (Enum.TryParse<PresetLevel>(preset.Text, out var p)) { var gp = CurrentProfile(); if (gp != null) { ProfileEngine.ApplyPreset(gp, p); SyncUi(gp); SaveCurrentProfile(); } } }; left.Controls.Add(preset);
        AddLabel(left, "CPU PRIORITY", 220, 166);
        priority.Items.AddRange(new object[] { "Idle", "Below Normal", "Normal", "Above Normal", "High" }); priority.Location = new Point(220, 191); priority.Size = new Size(180, 32); StyleCombo(priority); left.Controls.Add(priority);

        AddLabel(left, "BACKGROUND PROCESSES (names, comma separated)", 22, 240);
        backgroundProcesses.Location = new Point(22, 265); backgroundProcesses.Size = new Size(378, 32); StyleTextBox(backgroundProcesses); left.Controls.Add(backgroundProcesses);
        manageBackground.Text = "TEMPORARILY SUSPEND SELECTED PROCESSES WHILE GAME RUNS"; manageBackground.AutoSize = true; manageBackground.Location = new Point(22, 310); left.Controls.Add(manageBackground);
        backup.Text = "AUTOMATIC BACKUP BEFORE OPTIMIZATION"; backup.AutoSize = true; backup.Checked = true; backup.Location = new Point(22, 340); left.Controls.Add(backup);

        performance.Text = "⚡ PERFORMANCE MODE"; performance.Location = new Point(22, 382); performance.Size = new Size(608, 45); performance.Font = new Font("Consolas", 11, FontStyle.Bold); StyleButton(performance); performance.Click += Performance_Click; left.Controls.Add(performance);
        launch.Text = "🚀 LAUNCH OPTIMIZED"; launch.Location = new Point(22, 437); launch.Size = new Size(390, 65); launch.Font = new Font("Consolas", 13, FontStyle.Bold); StyleButton(launch); launch.Click += Launch_Click; left.Controls.Add(launch);
        restore.Text = "↩ RESTORE"; restore.Location = new Point(422, 437); restore.Size = new Size(208, 65); restore.Font = new Font("Consolas", 11, FontStyle.Bold); StyleButton(restore); restore.Click += Restore_Click; left.Controls.Add(restore);
        status.Text = "STATUS // READY"; status.Location = new Point(22, 515); status.AutoSize = true; status.ForeColor = Color.FromArgb(235, 55, 55); status.Font = new Font("Consolas", 10, FontStyle.Bold); left.Controls.Add(status);

        AddLabel(right, "LIVE STATS", 22, 20);
        AddStat(right, "FPS", fpsValue, 55); AddStat(right, "CPU", cpuValue, 105); AddStat(right, "RAM", ramValue, 155); AddStat(right, "GPU", gpuValue, 205); AddStat(right, "TEMP", tempValue, 255);
        AddLabel(right, "OPTIMIZATION LOG", 22, 315);
        logList.Location = new Point(22, 342); logList.Size = new Size(346, 175); logList.BackColor = Color.FromArgb(7,7,9); logList.ForeColor = Color.Gainsboro; logList.BorderStyle = BorderStyle.FixedSingle; right.Controls.Add(logList);
        var note = new Label { Text = "FPS/GPU/TEMP show N/A when Windows/game does not expose a generic metric. CPU/RAM are measured directly from the game process.", Location = new Point(22, 525), Size = new Size(345, 65), ForeColor = Color.FromArgb(125,125,130), Font = new Font("Segoe UI", 8.5F) }; right.Controls.Add(note);
    }

    private static void AddLabel(Control parent, string text, int x, int y) => parent.Controls.Add(new Label { Text = text, Location = new Point(x,y), AutoSize = true, ForeColor = Color.FromArgb(235,55,55), Font = new Font("Consolas", 8.5F, FontStyle.Bold) });
    private static void AddStat(Control parent, string name, Label value, int y) { AddLabel(parent, name, 22, y); value.Text = "N/A"; value.Location = new Point(130,y-2); value.AutoSize = true; value.ForeColor = Color.Gainsboro; value.Font = new Font("Consolas", 12, FontStyle.Bold); parent.Controls.Add(value); }
    private static void StyleTextBox(TextBox x) { x.BackColor=Color.FromArgb(22,22,26); x.ForeColor=Color.Gainsboro; x.BorderStyle=BorderStyle.FixedSingle; }
    private static void StyleCombo(ComboBox x) { x.BackColor=Color.FromArgb(22,22,26); x.ForeColor=Color.Gainsboro; x.FlatStyle=FlatStyle.Flat; }
    private static void StyleButton(Button x) { x.BackColor=Color.FromArgb(24,24,28); x.ForeColor=Color.FromArgb(235,55,55); x.FlatStyle=FlatStyle.Flat; x.FlatAppearance.BorderColor=Color.FromArgb(120,35,35); x.Cursor=Cursors.Hand; }

    private GameProfile? CurrentProfile() => profile.SelectedItem is string n ? settings.Profiles.FirstOrDefault(x => x.Name == n) : null;
    private void LoadProfile(string name)
    {
        var p = settings.Profiles.FirstOrDefault(x => x.Name == name) ?? settings.Profiles[0];
        loadingProfile = true;
        try
        {
            profile.Items.Clear();
            profile.Items.AddRange(settings.Profiles.Select(x => x.Name).ToArray());
            profile.SelectedItem = p.Name;
            SyncUi(p);
        }
        finally { loadingProfile = false; }
        settings.LastProfile = p.Name; store.Save(settings); status.Text = $"STATUS // PROFILE: {p.Name.ToUpperInvariant()}";
    }
    private void SyncUi(GameProfile p) { gamePath.Text=p.GamePath; preset.SelectedItem=p.Preset.ToString(); priority.SelectedItem=p.Priority; backgroundProcesses.Text=string.Join(", ",p.BackgroundProcesses); manageBackground.Checked=p.ManageBackground; backup.Checked=p.AutomaticBackup; }
    private void SaveCurrentProfile()
    {
        var p=CurrentProfile(); if(p==null)return; p.GamePath=gamePath.Text.Trim(); p.Priority=priority.Text; p.ManageBackground=manageBackground.Checked; p.AutomaticBackup=backup.Checked; p.BackgroundProcesses=backgroundProcesses.Text.Split(',',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries).ToList(); if(Enum.TryParse<PresetLevel>(preset.Text,out var pr))p.Preset=pr; store.Save(settings); status.Text="STATUS // PROFILE SAVED";
    }
    private void CreateProfile()
    {
        string name = "Profile " + (settings.Profiles.Count + 1); var p=new GameProfile{Name=name}; ProfileEngine.ApplyPreset(p,PresetLevel.Balanced); settings.Profiles.Add(p); store.Save(settings); LoadProfile(name);
    }
    private void Browse_Click(object? s, EventArgs e) { using var d=new OpenFileDialog{Filter="Windows executable (*.exe)|*.exe"}; if(d.ShowDialog()==DialogResult.OK){gamePath.Text=d.FileName;SaveCurrentProfile();} }
    private void Performance_Click(object? s, EventArgs e) { var p=CurrentProfile(); if(p==null)return; ProfileEngine.ApplyPreset(p,PresetLevel.Aggressive); SyncUi(p); SaveCurrentProfile(); status.Text="STATUS // PERFORMANCE MODE ARMED"; }

    private async void Launch_Click(object? sender, EventArgs e)
    {
        if(!File.Exists(gamePath.Text)){MessageBox.Show("Geçerli bir oyun .exe seç.","HELLDRIVE",MessageBoxButtons.OK,MessageBoxIcon.Warning);return;}
        try
        {
            SaveCurrentProfile(); var p=CurrentProfile()!; launch.Enabled=false; performance.Enabled=false; status.Text="STATUS // INITIALIZING...";
            if(p.AutomaticBackup && backup.Checked) backups.Create(p, priority.Text, suspendedIds);
            gameProcess=Process.Start(new ProcessStartInfo{FileName=p.GamePath,WorkingDirectory=Path.GetDirectoryName(p.GamePath)??"",UseShellExecute=true}) ?? throw new InvalidOperationException("Oyun başlatılamadı.");
            await Task.Delay(1000);
            priorityBefore=gameProcess.PriorityClass.ToString(); optimizer.ApplyPriority(gameProcess,p.Priority,logger);
            if(p.ManageBackground) suspendedIds=optimizer.SuspendBackground(p.BackgroundProcesses,gameProcess.Id,logger).Select(x=>x.Id).ToList();
            stats.Attach(gameProcess); statsTimer.Start(); RefreshLog(); status.Text=$"STATUS // HELLDRIVE ACTIVE // {gameProcess.ProcessName}";
            await gameProcess.WaitForExitAsync(); RestoreActiveChanges(); status.Text="STATUS // GAME FINISHED // SYSTEM STABLE";
        }
        catch(Exception ex){MessageBox.Show(ex.Message,"HELLDRIVE ERROR",MessageBoxButtons.OK,MessageBoxIcon.Error);status.Text="STATUS // ERROR";}
        finally{statsTimer.Stop();launch.Enabled=true;performance.Enabled=true;gameProcess?.Dispose();gameProcess=null;}
    }

    private void RestoreActiveChanges()
    {
        try
        {
            if(gameProcess!=null && !gameProcess.HasExited){gameProcess.PriorityClass=Enum.TryParse<ProcessPriorityClass>(priorityBefore,out var old)?old:ProcessPriorityClass.Normal;logger.Add("CPU PRIORITY RESTORE",$"{gameProcess.ProcessName}: {priorityBefore}");}
        }catch{}
        optimizer.Resume(suspendedIds,logger); suspendedIds.Clear(); RefreshLog();
    }
    private void Restore_Click(object? s, EventArgs e)
    {
        if(gameProcess!=null && !gameProcess.HasExited){RestoreActiveChanges();status.Text="STATUS // LIVE CHANGES RESTORED";return;}
        var snap=backups.LoadLatest(); if(snap==null){MessageBox.Show("Henüz backup bulunmuyor.","HELLDRIVE");return;}
        optimizer.Resume(snap.SuspendedProcessIds,logger); RefreshLog(); status.Text=$"STATUS // LAST SNAPSHOT RESTORED // {snap.ProfileName}";
    }
    private void UpdateStats()
    {
        var s=stats.Read(); fpsValue.Text=s.FpsText; cpuValue.Text=$"{s.CpuPercent:0}%"; ramValue.Text=$"{s.RamMb:0} MB"; gpuValue.Text=s.GpuPercent>0?$"{s.GpuPercent:0}%":"N/A"; tempValue.Text=s.TemperatureC>0?$"{s.TemperatureC:0} °C":"N/A";
    }
    private void RefreshLog(){logList.Items.Clear();foreach(var e in logger.Entries.Take(100))logList.Items.Add($"[{e.Time}] {e.Action} | {e.Detail}");}

    private sealed class HeaderPanel : Panel
    {
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); using var b=new LinearGradientBrush(ClientRectangle,Color.FromArgb(30,8,10),Color.FromArgb(8,8,10),LinearGradientMode.Horizontal);e.Graphics.FillRectangle(b,ClientRectangle);using var f=new Font("Segoe UI",30,FontStyle.Bold);using var sf=new SolidBrush(Color.FromArgb(235,55,55));e.Graphics.DrawString("HELLDRIVE",f,sf,28,17);using var sf2=new SolidBrush(Color.FromArgb(130,130,135));using var f2=new Font("Consolas",9,FontStyle.Bold);e.Graphics.DrawString("EXTREME GAME PERFORMANCE // SYSTEM CONTROL",f2,sf2,32,65);
        }
    }
}
