using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Text;
using System.Windows.Forms;

namespace FireZenHelper
{
    public sealed class MainForm : Form
    {
        private readonly string script;
        private readonly ComboBox zen = new ComboBox(), firefox = new ComboBox();
        private readonly TextBox backups = new TextBox(), log = new TextBox();
        private readonly Label zenInfo = new Label(), firefoxInfo = new Label(), status = new Label();
        private readonly CheckBox pause = new CheckBox(), borderless = new CheckBox(), keepEngine = new CheckBox();
        private readonly Label copyInfo = new Label();
        private readonly ProgressBar progress = new ProgressBar();
        private readonly Button check = new Button(), apply = new Button(), restore = new Button();
        private readonly Color ink = Color.FromArgb(29, 52, 61), accent = Color.FromArgb(17, 104, 107);
        private bool busy, valid, preview;
        private string sineProfile;
        private bool sineFireZen;
        private readonly FlowLayoutPanel actions = new FlowLayoutPanel();
        private readonly TableLayoutPanel layout = new TableLayoutPanel();

        public MainForm(string scriptPath, string zenPath, string firefoxPath, string backupRoot, bool pauseUpdates, bool removeBorder, bool keepFirefoxEngine, string profilePath, bool includeSineMod, bool openSineSetup)
        {
            script = scriptPath;
            sineProfile = profilePath;
            sineFireZen = includeSineMod;
            Text = "FireZEN Helper";
            Font = new Font("Segoe UI", 10F);
            ForeColor = ink;
            BackColor = Color.FromArgb(239, 245, 246);
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(790, 780);
            ClientSize = new Size(880, 855);
            AutoScaleMode = AutoScaleMode.Dpi;
            layout.Dock = DockStyle.Fill;
            layout.Padding = new Padding(28, 21, 28, 20);
            layout.ColumnCount = 3;
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
            Controls.Add(layout);

            var title = new Label { Text = "FireZEN Helper", Font = new Font("Segoe UI", 25F, FontStyle.Bold), ForeColor = accent, AutoSize = true, Margin = new Padding(0, 0, 0, 3) };
            AddWide(title, SizeType.AutoSize, 0);
            AddWide(new Label { Text = "Bring Zen's interface back to Firefox.", AutoSize = true, Margin = new Padding(1, 0, 0, 16) }, SizeType.AutoSize, 0);

            AddPathRow("Zen installation", zen, zenInfo, true);
            AddPathRow("Firefox installation", firefox, firefoxInfo, false);

            var detect = Button("Detect again", false);
            detect.Click += delegate { DetectFolders(true); };
            var guide = Button("Sine Mods", false);
            guide.Click += delegate { ShowSine(); };
            int utilityRow = layout.RowCount++;
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            copyInfo.Dock = DockStyle.Fill;
            copyInfo.TextAlign = ContentAlignment.MiddleLeft;
            copyInfo.Font = new Font("Segoe UI", 9F);
            copyInfo.Margin = new Padding(0, 0, 8, 0);
            layout.Controls.Add(copyInfo, 0, utilityRow);
            layout.Controls.Add(detect, 1, utilityRow);
            layout.Controls.Add(guide, 2, utilityRow);

            keepEngine.Text = "Keep Mozilla's Firefox engine (experimental)";
            keepEngine.Checked = keepFirefoxEngine;
            keepEngine.AutoSize = true;
            keepEngine.Margin = new Padding(0, 2, 0, 3);
            AddWide(keepEngine, SizeType.AutoSize, 0);
            AddWide(new Label {
                Text = "Keeps Firefox's xul.dll. Some Zen features may fail; protected video playback is not guaranteed.\r\nRequires an original Mozilla engine. Sine Mods is installed separately for your chosen profile.",
                AutoSize = true, Font = new Font("Segoe UI", 9F), ForeColor = Color.FromArgb(75, 96, 103), Margin = new Padding(21, 0, 0, 2)
            }, SizeType.AutoSize, 0);
            UpdateCopyInfo();

            borderless.Text = "Use borderless layout by default";
            borderless.Checked = removeBorder;
            borderless.AutoSize = true;
            borderless.Margin = new Padding(0, 7, 0, 3);
            AddWide(borderless, SizeType.AutoSize, 0);
            AddWide(new Label {
                Text = "Removes Zen's outer 8-pixel gap. Rounded corners stay as they are.",
                AutoSize = true, Font = new Font("Segoe UI", 9F), ForeColor = Color.FromArgb(75, 96, 103), Margin = new Padding(21, 0, 0, 2)
            }, SizeType.AutoSize, 0);

            pause.Text = "Pause Firefox updates to keep the FireZEN interface intact";
            pause.Checked = pauseUpdates;
            pause.AutoSize = true;
            pause.Margin = new Padding(0, 10, 0, 4);
            AddWide(pause, SizeType.AutoSize, 0);
            AddWide(new Label {
                Text = "Pausing also stops security updates. Update Firefox and Zen together, then reapply.\r\nLeaving this unchecked preserves your current update settings.",
                AutoSize = true, Font = new Font("Segoe UI", 9F), ForeColor = Color.FromArgb(75, 96, 103), Margin = new Padding(21, 0, 0, 14)
            }, SizeType.AutoSize, 0);

            AddWide(new Label { Text = "Backup folder", AutoSize = true, Font = new Font("Segoe UI", 10F, FontStyle.Bold), Margin = new Padding(0, 2, 0, 4) }, SizeType.AutoSize, 0);
            int backupRow = layout.RowCount++;
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 39));
            backups.Text = String.IsNullOrWhiteSpace(backupRoot) ? Engine.DefaultBackupRoot : backupRoot;
            backups.Dock = DockStyle.Fill;
            backups.Margin = new Padding(0, 3, 9, 0);
            layout.Controls.Add(backups, 0, backupRow);
            var browseBackups = Button("Browse...", false);
            browseBackups.Click += delegate {
                using (var dialog = new FolderBrowserDialog { Description = "Choose a folder for FireZEN backups", SelectedPath = backups.Text })
                    if (dialog.ShowDialog(this) == DialogResult.OK) backups.Text = dialog.SelectedPath;
            };
            layout.Controls.Add(browseBackups, 1, backupRow);
            var openBackups = Button("Open folder", false);
            openBackups.Click += delegate {
                if (Directory.Exists(backups.Text)) Open(backups.Text);
                else MessageBox.Show(this, "A backup is created when you apply FireZEN.", "Backups", MessageBoxButtons.OK, MessageBoxIcon.Information);
            };
            layout.Controls.Add(openBackups, 2, backupRow);

            status.Text = "Check the two installations before applying.";
            status.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            status.ForeColor = accent;
            status.AutoSize = true;
            status.MaximumSize = new Size(800, 0);
            status.Margin = new Padding(0, 12, 0, 8);
            AddWide(status, SizeType.AutoSize, 0);
            progress.Dock = DockStyle.Fill;
            progress.Height = 7;
            progress.Margin = new Padding(0, 0, 0, 10);
            AddWide(progress, SizeType.Absolute, 17);

            log.Multiline = true;
            log.ReadOnly = true;
            log.ScrollBars = ScrollBars.Vertical;
            log.BackColor = Color.White;
            log.ForeColor = ink;
            log.BorderStyle = BorderStyle.FixedSingle;
            log.Font = new Font("Segoe UI", 9F);
            log.Dock = DockStyle.Fill;
            log.Margin = new Padding(0, 0, 0, 12);
            AddWide(log, SizeType.Percent, 100);

            actions.Dock = DockStyle.Fill;
            actions.FlowDirection = FlowDirection.LeftToRight;
            actions.WrapContents = false;
            actions.Margin = Padding.Empty;
            ConfigureButton(check, "Check setup", false);
            ConfigureButton(apply, "Apply FireZEN", true);
            ConfigureButton(restore, "Restore latest backup", false);
            apply.Width = 160;
            restore.Width = 195;
            check.Width = 135;
            actions.Controls.Add(check);
            actions.Controls.Add(apply);
            actions.Controls.Add(restore);
            var copyLaunch = Button("Copy Firefox launch", false);
            copyLaunch.Width = 165;
            copyLaunch.Click += delegate {
                try {
                    Clipboard.SetText(FirefoxLaunchCommand(firefox.Text));
                    MessageBox.Show(this, "Close Firefox, then press Win+R, paste the copied command, and press Enter to open Firefox with fresh interface caches.", "Firefox launch copied", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception e) { MessageBox.Show(this, e.Message, "Copy Firefox launch", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            };
            actions.Controls.Add(copyLaunch);
            AddWide(actions, SizeType.Absolute, 48);
            AddWide(new Label {
                Text = "Close Firefox and Zen before applying. Windows may ask for administrator access.",
                Font = new Font("Segoe UI", 9F), AutoSize = true, ForeColor = Color.FromArgb(75, 96, 103), Margin = new Padding(0, 2, 0, 0)
            }, SizeType.AutoSize, 0);

            check.Click += delegate { CheckSetup(); };
            apply.Click += delegate { ApplyClicked(); };
            restore.Click += delegate { RestoreClicked(); };
            zen.TextChanged += delegate { InvalidateSetup(); UpdateInfo(zen, zenInfo, true); };
            firefox.TextChanged += delegate { InvalidateSetup(); UpdateInfo(firefox, firefoxInfo, false); };
            backups.TextChanged += delegate { InvalidateSetup(); };
            pause.CheckedChanged += delegate { InvalidateSetup(); };
            borderless.CheckedChanged += delegate { InvalidateSetup(); };
            keepEngine.CheckedChanged += delegate { UpdateCopyInfo(); InvalidateSetup(); };
            FormClosing += delegate(object sender, FormClosingEventArgs e) {
                if (busy) { e.Cancel = true; MessageBox.Show(this, "Wait for the current operation to finish before closing.", "Operation in progress"); }
            };
            DetectFolders(false);
            if (!String.IsNullOrWhiteSpace(zenPath)) { zen.SelectedIndex = -1; zen.Text = zenPath; }
            if (!String.IsNullOrWhiteSpace(firefoxPath)) { firefox.SelectedIndex = -1; firefox.Text = firefoxPath; }
            apply.Enabled = false;
            Append("Ready. Browser folders were detected automatically where possible.");
            Append("A verified backup is made before Firefox files are changed. Sine Mods has a separate installer and backup for its chosen profile.");
            Shown += delegate {
                if (!preview && !String.IsNullOrWhiteSpace(zen.Text) && !String.IsNullOrWhiteSpace(firefox.Text)) CheckSetup(false);
                if (!preview && openSineSetup) BeginInvoke(new Action(ShowSine));
            };
        }

        private Button Button(string text, bool primary)
        {
            var button = new Button();
            ConfigureButton(button, text, primary);
            return button;
        }

        private void ConfigureButton(Button button, string text, bool primary)
        {
            button.Text = text;
            button.Dock = DockStyle.None;
            button.Size = new Size(106, 34);
            button.Margin = new Padding(0, 0, 7, 0);
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = primary ? accent : Color.FromArgb(168, 189, 194);
            button.BackColor = primary ? accent : Color.White;
            button.ForeColor = primary ? Color.White : ink;
            button.Cursor = Cursors.Hand;
        }

        private void AddWide(Control control, SizeType type, float height)
        {
            int row = layout.RowCount++;
            layout.RowStyles.Add(new RowStyle(type, height));
            layout.Controls.Add(control, 0, row);
            layout.SetColumnSpan(control, 3);
        }

        private void AddPathRow(string title, ComboBox box, Label info, bool isZen)
        {
            AddWide(new Label { Text = title, AutoSize = true, Font = new Font("Segoe UI", 10F, FontStyle.Bold), Margin = new Padding(0, 5, 0, 5) }, SizeType.AutoSize, 0);
            int row = layout.RowCount++;
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            box.Dock = DockStyle.Fill;
            box.Margin = new Padding(0, 2, 9, 0);
            box.DropDownStyle = ComboBoxStyle.DropDown;
            box.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
            box.AutoCompleteSource = AutoCompleteSource.ListItems;
            layout.Controls.Add(box, 0, row);
            layout.SetColumnSpan(box, 2);
            var browse = Button("Browse...", false);
            browse.Click += delegate {
                using (var dialog = new FolderBrowserDialog { Description = "Select the folder containing " + (isZen ? "zen.exe" : "firefox.exe"), SelectedPath = box.Text })
                    if (dialog.ShowDialog(this) == DialogResult.OK) box.Text = dialog.SelectedPath;
            };
            layout.Controls.Add(browse, 2, row);
            info.AutoSize = true;
            info.Font = new Font("Segoe UI", 9F);
            info.ForeColor = Color.FromArgb(75, 96, 103);
            info.Margin = new Padding(0, 0, 0, 7);
            AddWide(info, SizeType.AutoSize, 0);
        }

        private void DetectFolders(bool replace)
        {
            foreach (bool isZen in new[] { true, false })
            {
                ComboBox box = isZen ? zen : firefox;
                string previous = box.Text;
                box.Items.Clear();
                foreach (string path in Engine.Detect(isZen)) box.Items.Add(path);
                if ((replace || String.IsNullOrWhiteSpace(previous)) && box.Items.Count > 0) box.SelectedIndex = 0;
                else box.Text = previous;
                UpdateInfo(box, isZen ? zenInfo : firefoxInfo, isZen);
            }
            if (replace) Append("Detection finished. Use Browse or type a path if an installation is missing.");
        }

        private void UpdateInfo(ComboBox box, Label label, bool isZen)
        {
            try
            {
                BrowserInfo info = Engine.ReadBrowser(box.Text, isZen);
                label.Text = (isZen ? "Zen " + info.Version + "  |  Firefox engine " : "Firefox engine ") + info.Engine + "  |  " + info.Architecture;
                label.ForeColor = Color.FromArgb(75, 96, 103);
            }
            catch { label.Text = "Choose the installation folder, or paste its full path."; }
        }

        private void UpdateCopyInfo()
        {
            copyInfo.Text = keepEngine.Checked
                ? "Copies Zen's browser folder and root omni.ja."
                : "Copies Zen's browser folder, xul.dll and root omni.ja.";
        }

        private void InvalidateSetup()
        {
            valid = false;
            apply.Enabled = false;
            status.Text = "Check the two installations before applying.";
            status.ForeColor = accent;
        }

        private void Append(string message)
        {
            log.AppendText(message + Environment.NewLine);
        }

        private void CheckSetup(bool notify = true)
        {
            if (busy) return;
            try
            {
                BrowserPair pair = Engine.Inspect(zen.Text, firefox.Text, keepEngine.Checked);
                zen.Text = pair.Zen.Path;
                firefox.Text = pair.Firefox.Path;
                if (String.IsNullOrWhiteSpace(backups.Text)) throw new IOException("Choose a backup folder.");
                Path.GetFullPath(backups.Text);
                valid = true;
                apply.Enabled = true;
                status.Text = (keepEngine.Checked ? "Ready: keep Mozilla engine (experimental). Firefox " : "Ready: copy Zen engine. Firefox ") + pair.Firefox.Engine + " / " + pair.Firefox.Architecture;
                status.ForeColor = accent;
                Append("Setup checked: " + pair.FileCount + " files, " + Math.Ceiling(pair.SourceBytes / 1048576D) + " MB to copy. A backup will also need disk space.");
            }
            catch (Exception e)
            {
                valid = false;
                apply.Enabled = false;
                status.Text = "Setup needs attention. See the details below.";
                status.ForeColor = Color.FromArgb(159, 66, 42);
                Append(e.Message);
                if (!preview && notify) MessageBox.Show(this, e.Message, "Check setup", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private bool Admin()
        {
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }

        private static string QuotePS(string text)
        {
            return "'" + text.Replace("'", "''") + "'";
        }

        // Build without launching a process so all elevation handoffs can be checked safely.
        private string RelaunchCommand(bool forSine, string profile, bool includeSineMod)
        {
            string command = "& " + QuotePS(script) + " -ZenPath " + QuotePS(zen.Text) + " -FirefoxPath " + QuotePS(firefox.Text) + " -BackupRoot " + QuotePS(backups.Text) + " -PauseUpdates " + (pause.Checked ? "1" : "0") + " -RemoveBorder " + (borderless.Checked ? "1" : "0") + " -KeepFirefoxEngine " + (keepEngine.Checked ? "1" : "0");
            command += " -SineProfilePath " + QuotePS((forSine ? profile : sineProfile) ?? String.Empty)
                + " -SineFireZen " + ((forSine ? includeSineMod : sineFireZen) ? "1" : "0");
            if (forSine) command += " -OpenSineSetup";
            return command;
        }

        private bool ReadyToWrite(bool forSine = false, string profile = "", bool includeSineMod = true)
        {
            if (Admin()) return true;
            string command = RelaunchCommand(forSine, profile, includeSineMod);
            string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(command));
            try
            {
                var start = new ProcessStartInfo {
                    FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe"),
                    Arguments = "-NoLogo -NoProfile -STA -WindowStyle Hidden -ExecutionPolicy Bypass -EncodedCommand " + encoded,
                    Verb = "runas", UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden
                };
                Process elevated = Process.Start(start);
                if (elevated != null) elevated.Dispose();
                Close();
            }
            catch (Win32Exception e)
            {
                Append(e.NativeErrorCode == 1223 ? "Administrator request cancelled. No files were changed." : "Could not restart as administrator: " + e.Message);
            }
            return false;
        }

        private string[] CacheProfiles(string target)
        {
            var profiles = new List<string>();
            try { profiles.AddRange(SineInstaller.DetectProfiles(target)); }
            catch (Exception e) { Append("Could not read registered Firefox profiles: " + e.Message + " Use Copy Firefox launch after the operation to refresh interface caches."); }
            if (!String.IsNullOrWhiteSpace(sineProfile))
            {
                // A removed or inactive manual Sine choice must not prevent browser restore.
                try { profiles.Add(SineInstaller.ValidateProfile(sineProfile)); }
                catch (Exception) { }
            }
            return profiles.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        private static string FirefoxLaunchCommand(string folder)
        {
            string executable = Path.Combine(Engine.NormalizeFolder(folder), "firefox.exe");
            if (!File.Exists(executable)) throw new FileNotFoundException("Choose the Firefox installation folder containing firefox.exe.");
            return "\"" + executable + "\" -purgecaches -no-remote";
        }

        private static string CacheRefreshDescription(int profileCount)
        {
            return profileCount > 0
                ? "Interface caches will refresh on the next normal Firefox start for " + profileCount + " detected or selected profile(s)."
                : "No Firefox profiles were found. Afterward, use Copy Firefox launch to start Firefox with fresh interface caches.";
        }

        private void ApplyClicked()
        {
            if (busy || !valid) return;
            if (!ReadyToWrite()) return;
            string source = zen.Text, target = firefox.Text, backupRoot = backups.Text;
            bool pauseUpdates = pause.Checked;
            bool removeBorder = borderless.Checked;
            bool keepFirefoxEngine = keepEngine.Checked;
            string[] profilePaths = CacheProfiles(target);
            string description = (keepFirefoxEngine
                ? "Experimental: copy Zen's browser folder and root omni.ja, keeping Mozilla's Firefox engine, into:\r\n"
                : "Copy Zen's browser folder, xul.dll and root omni.ja into:\r\n") + target + "\r\n\r\nA verified backup will be saved in:\r\n" + backupRoot;
            if (keepFirefoxEngine) description += "\r\n\r\nSome Zen features may fail. Protected video playback is not guaranteed.";
            description += "\r\n\r\nSine Mods is a separate installation for your chosen Firefox profile.";
            if (pauseUpdates) description += "\r\n\r\nFirefox updates, including security updates, will be paused.";
            if (removeBorder) description += "\r\n\r\nThe outer content gap will default to 0 pixels. Explicit profile settings take precedence.";
            description += "\r\n\r\n" + CacheRefreshDescription(profilePaths.Length);
            if (MessageBox.Show(this, description, "Apply FireZEN", MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK) return;
            RunOperation(delegate(Action<ProgressUpdate> callback) { return Engine.Apply(source, target, backupRoot, pauseUpdates, removeBorder, keepFirefoxEngine, profilePaths, callback); }, true, profilePaths.Length);
        }

        private void RestoreClicked()
        {
            if (busy) return;
            try
            {
                string target = Engine.NormalizeFolder(firefox.Text);
                string backup = Engine.LatestBackup(backups.Text, target);
                if (backup == null) { MessageBox.Show(this, "No applied backup was found for this Firefox installation in the selected backup folder.", "Restore backup"); return; }
                if (!ReadyToWrite()) return;
                string[] profilePaths = CacheProfiles(target);
                if (MessageBox.Show(this, "Return the files changed by the latest operation to their saved state? This includes the update policy if that operation changed it.\r\n\r\n" + backup + "\r\n\r\nRestore is blocked if Firefox has since been updated.\r\n\r\n" + CacheRefreshDescription(profilePaths.Length), "Restore latest backup", MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK) return;
                RunOperation(delegate(Action<ProgressUpdate> callback) { Engine.Restore(backup, target, profilePaths, callback); return backup; }, false, profilePaths.Length);
            }
            catch (Exception e) { MessageBox.Show(this, e.Message, "Restore backup", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }

        private void RunOperation(Func<Action<ProgressUpdate>, string> operation, bool applying, int profileCount)
        {
            busy = true;
            valid = false;
            SetInputs(false);
            progress.Value = 0;
            status.Text = applying ? (keepEngine.Checked ? "Backing up, then applying FireZEN with Mozilla engine..." : "Backing up, then applying FireZEN with Zen engine...") : "Restoring the latest backup...";
            var worker = new BackgroundWorker { WorkerReportsProgress = true };
            worker.DoWork += delegate(object sender, DoWorkEventArgs e) {
                e.Result = operation(delegate(ProgressUpdate update) { worker.ReportProgress(update.Percent, update); });
            };
            worker.ProgressChanged += delegate(object sender, ProgressChangedEventArgs e) {
                var update = (ProgressUpdate)e.UserState;
                progress.Value = Math.Min(100, Math.Max(0, update.Percent));
                Append(update.Message);
            };
            worker.RunWorkerCompleted += delegate(object sender, RunWorkerCompletedEventArgs e) {
                busy = false;
                SetInputs(true);
                apply.Enabled = false;
                if (e.Error != null)
                {
                    status.Text = "The operation stopped. See details below.";
                    status.ForeColor = Color.FromArgb(159, 66, 42);
                    Append(e.Error.Message);
                    MessageBox.Show(this, e.Error.Message, "Operation stopped", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                else
                {
                    progress.Value = 100;
                    status.Text = applying ? (keepEngine.Checked ? "FireZEN applied with Mozilla engine (experimental)." : "FireZEN applied with Zen engine.") : "Backup restored.";
                    status.Text += profileCount > 0 ? " Open Firefox normally." : " Use Copy Firefox launch.";
                    status.ForeColor = accent;
                    Append("Backup: " + (string)e.Result);
                    Append(profileCount > 0
                        ? "Interface cache refresh queued. Close any running Firefox windows, then open Firefox normally."
                        : "No profile cache refresh was queued. Close Firefox, use Copy Firefox launch, then press Win+R and paste the command.");
                    if (applying && keepEngine.Checked) Append("Mozilla engine retained. Open Firefox and test the features and protected video services you use; compatibility is experimental.");
                    if (applying && borderless.Checked) Append("Borderless default installed. If a profile still has a frame, set zen.theme.content-element-separation to 0 in about:config (a profile override wins over defaults).");
                    if (applying) Append("Use Sine Mods to install or repair Sine and the FireZEN visual-fix mod for your Firefox profile.");
                }
                worker.Dispose();
            };
            worker.RunWorkerAsync();
        }

        private void SetInputs(bool enabled)
        {
            foreach (Control control in layout.Controls)
                if (!(control is Label) && control != log && control != progress) control.Enabled = enabled;
            apply.Enabled = enabled && valid;
        }

        private void Open(string target)
        {
            try { using (Process process = Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })) { } }
            catch (Exception e) { MessageBox.Show(this, e.Message, "Could not open link"); }
        }

        private void ShowSine()
        {
            try
            {
                using (var form = new SineSetupForm(firefox.Text, backups.Text, sineProfile, sineFireZen,
                    delegate(string profile, bool includeMod) { return ReadyToWrite(true, profile, includeMod); }))
                {
                    form.ShowDialog(this);
                    sineProfile = form.ProfilePath;
                    sineFireZen = form.IncludeMod;
                }
            }
            catch (Exception e) { MessageBox.Show(this, e.Message, "Sine Mods", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }

        public void SaveSinePreview(string path)
        {
            using (var form = new SineSetupForm(firefox.Text, backups.Text, sineProfile, sineFireZen,
                delegate(string profile, bool includeMod) { return false; })) form.SavePreview(path);
        }

        // Render the real form for QA without invoking any file-changing operation.
        public void SavePreview(string path)
        {
            if (String.IsNullOrWhiteSpace(path)) throw new ArgumentException("Specify PreviewPath.");
            preview = true;
            Show();
            CheckSetup();
            PerformLayout();
            Application.DoEvents();
            using (var bitmap = new Bitmap(Width, Height))
            {
                DrawToBitmap(bitmap, new Rectangle(0, 0, Width, Height));
                bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            }
            Hide();
        }
    }
}
