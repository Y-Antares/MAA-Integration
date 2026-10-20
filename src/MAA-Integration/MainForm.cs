using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MAAIntegration
{
    internal sealed class LauncherSettings
    {
        public string MaaDir { get; set; } = "";
        public List<string> Checked { get; set; } = new List<string>();
        public bool ForceExit { get; set; } = true;
        public bool AutoRun { get; set; } = true;
        public bool LowPriority { get; set; } = true;
        public bool SkipAutoRun { get; set; } = false;
    }

    internal sealed class MainForm : Form
    {
        private const string DefaultMaaDir = @"C:\Users\lenovo\Downloads\MAA";

        private readonly string _appDir;
        private readonly string _settingsFile;

        private TextBox _dirBox;
        private CheckedListBox _list;
        private CheckBox _forceExit;
        private CheckBox _autoRun;
        private CheckBox _lowPriority;
        private CheckBox _skipAutoRun;
        private Button _start;
        private Button _stop;
        private Button _browse;
        private Button _refresh;
        private ProgressBar _bar;
        private Label _status;
        private TextBox _log;

        private CancellationTokenSource _cts;

        public MainForm()
        {
            _appDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
            _settingsFile = Path.Combine(_appDir, "MAA-Integration.settings.json");

            BuildUi();
            LoadSettings();

            string restoreMessage = ConfigStore.TryRestoreJournal(JournalPath);
            if (!string.IsNullOrEmpty(restoreMessage)) AppendLog("! " + restoreMessage);

            RefreshConfigs();
        }

        /// <summary>标题栏 / 任务栏图标：读嵌入的 app.ico。</summary>
        private void ApplyEmbeddedIcon()
        {
            try
            {
                using (Stream s = typeof(MainForm).Assembly.GetManifestResourceStream("app.ico"))
                {
                    if (s != null) Icon = new Icon(s);
                }
            }
            catch { }
        }

        private string JournalPath
        {
            get { return Path.Combine(_appDir, "MAA-Integration.pending-restore.json"); }
        }

        private void BuildUi()
        {
            Text = "MAA 多配置启动器";
            AutoScaleMode = AutoScaleMode.None;
            ApplyEmbeddedIcon();
            ClientSize = new Size(640, 658);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

            var lblDir = new Label { Text = "MAA 目录", Location = new Point(14, 17), Size = new Size(70, 20) };

            _dirBox = new TextBox
            {
                Location = new Point(86, 13),
                Size = new Size(428, 25),
                ReadOnly = true,
                BackColor = SystemColors.Window
            };

            _browse = new Button { Text = "浏览…", Location = new Point(524, 12), Size = new Size(98, 27) };
            _browse.Click += (s, e) => BrowseMaaDir();

            var grp = new GroupBox
            {
                Text = "选择要运行的配置（按列表顺序依次运行）",
                Location = new Point(14, 50),
                Size = new Size(608, 220)
            };

            _list = new CheckedListBox
            {
                Location = new Point(12, 24),
                Size = new Size(584, 152),
                CheckOnClick = true,
                IntegralHeight = false
            };

            _refresh = new Button { Text = "刷新列表", Location = new Point(496, 182), Size = new Size(100, 28) };
            _refresh.Click += (s, e) => RefreshConfigs();

            var hint = new Label
            {
                Text = "每个配置会单独启动一次 MAA；上一个 MAA 退出后才启动下一个。",
                Location = new Point(12, 186),
                Size = new Size(470, 20),
                ForeColor = SystemColors.GrayText
            };

            grp.Controls.AddRange(new Control[] { _list, _refresh, hint });

            _forceExit = new CheckBox
            {
                Text = "运行完自动退出 MAA（需要时给“任务完成后操作”补上 退出 MAA，其它动作保留）",
                Location = new Point(14, 280),
                Size = new Size(580, 24),
                Checked = true
            };

            _autoRun = new CheckBox
            {
                Text = "临时打开“启动 MAA 后自动运行”（配了关闭的话，任务不会自己跑、也就不会自己退出）",
                Location = new Point(14, 308),
                Size = new Size(580, 24),
                Checked = true
            };

            _lowPriority = new CheckBox
            {
                Text = "以低优先级运行 MAA（CPU 让给浏览器/游戏，录播、直播不容易卡）",
                Location = new Point(14, 364),
                Size = new Size(580, 24),
                Checked = true
            };

            _skipAutoRun = new CheckBox
            {
                Text = "启动后跳过自动运行（--skip-startup-auto-run，只开界面不跑任务）",
                Location = new Point(14, 336),
                Size = new Size(580, 24)
            };

            // 这两项互斥：要么让它自动跑，要么让它只开界面
            _autoRun.CheckedChanged += (s, e) =>
            {
                if (_autoRun.Checked && _skipAutoRun.Checked) _skipAutoRun.Checked = false;
            };
            _skipAutoRun.CheckedChanged += (s, e) =>
            {
                if (_skipAutoRun.Checked && _autoRun.Checked) _autoRun.Checked = false;
            };

            _start = new Button { Text = "开始运行", Location = new Point(14, 400), Size = new Size(132, 34) };
            _start.Click += async (s, e) => await StartAsync();

            _stop = new Button { Text = "停止", Location = new Point(158, 400), Size = new Size(100, 34), Enabled = false };
            _stop.Click += (s, e) => Stop();

            _status = new Label { Text = "就绪", Location = new Point(14, 442), Size = new Size(608, 20) };

            _bar = new ProgressBar { Location = new Point(14, 466), Size = new Size(608, 16) };

            _log = new TextBox
            {
                Location = new Point(14, 492),
                Size = new Size(608, 150),
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = Color.White,
                WordWrap = false
            };

            Controls.AddRange(new Control[]
            {
                lblDir, _dirBox, _browse, grp, _forceExit, _autoRun, _lowPriority, _skipAutoRun, _start, _stop, _status, _bar, _log
            });

            FormClosing += OnFormClosing;
        }

        // 布局按 96 DPI 写死；高 DPI 时整体等比放大，文字保持清晰（不做位图拉伸）
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            float scale = DeviceDpi / 96f;
            if (scale > 1.05f)
            {
                SuspendLayout();
                ScaleControlsIn(this, scale);
                ClientSize = new Size(
                    (int)Math.Round(ClientSize.Width * scale),
                    (int)Math.Round(ClientSize.Height * scale));
                ResumeLayout(true);
            }

            CenterToScreen();
        }

        private static void ScaleControlsIn(Control parent, float scale)
        {
            foreach (Control c in parent.Controls)
            {
                c.Location = new Point(
                    (int)Math.Round(c.Left * scale),
                    (int)Math.Round(c.Top * scale));
                c.Size = new Size(
                    Math.Max(1, (int)Math.Round(c.Width * scale)),
                    Math.Max(1, (int)Math.Round(c.Height * scale)));
                ScaleControlsIn(c, scale);
            }
        }

        private Workspace Workspace
        {
            get { return new Workspace { Dir = _dirBox.Text }; }
        }

        // ---------------- 设置 ----------------

        private void LoadSettings()
        {
            string dir = DefaultMaaDir;

            if (File.Exists(_settingsFile))
            {
                try
                {
                    var s = JsonSerializer.Deserialize<LauncherSettings>(File.ReadAllText(_settingsFile, Encoding.UTF8));
                    if (s != null)
                    {
                        if (!string.IsNullOrWhiteSpace(s.MaaDir)) dir = s.MaaDir;
                        _forceExit.Checked = s.ForceExit;
                        _skipAutoRun.Checked = s.SkipAutoRun;
                        _autoRun.Checked = s.AutoRun;
                        _lowPriority.Checked = s.LowPriority;
                        if (s.Checked != null) _savedChecked = s.Checked;
                    }
                }
                catch { }
            }

            _dirBox.Text = dir;
        }

        private List<string> _savedChecked = new List<string>();
        private bool _loadedOnce;

        private void SaveSettings()
        {
            try
            {
                var s = new LauncherSettings
                {
                    MaaDir = _dirBox.Text,
                    Checked = CheckedNames(),
                    ForceExit = _forceExit.Checked,
                    AutoRun = _autoRun.Checked,
                    LowPriority = _lowPriority.Checked,
                    SkipAutoRun = _skipAutoRun.Checked
                };
                File.WriteAllText(_settingsFile,
                    JsonSerializer.Serialize(s, new JsonSerializerOptions
                    {
                        WriteIndented = true,
                        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                    }),
                    new UTF8Encoding(false));
            }
            catch { }
        }

        private List<string> CheckedNames()
        {
            var names = new List<string>();
            foreach (object o in _list.CheckedItems)
            {
                var c = o as MaaConfig;
                if (c != null) names.Add(c.Name);
            }
            return names;
        }

        // ---------------- 配置列表 ----------------

        private void BrowseMaaDir()
        {
            using (var dlg = new FolderBrowserDialog())
            {
                dlg.Description = "选择 MAA 所在目录（包含 MAA.exe 的目录）";
                if (Directory.Exists(_dirBox.Text)) dlg.SelectedPath = _dirBox.Text;
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    _dirBox.Text = dlg.SelectedPath;
                    RefreshConfigs();
                    SaveSettings();
                }
            }
        }

        private void RefreshConfigs()
        {
            var ws = Workspace;
            var keep = CheckedNames();
            var previous = new List<string>();
            foreach (object o in _list.Items)
            {
                var c = o as MaaConfig;
                if (c != null) previous.Add(c.Name);
            }
            bool useSavedDefaults = !_loadedOnce && keep.Count == 0 && _savedChecked.Count > 0;

            string current, error;
            var configs = ConfigStore.Load(ws, out current, out error);

            _list.BeginUpdate();
            _list.Items.Clear();

            foreach (var cfg in configs)
            {
                bool isChecked;
                if (previous.Contains(cfg.Name))
                    isChecked = keep.Contains(cfg.Name);          // 保留用户当前的勾选
                else if (useSavedDefaults)
                    isChecked = _savedChecked.Contains(cfg.Name); // 上次关闭时的勾选
                else
                    isChecked = !IsTimestampName(cfg.Name);       // 新配置：默认勾上（时间戳那种除外）

                _list.Items.Add(cfg, isChecked);
            }
            _list.EndUpdate();
            _loadedOnce = true;

            if (error.Length > 0)
            {
                AppendLog("! " + error);
                SetStatus("读取配置失败");
                return;
            }

            AppendLog("已载入 " + configs.Count + " 个配置，当前配置：" + (current.Length == 0 ? "（未知）" : current));
            if (!ws.Exists) AppendLog("! 该目录下没有 MAA.exe：" + ws.ExePath);
            SetStatus("就绪");
        }

        private static bool IsTimestampName(string name)
        {
            // MAA 自动生成的配置名形如 "25/12/05 14:47:01"
            if (name.Length < 8) return false;
            return char.IsDigit(name[0]) && char.IsDigit(name[1]) && name[2] == '/' ;
        }

        // ---------------- 运行 ----------------

        private async Task StartAsync()
        {
            if (_cts != null) return;

            var ws = Workspace;
            var names = CheckedNames();

            if (!ws.Exists)
            {
                MessageBox.Show(this, "这个目录里没有 MAA.exe：\n" + ws.ExePath + "\n\n请用“浏览…”选择正确的 MAA 目录。",
                    "找不到 MAA", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (names.Count == 0)
            {
                MessageBox.Show(this, "请至少勾选一个配置。", "未选择配置",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (MaaProcess.Find(ws.ExePath).Count > 0)
            {
                MessageBox.Show(this, "MAA 已经在运行。\n\n同一安装目录只允许一个实例，带 --config 启动只会唤醒旧窗口、配置不会切换。\n请先完全退出 MAA 再开始。",
                    "MAA 正在运行", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // 和 MAA 启动时的检查一样：先试写一次。写不进去的话，MAA 会弹“无法保存数据”
            string writeError;
            if (!ws.ProbeWritable(out writeError))
            {
                AppendLog("! 当前运行环境写不进 MAA 目录：" + writeError);
                DialogResult answer = MessageBox.Show(this,
                    "这个程序现在没有写入 MAA 目录的权限：\n" + ws.Dir +
                    "\n\n最可能的原因：本程序是被某个受限的进程（沙箱、AI 助手的终端窗口、受限快捷方式等）拉起来的，" +
                    "而不是你在资源管理器里双击打开的。这种令牌下启动的 MAA 也会写不进自己的目录，" +
                    "于是弹出“当前软件所在的位置无法保存数据”。\n\n" +
                    "建议：关掉本窗口，直接在资源管理器里双击 MAA-Integration.exe 重新打开。\n\n" +
                    "仍然继续吗？（继续的话 MAA 可能无法保存设置）",
                    "没有写权限", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);

                if (answer != DialogResult.Yes)
                {
                    SetStatus("已取消（无写权限）");
                    return;
                }
            }

            _cts = new CancellationTokenSource();
            SetRunningState(true);
            _bar.Maximum = Math.Max(1, names.Count);
            _bar.Value = 0;

            var opt = new RunnerOptions
            {
                ForceExit = _forceExit.Checked,
                ForceAutoRun = _autoRun.Checked,
                LowPriority = _lowPriority.Checked,
                SkipAutoRun = _skipAutoRun.Checked,
                JournalFile = JournalPath
            };

            var runner = new Runner(ws, names, opt, AppendLog, UpdateProgress, _cts.Token);

            try
            {
                await runner.RunAsync();
                SetStatus("全部完成");
            }
            catch (OperationCanceledException)
            {
                AppendLog("已停止（当前 MAA 不会被关闭）。");
                SetStatus("已停止");
            }
            catch (Exception ex)
            {
                AppendLog("! 出错：" + ex.Message);
                SetStatus("出错");
                MessageBox.Show(this, ex.Message, "运行出错", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _cts.Dispose();
                _cts = null;
                SetRunningState(false);
                SaveSettings();
            }
        }

        private void Stop()
        {
            if (_cts == null) return;
            _stop.Enabled = false;
            SetStatus("正在停止……");
            try { _cts.Cancel(); } catch { }
        }

        private void UpdateProgress(int index, int total, string name)
        {
            if (IsDisposed || !IsHandleCreated) return;
            if (InvokeRequired)
            {
                BeginInvoke(new Action<int, int, string>(UpdateProgress), index, total, name);
                return;
            }

            _bar.Maximum = Math.Max(1, total);
            _bar.Value = Math.Min(index, _bar.Maximum);
            _status.Text = "正在运行 " + index + "/" + total + "：" + name;
        }

        private void SetRunningState(bool running)
        {
            _start.Enabled = !running;
            _browse.Enabled = !running;
            _refresh.Enabled = !running;
            _list.Enabled = !running;
            _forceExit.Enabled = !running;
            _autoRun.Enabled = !running;
            _lowPriority.Enabled = !running;
            _skipAutoRun.Enabled = !running;
            _stop.Enabled = running;
        }

        private void SetStatus(string text)
        {
            if (IsDisposed || !IsHandleCreated) return;
            if (InvokeRequired)
            {
                BeginInvoke(new Action<string>(SetStatus), text);
                return;
            }
            _status.Text = text;
        }

        private void AppendLog(string line)
        {
            if (_log == null) return;
            if (IsDisposed || !IsHandleCreated) return;

            if (_log.InvokeRequired)
            {
                try { _log.BeginInvoke(new Action<string>(AppendLog), line); } catch { }
                return;
            }

            _log.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + line + Environment.NewLine);
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (_cts != null)
            {
                e.Cancel = true;
                MessageBox.Show(this, "正在运行中，请先点“停止”，等当前 MAA 退出后再关闭。",
                    "正在运行", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            SaveSettings();
        }
    }
}
