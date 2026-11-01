using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace FireZenHelper
{
    public sealed class SineSetupForm : Form
    {
        private readonly string firefox, backups;
        private readonly Func<string, bool, bool> requestPrivilege;
        private readonly ComboBox profiles = new ComboBox();
        private readonly CheckBox firezen = new CheckBox();
        private readonly Label status = new Label();
        private readonly TextBox log = new TextBox();
        private readonly ProgressBar progress = new ProgressBar();
        private readonly TableLayoutPanel layout = new TableLayoutPanel();
        private bool busy;

        public string ProfilePath { get { return profiles.Text; } }
        public bool IncludeMod { get { return firezen.Checked; } }

        public SineSetupForm(string firefoxPath, string backupRoot, string preferredProfile, bool includeMod, Func<string, bool, bool> privilege)
        {
            firefox = Engine.ReadBrowser(firefoxPath, false).Path;
            backups = Path.GetFullPath(backupRoot);
            requestPrivilege = privilege;
            Text = "Install Sine Mods";
            Font = new Font("Segoe UI", 10F);
            ForeColor = Color.FromArgb(29, 52, 61);
            BackColor = Color.FromArgb(239, 245, 246);
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(820, 680);
            MinimumSize = new Size(780, 650);
            AutoScaleMode = AutoScaleMode.Dpi;
            layout.Dock = DockStyle.Fill;
            layout.Padding = new Padding(24);
            layout.ColumnCount = 2;
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
            Controls.Add(layout);
            Wide(new Label { Text = "Sine Mods", Font = new Font("Segoe UI", 22F, FontStyle.Bold), ForeColor = Color.FromArgb(17, 104, 107), AutoSize = true }, SizeType.AutoSize, 0);
            Wide(new Label { Text = "Install or repair Sine " + SineInstaller.Version + " in the selected Firefox profile.", AutoSize = true, Margin = new Padding(0, 3, 0, 13) }, SizeType.AutoSize, 0);
            Wide(new Label { Text = "Firefox: " + firefox, AutoSize = true, Margin = new Padding(0, 0, 0, 14) }, SizeType.AutoSize, 0);
            Wide(new Label { Text = "Firefox profile", Font = new Font("Segoe UI", 10F, FontStyle.Bold), AutoSize = true, Margin = new Padding(0, 0, 0, 5) }, SizeType.AutoSize, 0);
            int row = layout.RowCount++;
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 37));
            profiles.Dock = DockStyle.Fill;
            profiles.Margin = new Padding(0, 1, 8, 0);
            profiles.DropDownStyle = ComboBoxStyle.DropDown;
            foreach (string path in SineInstaller.DetectProfiles(firefox)) profiles.Items.Add(path);
            if (!String.IsNullOrWhiteSpace(preferredProfile)) profiles.Text = preferredProfile;
            else if (profiles.Items.Count > 0) profiles.SelectedIndex = 0;
            layout.Controls.Add(profiles, 0, row);
            var browse = MakeButton("Browse...", false);
            browse.Click += delegate {
                using (var dialog = new FolderBrowserDialog { Description = "Select the Firefox profile folder shown in about:support", SelectedPath = profiles.Text })
                    if (dialog.ShowDialog(this) == DialogResult.OK) profiles.Text = dialog.SelectedPath;
            };
            layout.Controls.Add(browse, 1, row);
            Wide(new Label { Text = "Verify this is your active profile: Firefox > about:support > Profile Folder.", AutoSize = true, Font = new Font("Segoe UI", 9F), Margin = new Padding(0, 0, 0, 13) }, SizeType.AutoSize, 0);
            firezen.Text = "Also install the FireZEN visual-fix mod";
            firezen.Checked = includeMod;
            firezen.AutoSize = true;
            firezen.Margin = new Padding(0, 0, 0, 8);
            Wide(firezen, SizeType.AutoSize, 0);
            Wide(new Label {
                Text = "Downloads official files and checks their SHA-256 fingerprints. Existing mods stay\r\nin place. Sine files and a startup-cache refresh flag get a separate verified backup.",
                AutoSize = true, Font = new Font("Segoe UI", 9F), Margin = new Padding(0, 0, 0, 14)
            }, SizeType.AutoSize, 0);
            status.Text = "Close Firefox and Zen before installing or restoring.";
            status.AutoSize = true;
            status.ForeColor = Color.FromArgb(17, 104, 107);
            status.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            status.Margin = new Padding(0, 0, 0, 9);
            Wide(status, SizeType.AutoSize, 0);
            progress.Dock = DockStyle.Fill;
            progress.Margin = new Padding(0, 0, 0, 10);
            Wide(progress, SizeType.Absolute, 17);
            log.Multiline = true;
            log.ReadOnly = true;
            log.ScrollBars = ScrollBars.Vertical;
            log.Dock = DockStyle.Fill;
            log.BackColor = Color.White;
            log.Margin = new Padding(0, 0, 0, 12);
            Wide(log, SizeType.Percent, 100);
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
            var install = MakeButton("Install / repair Sine", true);
            install.Width = 177;
            install.Click += delegate { InstallClicked(); };
            actions.Controls.Add(install);
            var restore = MakeButton("Restore Sine backup", false);
            restore.Width = 184;
            restore.Click += delegate { RestoreClicked(); };
            actions.Controls.Add(restore);
            var launch = MakeButton("Copy Firefox launch", false);
            launch.Width = 166;
            launch.Click += delegate { LaunchFirefox(); };
            actions.Controls.Add(launch);
            var guide = MakeButton("Official guide", false);
            guide.Width = 123;
            guide.Click += delegate {
                try { using (Process opened = Process.Start(new ProcessStartInfo("https://github.com/sineorg/docs/blob/main/src/installation.md") { UseShellExecute = true })) { } }
                catch (Exception e) { MessageBox.Show(this, e.Message, "Open official guide", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            };
            actions.Controls.Add(guide);
            Wide(actions, SizeType.Absolute, 45);
            Wide(new Label { Text = "After installing, restart Firefox normally; the selected profile's startup cache will refresh.\r\nAfter restoring, use Copy Firefox launch and paste into Win+R to refresh caches.", AutoSize = true, Font = new Font("Segoe UI", 9F), Margin = new Padding(0, 4, 0, 0) }, SizeType.AutoSize, 0);
            log.Text = "No files are changed until you choose Install / repair Sine.\r\nBackup folder: " + Path.Combine(backups, "Sine") + "\r\n";
            FormClosing += delegate(object sender, FormClosingEventArgs e) {
                if (busy) { e.Cancel = true; MessageBox.Show(this, "Wait for the Sine operation to finish before closing.", "Operation in progress"); }
            };
        }

        private Button MakeButton(string text, bool primary)
        {
            var accent = Color.FromArgb(17, 104, 107);
            var b = new Button { Text = text, Size = new Size(106, 34), FlatStyle = FlatStyle.Flat, BackColor = primary ? accent : Color.White, ForeColor = primary ? Color.White : ForeColor, Margin = new Padding(0, 0, 7, 0), Cursor = Cursors.Hand };
            b.FlatAppearance.BorderColor = primary ? accent : Color.FromArgb(168, 189, 194);
            return b;
        }

        private void Wide(Control control, SizeType type, float height)
        {
            int row = layout.RowCount++;
            layout.RowStyles.Add(new RowStyle(type, height));
            layout.Controls.Add(control, 0, row);
            layout.SetColumnSpan(control, 2);
        }

        private void InstallClicked()
        {
            try
            {
                string profile = SineInstaller.ValidateProfile(profiles.Text);
                if (!requestPrivilege(profile, firezen.Checked)) return;
                bool includeMod = firezen.Checked;
                string message = "Install Sine " + SineInstaller.Version + " for:\r\n" + firefox + "\r\n\r\nProfile:\r\n" + profile + "\r\n\r\nChanged files will be backed up under:\r\n" + Path.Combine(backups, "Sine");
                if (includeMod) message += "\r\n\r\nThe FireZEN visual-fix mod will also be installed and enabled.";
                if (MessageBox.Show(this, message, "Install Sine Mods", MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK) return;
                RunOperation(delegate(Action<ProgressUpdate> report) {
                    SineInstaller.ValidateSetup(firefox, profile, backups);
                    string cache = Path.Combine(backups, "SineDownloads", "2.3.3");
                    SineInstaller.PrepareDownloads(cache, includeMod, report);
                    return SineInstaller.InstallFromPackages(firefox, profile, backups, cache, includeMod, report);
                }, true);
            }
            catch (Exception e) { MessageBox.Show(this, e.Message, "Sine setup", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }

        private void RestoreClicked()
        {
            try
            {
                string profile = SineInstaller.ValidateProfile(profiles.Text);
                string backup = SineInstaller.LatestBackup(backups, firefox, profile);
                if (backup == null) throw new IOException("No applied Sine backup was found for this Firefox installation and profile.");
                if (!requestPrivilege(profile, firezen.Checked)) return;
                if (MessageBox.Show(this, "Restore the Sine loader, engine and mod files changed by this operation?\r\n\r\n" + backup + "\r\n\r\nBrowser history, cookies and passwords are not part of this backup.", "Restore Sine backup", MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK) return;
                RunOperation(delegate(Action<ProgressUpdate> report) { SineInstaller.Restore(backup, firefox, profile, report); return backup; }, false);
            }
            catch (Exception e) { MessageBox.Show(this, e.Message, "Restore Sine backup", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }

        private void RunOperation(Func<Action<ProgressUpdate>, string> action, bool installing)
        {
            busy = true;
            foreach (Control c in layout.Controls) if (!(c is Label) && c != log && c != progress) c.Enabled = false;
            status.Text = installing ? "Downloading, verifying and installing Sine..." : "Restoring Sine files...";
            progress.Value = 0;
            var worker = new BackgroundWorker { WorkerReportsProgress = true };
            worker.DoWork += delegate(object sender, DoWorkEventArgs e) { e.Result = action(delegate(ProgressUpdate u) { worker.ReportProgress(u.Percent, u); }); };
            worker.ProgressChanged += delegate(object sender, ProgressChangedEventArgs e) { progress.Value = Math.Max(0, Math.Min(100, e.ProgressPercentage)); log.AppendText(((ProgressUpdate)e.UserState).Message + "\r\n"); };
            worker.RunWorkerCompleted += delegate(object sender, RunWorkerCompletedEventArgs e) {
                busy = false;
                foreach (Control c in layout.Controls) c.Enabled = true;
                if (e.Error != null)
                {
                    status.Text = "Sine operation stopped. See details below.";
                    log.AppendText(e.Error.Message + "\r\n");
                    MessageBox.Show(this, e.Error.Message, "Sine operation stopped", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                else
                {
                    progress.Value = 100;
                    status.Text = installing ? "Sine installed. Restart Firefox to finish." : "Sine restored. Use Copy Firefox launch, then Win+R and paste.";
                    log.AppendText("Backup: " + (string)e.Result + "\r\n");
                    log.AppendText(installing
                        ? "Restart Firefox normally. The selected profile's startup cache will refresh automatically. If you previously disabled all Sine mods, enable them in Settings > Sine.\r\n"
                        : "Use Copy Firefox launch, then Win+R and paste. This starts the restored profile with refreshed caches.\r\n");
                }
                worker.Dispose();
            };
            worker.RunWorkerAsync();
        }

        private void LaunchFirefox()
        {
            try
            {
                string profile = SineInstaller.ValidateProfile(profiles.Text);
                Engine.CheckBrowsersClosed();
                Clipboard.SetText("\"" + Path.Combine(firefox, "firefox.exe") + "\" -purgecaches -no-remote -profile \"" + profile + "\"");
                MessageBox.Show(this, "Firefox's launch command was copied. Press Win+R, paste, and press Enter to start this profile with refreshed startup caches.", "Open Firefox", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception e) { MessageBox.Show(this, e.Message, "Open Firefox", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }

        public void SavePreview(string path)
        {
            Show(); PerformLayout(); Application.DoEvents();
            using (var bitmap = new Bitmap(Width, Height)) { DrawToBitmap(bitmap, new Rectangle(0, 0, Width, Height)); bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png); }
            Hide();
        }
    }
}
