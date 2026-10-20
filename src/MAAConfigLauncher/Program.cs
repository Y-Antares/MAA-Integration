using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace MAAConfigLauncher
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length >= 3 && args[0] == "--selftest")
                return SelfTest(args);

            if (args.Length >= 3 && args[0] == "--probe")
            {
                var ws = new Workspace { Dir = args[1] };
                string err;
                bool ok = ws.ProbeWritable(out err);
                File.WriteAllText(args[2], ok ? "WRITABLE" : "NOT-WRITABLE: " + err, new UTF8Encoding(false));
                return ok ? 0 : 3;
            }

            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
            return 0;
        }

        /// <summary>
        /// 无界面自检：--selftest &lt;MaaDir&gt; &lt;日志文件&gt; [--forceexit] [--quiet=N]
        /// 用同一套 Runner 跑完所有配置，日志写文件，便于自动化验证。
        /// </summary>
        private static int SelfTest(string[] args)
        {
            string dir = args[1];
            string logPath = args[2];
            bool forceExit = args.Contains("--forceexit");
            bool forceAutoRun = args.Contains("--autorun");
            bool lowPriority = args.Contains("--lowpriority");
            int quiet = 3;
            foreach (string a in args)
                if (a.StartsWith("--quiet=", StringComparison.Ordinal))
                    int.TryParse(a.Substring("--quiet=".Length), out quiet);

            var sb = new StringBuilder();
            var gate = new object();

            void Log(string line)
            {
                lock (gate)
                {
                    sb.AppendLine(line);
                    File.WriteAllText(logPath, sb.ToString(), new UTF8Encoding(false));
                }
            }

            var ws = new Workspace { Dir = dir };
            Log("MAA 目录：" + ws.Dir);
            Log("MAA.exe 存在：" + ws.Exists);

            string beforeText = File.Exists(ws.ConfigFile) ? File.ReadAllText(ws.ConfigFile) : "";
            string beforeHash = File.Exists(ws.ConfigFile) ? Hash(ws.ConfigFile) : "(none)";
            Log("配置哈希(前)：" + beforeHash);

            string current, error;
            var configs = ConfigStore.Load(ws, out current, out error);
            if (error.Length > 0) { Log("错误：" + error); return 1; }

            Log("当前配置：" + current);
            Log("配置数量：" + configs.Count);

            var names = configs.Select(c => c.Name).ToList();
            var opt = new RunnerOptions
            {
                ForceExit = forceExit,
                ForceAutoRun = forceAutoRun,
                LowPriority = lowPriority,
                QuietSeconds = quiet,
                JournalFile = logPath + ".journal.json"
            };
            var runner = new Runner(ws, names, opt, Log,
                (i, n, name) => Log("进度 " + i + "/" + n + " " + name),
                CancellationToken.None);

            try
            {
                runner.RunAsync().GetAwaiter().GetResult();
                Log("RESULT OK");
            }
            catch (Exception ex)
            {
                Log("RESULT FAIL: " + ex.Message);
            }

            string afterText = File.Exists(ws.ConfigFile) ? File.ReadAllText(ws.ConfigFile) : "";
            string afterHash = File.Exists(ws.ConfigFile) ? Hash(ws.ConfigFile) : "(none)";
            Log("配置哈希(后)：" + afterHash);

            bool restored = false;
            try
            {
                restored = System.Text.Json.Nodes.JsonNode.DeepEquals(
                    System.Text.Json.Nodes.JsonNode.Parse(beforeText),
                    System.Text.Json.Nodes.JsonNode.Parse(afterText));
            }
            catch { }
            Log("配置已恢复(内容一致)：" + restored);
            return restored ? 0 : 2;
        }

        private static string Hash(string path)
        {
            using (var sha = SHA256.Create())
            using (var fs = File.OpenRead(path))
                return Convert.ToHexString(sha.ComputeHash(fs));
        }
    }
}
