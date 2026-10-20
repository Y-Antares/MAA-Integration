using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace MAAIntegration
{
    /// <summary>一个 MAA 配置（config\gui.new.json 里 Configurations 下的一个键）。</summary>
    internal sealed class MaaConfig
    {
        public string Name = "";
        public string Account = "";
        public string PostActions = "";
        public bool HasExitSelf;
        public bool? RunDirectly;
        public bool IsCurrent;

        public string AutoRunText
        {
            get
            {
                if (RunDirectly == null) return "未设置";
                return RunDirectly.Value ? "开" : "关";
            }
        }

        public override string ToString()
        {
            string text = string.IsNullOrEmpty(Account) ? Name : Name + "（" + Account + "）";
            text += "   自动运行：" + AutoRunText;
            if (IsCurrent) text += "  ← 当前";
            return text;
        }
    }

    /// <summary>MAA 安装目录。</summary>
    internal sealed class Workspace
    {
        public string Dir = "";

        public string ExePath { get { return Path.Combine(Dir, "MAA.exe"); } }
        public string ConfigFile { get { return Path.Combine(Dir, "config", "gui.new.json"); } }
        public string LegacyConfig { get { return Path.Combine(Dir, "config", "gui.json"); } }
        public bool Exists { get { return File.Exists(ExePath); } }

        /// <summary>
        /// 和 MAA 自己启动时的检查完全一致：在目录里写一个临时文件再删掉。
        /// 失败说明这个进程（以及它启动的 MAA）没有写权限 —— MAA 会弹“无法保存数据”。
        /// </summary>
        public bool ProbeWritable(out string error)
        {
            error = "";
            if (string.IsNullOrWhiteSpace(Dir) || !Directory.Exists(Dir))
            {
                error = "目录不存在：" + Dir;
                return false;
            }

            try
            {
                string probe = Path.Combine(Dir, "write_probe.tmp");
                File.WriteAllText(probe, "test");
                File.Delete(probe);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }
    }

    /// <summary>记录“为了这次批量运行而临时改掉的配置项”，用于跑完恢复。</summary>
    internal sealed class ConfigPatch
    {
        public readonly Dictionary<string, string> PostActions = new Dictionary<string, string>(StringComparer.Ordinal);
        public readonly Dictionary<string, bool?> RunDirectly = new Dictionary<string, bool?>(StringComparer.Ordinal);

        public bool IsEmpty { get { return PostActions.Count == 0 && RunDirectly.Count == 0; } }

        public int Count { get { return PostActions.Count + RunDirectly.Count; } }
    }

    /// <summary>读写 gui.new.json。</summary>
    internal static class ConfigStore
    {
        public static readonly JsonSerializerOptions WriteOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            IndentSize = 2,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        public static string ReadAllTextWithRetry(string path, int attempts = 12)
        {
            Exception last = null;
            for (int i = 0; i < attempts; i++)
            {
                try { return File.ReadAllText(path, Encoding.UTF8); }
                catch (Exception ex) { last = ex; Thread.Sleep(250); }
            }
            throw new IOException("读取失败：" + path, last);
        }

        public static void WriteAllTextWithRetry(string path, string text, int attempts = 12)
        {
            Exception last = null;
            for (int i = 0; i < attempts; i++)
            {
                try
                {
                    string tmp = path + ".launcher.tmp";
                    File.WriteAllText(tmp, text, new UTF8Encoding(false));
                    File.Copy(tmp, path, true);
                    try { File.Delete(tmp); } catch { }
                    return;
                }
                catch (Exception ex) { last = ex; Thread.Sleep(250); }
            }
            throw new IOException("写入失败：" + path, last);
        }

        public static JsonObject ReadRoot(string file)
        {
            return (JsonObject)JsonNode.Parse(ReadAllTextWithRetry(file));
        }

        /// <summary>读取全部配置；失败时返回空列表并给出原因。</summary>
        public static List<MaaConfig> Load(Workspace ws, out string current, out string error)
        {
            current = "";
            error = "";
            var list = new List<MaaConfig>();

            if (!File.Exists(ws.ConfigFile))
            {
                error = "找不到配置文件：" + ws.ConfigFile;
                return list;
            }

            JsonObject root;
            try { root = ReadRoot(ws.ConfigFile); }
            catch (Exception ex) { error = "解析配置失败：" + ex.Message; return list; }

            try
            {
                if (root["Current"] != null) current = root["Current"].GetValue<string>();
            }
            catch { }

            if (root["Configurations"] is not JsonObject configs)
            {
                error = "配置文件里没有 Configurations 节点。";
                return list;
            }

            foreach (var pair in configs)
            {
                if (pair.Value is not JsonObject cfg) continue;

                var item = new MaaConfig { Name = pair.Key, IsCurrent = pair.Key == current };

                if (cfg["Gui"] is JsonObject gui)
                {
                    item.PostActions = ReadString(gui["PostActions"]);
                    item.HasExitSelf = SplitFlags(item.PostActions).Contains("ExitSelf");

                    if (gui["StartUpSettings"] is JsonObject startup)
                    {
                        try
                        {
                            if (startup["RunDirectly"] != null)
                                item.RunDirectly = startup["RunDirectly"].GetValue<bool>();
                        }
                        catch { }
                    }
                }

                try
                {
                    if (cfg["TaskQueue"] is JsonArray tasks)
                    {
                        foreach (var t in tasks)
                        {
                            if (t is JsonObject task &&
                                task["TaskType"] != null &&
                                task["TaskType"].GetValue<string>() == "StartUp")
                            {
                                if (task["AccountName"] != null)
                                    item.Account = task["AccountName"].GetValue<string>();
                                break;
                            }
                        }
                    }
                }
                catch { }

                list.Add(item);
            }

            return list;
        }

        public static List<string> SplitFlags(string value)
        {
            var flags = new List<string>();
            if (string.IsNullOrWhiteSpace(value)) return flags;
            foreach (string part in value.Split(','))
            {
                string f = part.Trim();
                if (f.Length > 0) flags.Add(f);
            }
            return flags;
        }

        private static string ReadString(JsonNode node)
        {
            if (node == null) return "";
            try { return node.GetValue<string>(); }
            catch { return node.ToJsonString().Trim('"'); }
        }

        /// <summary>
        /// 按需临时修改被选中配置：
        ///   addExitSelf    —— 在“任务完成后操作”里补上 ExitSelf（多值，保留原有动作，已有则不动）
        ///   forceAutoRun   —— 把“启动 MAA 后自动运行”打开（Restore 时可还原）
        /// 返回记录，用来恢复。
        /// </summary>
        public static ConfigPatch Patch(Workspace ws, IEnumerable<string> names, bool addExitSelf, bool forceAutoRun)
        {
            var patch = new ConfigPatch();
            if (!addExitSelf && !forceAutoRun) return patch;

            var root = ReadRoot(ws.ConfigFile);
            if (root["Configurations"] is not JsonObject configs)
                throw new InvalidOperationException("配置文件里没有 Configurations 节点。");

            foreach (string name in names)
            {
                if (configs[name] is not JsonObject cfg) continue;

                if (cfg["Gui"] is not JsonObject gui)
                {
                    gui = new JsonObject();
                    cfg["Gui"] = gui;
                }

                if (addExitSelf)
                {
                    string old = ReadString(gui["PostActions"]);
                    var flags = SplitFlags(old);
                    if (!flags.Contains("ExitSelf"))
                    {
                        patch.PostActions[name] = old;
                        flags.Add("ExitSelf");
                        gui["PostActions"] = string.Join(", ", flags);
                    }
                }

                if (forceAutoRun)
                {
                    if (gui["StartUpSettings"] is not JsonObject startup)
                    {
                        startup = new JsonObject();
                        gui["StartUpSettings"] = startup;
                    }

                    bool current = true;
                    if (startup["RunDirectly"] != null)
                    {
                        try { current = startup["RunDirectly"].GetValue<bool>(); } catch { }
                    }

                    if (!current)
                    {
                        bool? original = null;
                        if (startup["RunDirectly"] != null)
                        {
                            try { original = startup["RunDirectly"].GetValue<bool>(); } catch { }
                        }
                        patch.RunDirectly[name] = original;
                        startup["RunDirectly"] = true;
                    }
                }
            }

            if (!patch.IsEmpty)
                WriteAllTextWithRetry(ws.ConfigFile, root.ToJsonString(WriteOptions));

            return patch;
        }

        /// <summary>把 Patch 改过的配置项恢复原样。</summary>
        public static bool Restore(Workspace ws, ConfigPatch patch)
        {
            if (patch == null || patch.IsEmpty) return true;

            try
            {
                var root = ReadRoot(ws.ConfigFile);
                if (root["Configurations"] is not JsonObject configs) return false;

                foreach (var pair in patch.PostActions)
                {
                    if (configs[pair.Key] is not JsonObject cfg) continue;
                    if (cfg["Gui"] is not JsonObject gui) continue;
                    gui["PostActions"] = JsonValue.Create(pair.Value);
                }

                foreach (var pair in patch.RunDirectly)
                {
                    if (configs[pair.Key] is not JsonObject cfg) continue;
                    if (cfg["Gui"] is not JsonObject gui) continue;
                    if (gui["StartUpSettings"] is not JsonObject startup) continue;

                    if (pair.Value.HasValue) startup["RunDirectly"] = JsonValue.Create(pair.Value.Value);
                    else startup.Remove("RunDirectly");
                }

                WriteAllTextWithRetry(ws.ConfigFile, root.ToJsonString(WriteOptions));
                return true;
            }
            catch
            {
                return false;
            }
        }

        // ---------- 被强杀时的兜底记录 ----------

        public static void SaveJournal(string journalFile, Workspace ws, ConfigPatch patch)
        {
            if (patch == null || patch.IsEmpty) return;

            var sa = new JsonObject();
            foreach (var pair in patch.PostActions) sa[pair.Key] = pair.Value;

            var rd = new JsonObject();
            foreach (var pair in patch.RunDirectly)
                rd[pair.Key] = pair.Value.HasValue ? JsonValue.Create(pair.Value.Value) : null;

            var obj = new JsonObject
            {
                ["Dir"] = ws.Dir,
                ["PostActions"] = sa,
                ["RunDirectly"] = rd
            };

            File.WriteAllText(journalFile, obj.ToJsonString(WriteOptions), new UTF8Encoding(false));
        }

        public static void DeleteJournal(string journalFile)
        {
            try { if (File.Exists(journalFile)) File.Delete(journalFile); } catch { }
        }

        public static string TryRestoreJournal(string journalFile)
        {
            if (!File.Exists(journalFile)) return null;

            try
            {
                var root = (JsonObject)JsonNode.Parse(File.ReadAllText(journalFile, Encoding.UTF8));
                string dir = root["Dir"] != null ? root["Dir"].GetValue<string>() : "";
                if (string.IsNullOrWhiteSpace(dir)) { DeleteJournal(journalFile); return null; }

                var patch = new ConfigPatch();

                if (root["PostActions"] is JsonObject sa)
                    foreach (var pair in sa)
                        patch.PostActions[pair.Key] = ReadString(pair.Value);

                if (root["RunDirectly"] is JsonObject rd)
                    foreach (var pair in rd)
                    {
                        bool? v = null;
                        if (pair.Value != null)
                        {
                            try { v = pair.Value.GetValue<bool>(); } catch { v = null; }
                        }
                        patch.RunDirectly[pair.Key] = v;
                    }

                var ws = new Workspace { Dir = dir };
                if (!File.Exists(ws.ConfigFile)) { DeleteJournal(journalFile); return null; }

                bool ok = Restore(ws, patch);
                if (ok) DeleteJournal(journalFile);

                return ok
                    ? "检测到上次运行没有正常收尾，已恢复 " + patch.Count + " 处临时改动（完成后操作 / 自动运行）。"
                    : "检测到上次运行没有正常收尾，但恢复配置失败，请检查 config\\gui.new.json。";
            }
            catch (Exception ex)
            {
                return "恢复上次残留的配置改动失败：" + ex.Message;
            }
        }
    }

    /// <summary>进程与命令行工具。</summary>
    internal static class MaaProcess
    {
        // PID -> 是否属于本 MAA 目录。读 MainModule 代价不低（遇到卡死/已退出但句柄未释放的
        // 同名进程时单次要 ~100 ms），所以每个 PID 只查一次，之后走缓存。
        private static readonly Dictionary<int, bool> PathCache = new Dictionary<int, bool>();
        private static string _cachedTarget = "";

        public static List<Process> Find(string exePath)
        {
            var found = new List<Process>();
            string target;
            try { target = Path.GetFullPath(exePath); }
            catch { return found; }

            if (!string.Equals(target, _cachedTarget, StringComparison.OrdinalIgnoreCase))
            {
                PathCache.Clear();
                _cachedTarget = target;
            }

            string name = Path.GetFileNameWithoutExtension(exePath);
            Process[] all;
            try { all = Process.GetProcessesByName(name); }
            catch { return found; }

            var alive = new HashSet<int>();

            foreach (var p in all)
            {
                int pid;
                try { pid = p.Id; } catch { continue; }
                alive.Add(pid);

                // 已经退出、只是句柄还没释放的“僵尸”进程直接跳过（HasExited 约 0.3 ms）
                try { if (p.HasExited) { PathCache.Remove(pid); continue; } }
                catch { continue; }

                bool ours;
                if (!PathCache.TryGetValue(pid, out ours))
                {
                    ours = false;
                    try
                    {
                        string pPath = p.MainModule.FileName;
                        if (!string.IsNullOrWhiteSpace(pPath))
                            ours = string.Equals(Path.GetFullPath(pPath), target, StringComparison.OrdinalIgnoreCase);
                    }
                    catch { }
                    PathCache[pid] = ours;
                }

                if (ours) found.Add(p);
            }

            if (PathCache.Count > alive.Count)
            {
                foreach (int pid in PathCache.Keys.Where(k => !alive.Contains(k)).ToList())
                    PathCache.Remove(pid);
            }

            return found;
        }

        public static string BuildArguments(IEnumerable<string> items)
        {
            var parts = new List<string>();
            foreach (string item in items)
            {
                if (item.Length == 0) parts.Add("\"\"");
                else if (item.IndexOf(' ') >= 0 || item.IndexOf('\t') >= 0 || item.IndexOf('"') >= 0)
                    parts.Add("\"" + item.Replace("\"", "\\\"") + "\"");
                else parts.Add(item);
            }
            return string.Join(" ", parts);
        }

        public static Process Launch(Workspace ws, IEnumerable<string> items, bool lowPriority)
        {
            var psi = new ProcessStartInfo
            {
                FileName = ws.ExePath,
                WorkingDirectory = ws.Dir,
                UseShellExecute = false,
                CreateNoWindow = true,
                Arguments = BuildArguments(items)
            };

            Process proc = Process.Start(psi);

            if (lowPriority && proc != null)
            {
                // 子进程会继承优先级类别，MAA 自己重启出来的新进程同样是 BelowNormal
                try { proc.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
            }

            return proc;
        }
    }

    internal sealed class RunnerOptions
    {
        public bool ForceExit = true;       // 需要时给 PostActions 补 ExitSelf
        public bool ForceAutoRun = true;    // 临时打开“启动后自动运行”
        public bool SkipAutoRun = false;    // 传 --skip-startup-auto-run
        public bool LowPriority = false;    // 以“低于正常”优先级启动 MAA
        public int QuietSeconds = 8;
        public string JournalFile = "";
    }

    /// <summary>依次启动配置的完整流程。</summary>
    internal sealed class Runner
    {
        private readonly Workspace _ws;
        private readonly List<string> _names;
        private readonly RunnerOptions _opt;
        private readonly Action<string> _log;
        private readonly Action<int, int, string> _progress;
        private readonly CancellationToken _ct;

        public Runner(Workspace ws, IEnumerable<string> names, RunnerOptions opt,
                      Action<string> log, Action<int, int, string> progress, CancellationToken ct)
        {
            _ws = ws;
            _names = names.ToList();
            _opt = opt ?? new RunnerOptions();
            _log = log ?? delegate { };
            _progress = progress ?? delegate { };
            _ct = ct;
        }

        public async Task RunAsync()
        {
            if (!_ws.Exists)
                throw new InvalidOperationException("找不到 MAA.exe：" + _ws.ExePath);

            if (_names.Count == 0)
                throw new InvalidOperationException("没有选择任何配置。");

            if (MaaProcess.Find(_ws.ExePath).Count > 0)
                throw new InvalidOperationException("MAA 已经在运行。请先完全退出 MAA 再开始（同一安装目录只允许一个实例）。");

            bool wantAutoRun = _opt.ForceAutoRun && !_opt.SkipAutoRun;
            bool wantPatch = _opt.ForceExit || wantAutoRun;
            ConfigPatch patch = null;

            if (!wantPatch)
            {
                _log("未做任何临时改动：完全按 MAA 现有设置运行。");
                _log("→ 任务不会自动开始，也不会自动退出；需要你在 MAA 里点“开始”，跑完关掉窗口才会继续下一个。");
            }
            else
            {
                try
                {
                    patch = ConfigStore.Patch(_ws, _names, _opt.ForceExit, wantAutoRun);
                }
                catch (Exception ex)
                {
                    _log("! 临时修改配置失败：" + ex.Message);
                    _log("  将以配置里的原样运行（可能不会自动跑任务、也不会自动退出）。");
                    patch = null;
                }

                if (patch != null)
                {
                    if (patch.IsEmpty)
                    {
                        _log("这些配置已经满足要求（完成后操作已含 退出 MAA、自动运行已开启），无需改动。");
                    }
                    else
                    {
                        if (patch.PostActions.Count > 0)
                            _log("已给 " + patch.PostActions.Count + " 个配置的“任务完成后操作”补上 退出 MAA（其它动作保留）。");
                        if (patch.RunDirectly.Count > 0)
                            _log("已临时打开 " + patch.RunDirectly.Count + " 个配置的“启动 MAA 后自动运行”。");
                        _log("（跑完会恢复成你原来的设置）");

                        try
                        {
                            if (!string.IsNullOrEmpty(_opt.JournalFile))
                                ConfigStore.SaveJournal(_opt.JournalFile, _ws, patch);
                        }
                        catch { }
                    }
                }

                if (wantAutoRun && !_opt.ForceExit)
                    _log("提示：只开了自动运行、没开自动退出 —— MAA 跑完任务不会自己关窗，需要你关掉才会继续下一个。");
                if (!wantAutoRun && _opt.ForceExit)
                    _log("提示：没开自动运行 —— MAA 启动后不会自己跑任务，请手动点“开始”；跑完才会自动退出。");
            }

            try
            {
                for (int i = 0; i < _names.Count; i++)
                {
                    _ct.ThrowIfCancellationRequested();

                    string name = _names[i];
                    _progress(i + 1, _names.Count, name);
                    _log("");
                    _log("===== 第 " + (i + 1) + "/" + _names.Count + " 个配置：" + name);

                    await RunOneAsync(name);

                    if (i < _names.Count - 1)
                        await Task.Delay(1500, _ct);
                }

                _log("");
                _log("全部 " + _names.Count + " 个配置已依次运行完毕。");
            }
            finally
            {
                if (patch != null && !patch.IsEmpty)
                {
                    try
                    {
                        await Task.Delay(1000);
                        if (ConfigStore.Restore(_ws, patch))
                        {
                            _log("已恢复临时改动的配置项。");
                            if (!string.IsNullOrEmpty(_opt.JournalFile))
                                ConfigStore.DeleteJournal(_opt.JournalFile);
                        }
                        else
                        {
                            _log("! 恢复配置失败，请检查 config\\gui.new.json。");
                        }
                    }
                    catch (Exception ex)
                    {
                        _log("! 恢复配置出错：" + ex.Message);
                    }
                }
            }
        }

        private async Task RunOneAsync(string name)
        {
            string current = "";
            try { current = ConfigStore.ReadRoot(_ws.ConfigFile)["Current"]?.GetValue<string>() ?? ""; }
            catch { }

            if (current.Length > 0 && !string.Equals(current, name, StringComparison.Ordinal))
                _log("  当前配置是“" + current + "”，MAA 切换后会重启一次。");
            else if (current.Length > 0)
                _log("  该配置已经是当前配置，启动后无需切换。");

            var args = new List<string> { "--config", name };
            if (_opt.SkipAutoRun) args.Add("--skip-startup-auto-run");

            Process proc;
            try { proc = MaaProcess.Launch(_ws, args, _opt.LowPriority); }
            catch (Exception ex)
            {
                _log("  ! 启动失败：" + ex.Message);
                throw;
            }

            if (proc != null) _log("  已启动进程 PID " + proc.Id + "。");

            if (!await WaitAppearAsync(60))
            {
                _log("  ! 没有检测到 MAA 进程，跳过该配置。");
                return;
            }

            await WaitExitAsync();
        }

        private async Task<bool> WaitAppearAsync(int timeoutSeconds)
        {
            var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
            while (DateTime.UtcNow < deadline)
            {
                if (MaaProcess.Find(_ws.ExePath).Count > 0) return true;
                await Task.Delay(300);
            }
            return false;
        }

        private async Task WaitExitAsync()
        {
            _log("  等待 MAA 退出……");
            var lastSeen = DateTime.UtcNow;
            bool cancelled = false;

            while (true)
            {
                if (MaaProcess.Find(_ws.ExePath).Count > 0)
                {
                    lastSeen = DateTime.UtcNow;
                }
                else if ((DateTime.UtcNow - lastSeen).TotalSeconds >= _opt.QuietSeconds)
                {
                    _log("  MAA 已退出。");
                    return;
                }

                if (_ct.IsCancellationRequested)
                {
                    cancelled = true;
                    break;
                }

                await Task.Delay(1000);
            }

            if (cancelled)
                _log("  已停止等待（MAA 仍在运行，不会被关闭）。");
        }
    }
}
