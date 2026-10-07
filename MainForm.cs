using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace HellDrive;

public sealed class MainForm : Form
{
    private readonly TextBox gamePath = new();
    private readonly ComboBox profile = new();
    private readonly ComboBox resolution = new();
    private readonly ComboBox priority = new();
    private readonly CheckBox backup = new();
    private readonly CheckBox potato = new();
    private readonly Button browse = new();
    private readonly Button launch = new();
    private readonly Button restore = new();
    private readonly Label status = new();
    private readonly Label specs = new();

    private Process? gameProcess;
    private readonly string backupRoot =
        Path.Combine(AppContext.BaseDirectory, "Backups");

    public MainForm()
    {
        Text = "HELLDRIVE";
        ClientSize = new Size(900, 600);
        MinimumSize = new Size(760, 520);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(10, 10, 12);
        ForeColor = Color.Gainsboro;
        Font = new Font("Segoe UI", 10F);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;

        BuildUi();
        ApplyProfile("HELL");
    }

    private void BuildUi()
    {
        var title = new Label
        {
            Text = "HELLDRIVE",
            Font = new Font("Segoe UI", 30F, FontStyle.Bold),
            ForeColor = Color.FromArgb(235, 55, 55),
            AutoSize = true,
            Location = new Point(35, 25)
        };

        var subtitle = new Label
        {
            Text = "EXTREME GAME PERFORMANCE UTILITY",
            Font = new Font("Consolas", 9F, FontStyle.Bold),
            ForeColor = Color.FromArgb(125, 125, 130),
            AutoSize = true,
            Location = new Point(39, 72)
        };

        var gameLabel = MakeLabel("GAME EXECUTABLE", 40, 120);
        gamePath.Location = new Point(40, 145);
        gamePath.Size = new Size(670, 34);
        StyleTextBox(gamePath);

        browse.Text = "SELECT";
        browse.Location = new Point(725, 143);
        browse.Size = new Size(130, 38);
        StyleButton(browse);
        browse.Click += Browse_Click;

        var profileLabel = MakeLabel("PERFORMANCE PROFILE", 40, 205);

        profile.Items.AddRange(new object[]
        {
            "POTATO",
            "BALANCED",
            "HELL"
        });
        profile.SelectedIndex = 2;
        profile.Location = new Point(40, 230);
        profile.Size = new Size(220, 35);
        StyleCombo(profile);
        profile.SelectedIndexChanged += (_, _) =>
        {
            if (profile.SelectedItem is string p)
                ApplyProfile(p);
        };

        resolution.Items.AddRange(new object[]
        {
            "640 x 360",
            "854 x 480",
            "1280 x 720"
        });
        resolution.Location = new Point(280, 230);
        resolution.Size = new Size(180, 35);
        StyleCombo(resolution);

        priority.Items.AddRange(new object[]
        {
            "Normal",
            "Above Normal",
            "High"
        });
        priority.Location = new Point(480, 230);
        priority.Size = new Size(180, 35);
        StyleCombo(priority);

        potato.Text = "FORCE POTATO PROFILE";
        potato.AutoSize = true;
        potato.Location = new Point(40, 285);
        potato.ForeColor = Color.Gainsboro;
        potato.CheckedChanged += (_, _) =>
        {
            if (potato.Checked)
                ApplyProfile("POTATO");
        };

        backup.Text = "BACK UP CONFIG BEFORE MODIFICATION";
        backup.AutoSize = true;
        backup.Location = new Point(40, 320);
        backup.Checked = true;
        backup.ForeColor = Color.Gainsboro;

        specs.Text =
            "POTATO  640×360  |  TEXTURES LOW  |  SHADOWS OFF  |  AA OFF  |  EFFECTS LOW";
        specs.Location = new Point(40, 365);
        specs.AutoSize = true;
        specs.ForeColor = Color.FromArgb(150, 150, 155);
        specs.Font = new Font("Consolas", 9F);

        launch.Text = "ENGAGE HELLDRIVE";
        launch.Location = new Point(40, 425);
        launch.Size = new Size(390, 70);
        launch.Font = new Font("Consolas", 14F, FontStyle.Bold);
        StyleButton(launch);
        launch.Click += Launch_Click;

        restore.Text = "RESTORE BACKUP";
        restore.Location = new Point(450, 425);
        restore.Size = new Size(210, 70);
        restore.Font = new Font("Consolas", 10F, FontStyle.Bold);
        StyleButton(restore);
        restore.Click += Restore_Click;

        status.Text = "STATUS // READY";
        status.Location = new Point(40, 535);
        status.AutoSize = true;
        status.ForeColor = Color.FromArgb(235, 55, 55);
        status.Font = new Font("Consolas", 10F, FontStyle.Bold);

        Controls.AddRange(new Control[]
        {
            title, subtitle, gameLabel, gamePath, browse,
            profileLabel, profile, resolution, priority,
            potato, backup, specs, launch, restore, status
        });
    }

    private Label MakeLabel(string text, int x, int y)
    {
        return new Label
        {
            Text = text,
            Location = new Point(x, y),
            AutoSize = true,
            ForeColor = Color.FromArgb(235, 55, 55),
            Font = new Font("Consolas", 9F, FontStyle.Bold)
        };
    }

    private static void StyleTextBox(TextBox box)
    {
        box.BackColor = Color.FromArgb(22, 22, 26);
        box.ForeColor = Color.Gainsboro;
        box.BorderStyle = BorderStyle.FixedSingle;
    }

    private static void StyleCombo(ComboBox box)
    {
        box.BackColor = Color.FromArgb(22, 22, 26);
        box.ForeColor = Color.Gainsboro;
        box.FlatStyle = FlatStyle.Flat;
    }

    private static void StyleButton(Button button)
    {
        button.BackColor = Color.FromArgb(24, 24, 28);
        button.ForeColor = Color.FromArgb(235, 55, 55);
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderColor = Color.FromArgb(120, 35, 35);
        button.FlatAppearance.BorderSize = 1;
        button.Cursor = Cursors.Hand;
    }

    private void ApplyProfile(string name)
    {
        switch (name)
        {
            case "POTATO":
                resolution.SelectedIndex = 0;
                priority.SelectedIndex = 2;
                specs.Text =
                    "POTATO  640×360  |  TEXTURES LOW  |  SHADOWS OFF  |  AA OFF  |  EFFECTS LOW";
                break;

            case "BALANCED":
                resolution.SelectedIndex = 1;
                priority.SelectedIndex = 1;
                specs.Text =
                    "BALANCED  854×480  |  LOW/MEDIUM  |  SHADOWS LOW  |  AA OFF";
                break;

            default:
                resolution.SelectedIndex = 2;
                priority.SelectedIndex = 2;
                specs.Text =
                    "HELL  1280×720  |  LOW TEXTURES  |  SHADOWS OFF  |  AA OFF  |  EFFECTS LOW";
                break;
        }

        status.Text = $"STATUS // PROFILE: {name}";
    }

    private void Browse_Click(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "Windows executable (*.exe)|*.exe",
            Title = "Select Game Executable"
        };

        if (dialog.ShowDialog() == DialogResult.OK)
            gamePath.Text = dialog.FileName;
    }

    private async void Launch_Click(object? sender, EventArgs e)
    {
        if (!File.Exists(gamePath.Text))
        {
            MessageBox.Show(
                "Önce geçerli bir oyun .exe dosyası seç.",
                "HELLDRIVE",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        try
        {
            launch.Enabled = false;
            status.Text = "STATUS // INITIALIZING...";

            if (backup.Checked)
                CreateBackup();

            gameProcess = Process.Start(new ProcessStartInfo
            {
                FileName = gamePath.Text,
                WorkingDirectory =
                    Path.GetDirectoryName(gamePath.Text) ?? "",
                UseShellExecute = true
            });

            if (gameProcess == null)
                throw new InvalidOperationException("Oyun başlatılamadı.");

            await Task.Delay(1200);

            try
            {
                gameProcess.PriorityClass = priority.SelectedIndex switch
                {
                    1 => ProcessPriorityClass.AboveNormal,
                    2 => ProcessPriorityClass.High,
                    _ => ProcessPriorityClass.Normal
                };

                status.Text =
                    $"STATUS // HELLDRIVE ACTIVE // {gameProcess.ProcessName}";
            }
            catch
            {
                status.Text =
                    "STATUS // GAME ACTIVE // PRIORITY CHANGE FAILED";
            }

            await gameProcess.WaitForExitAsync();

            status.Text = "STATUS // GAME FINISHED // SYSTEM STABLE";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "HELLDRIVE ERROR",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);

            status.Text = "STATUS // ERROR";
        }
        finally
        {
            launch.Enabled = true;
            gameProcess?.Dispose();
            gameProcess = null;
        }
    }

    private void CreateBackup()
    {
        Directory.CreateDirectory(backupRoot);

        string gameName =
            Path.GetFileNameWithoutExtension(gamePath.Text);

        string target = Path.Combine(
            backupRoot,
            gameName + "_" +
            DateTime.Now.ToString("yyyyMMdd_HHmmss"));

        Directory.CreateDirectory(target);

        // İlk sürüm güvenli davranır:
        // Oyunun bilinmeyen config dosyalarını otomatik değiştirmez.
        File.WriteAllText(
            Path.Combine(target, "profile.json"),
            JsonSerializer.Serialize(new
            {
                Game = gamePath.Text,
                Profile = profile.SelectedItem?.ToString(),
                Resolution = resolution.SelectedItem?.ToString(),
                Priority = priority.SelectedItem?.ToString(),
                Created = DateTime.Now
            }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private void Restore_Click(object? sender, EventArgs e)
    {
        if (!Directory.Exists(backupRoot))
        {
            MessageBox.Show(
                "Henüz backup bulunmuyor.",
                "HELLDRIVE");
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = backupRoot,
            UseShellExecute = true
        });

        status.Text = "STATUS // BACKUP DIRECTORY OPENED";
    }
}
