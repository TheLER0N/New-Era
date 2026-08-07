// BuildCheck.cs — Build Gate: build, verify, undo, reports, rollback, classifier
// New Era v7.2+ · C# 5 / .NET Framework 4.x
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;

class BuildResult
{
    public bool Success;
    public int ExitCode;
    public string Output;
    public string LogFile;
    public string ProjectRoot;
    public string Command;
    public ErrorInfo Error;
    public long DurationMs;
    public DateTime StartTime;
}

class ErrorInfo
{
    public string Type;
    public string Cause;
    public string NextStep;
    public bool CanAutoRepair;
}

class VerifyResult
{
    public bool Success;
    public string Status;
    public string Details;
    public BuildResult LastBuildResult;
}

class ProjectContext
{
    public string RootPath = "";
    public string ManifestPath = "";
    public bool ManifestExists = false;
    public string ManifestError = null;
    public bool Recognized = false;

    public string ProjectType = "auto";
    public string ProjectFile = null;

    public string BuildCmd = null;
    public string BuildArgs = null;
    public string BuildWorkdir = null;
    public int BuildTimeoutMs = 180000;
    public string CompilerPreference = null;

    public string RunCmd = null;
    public string RunArgs = null;
    public string RunWorkdir = null;
    public int RunTimeoutMs = 30000;
    public int[] ExpectedExitCodes = new int[] { 0 };
    public List<string> ExpectStdoutContains = new List<string>();
    public List<string> ExpectFiles = new List<string>();
    public string HealthCheck = null;

    public string VerifyMode = null;
    public string BaselineFile = null;
    public string VerifyScript = null;

    public bool RepairEnabled = true;
    public int RepairMaxRounds = 3;
    public bool RepairAutoApply = true;
    public bool RepairRollback = true;
    public List<string> AllowedPaths = new List<string>();
    public List<string> ForbiddenPaths = new List<string>();

    public string ReportDir = null;
    public bool IncludeLogTail = true;

    public int UndoMaxSnapshots = 50;
    public string SnapshotDir = null;
}

class ParsedArgs
{
    public string Path = null;
    public Dictionary<string, string> Options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public bool HasOption(string name)
    {
        return Options.ContainsKey(name);
    }

    public string GetOption(string name, string fallback)
    {
        string v;
        if (Options.TryGetValue(name, out v) && !string.IsNullOrEmpty(v)) return v;
        return fallback;
    }
}

partial class MainConsole
{
    static class BuildGate
    {
        public static bool SuppressPostApply = false;

        public static bool AfterEditEnabled = true;
        public static bool AutoRepair = true;
        public static int MaxRepairRounds = 3;

        public static string BuildCmd = "auto";
        public static string BuildArgs = "";
        public static string BuildWorkdir = "";
        public static int BuildTimeoutMs = 180000;
        public static string BuildLog = "";
        public static string ProjectType = "auto";

        public static string DefaultVerifyMode = "smoke";
        public static string DefaultReportDir = ".newera/reports";
        public static int MaxSnapshots = 50;

        public static ErrorInfo LastError = null;
        public static ProjectContext LastContext = null;
        public static string LastReportPath = null;

        static readonly object GateLock = new object();
        static readonly List<string> ChangedFiles = new List<string>();

        static string lastBuildStatus = "нет";
        static bool lastBuildOk = false;
        static DateTime? lastBuildTime = null;

        static readonly string[] BoolOptions =
        {
            "json", "no-run", "force", "update-baseline", "fix-safe",
            "auto", "ask", "list", "last", "preview", "help"
        };

        // ══════════════════════════════════════════════
        //  CONFIG
        // ══════════════════════════════════════════════

        public static void LoadBuildConfig()
        {
            AfterEditEnabled = true;
            AutoRepair = true;
            MaxRepairRounds = 3;

            BuildCmd = "auto";
            BuildArgs = "";
            BuildWorkdir = "";
            BuildTimeoutMs = 180000;
            BuildLog = Path.Combine(BaseDir, "build.log");
            ProjectType = "auto";

            DefaultVerifyMode = "smoke";
            DefaultReportDir = ".newera/reports";
            MaxSnapshots = 50;

            if (!File.Exists(ConfigFile)) return;

            try
            {
                string text = ReadTextAuto(ConfigFile);
                string[] lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

                foreach (string raw in lines)
                {
                    string t = raw.Trim();
                    if (t.StartsWith("#")) continue;

                    if (t.StartsWith("BUILD_AFTER_EDIT=")) AfterEditEnabled = ParseBool(t.Substring(17));
                    else if (t.StartsWith("AUTO_REPAIR=")) AutoRepair = ParseBool(t.Substring(12));
                    else if (t.StartsWith("MAX_REPAIR_ROUNDS=")) MaxRepairRounds = ParseInt(t.Substring(18), 3);
                    else if (t.StartsWith("BUILD_CMD=")) BuildCmd = t.Substring(10).Trim();
                    else if (t.StartsWith("BUILD_ARGS=")) BuildArgs = t.Substring(11).Trim();
                    else if (t.StartsWith("BUILD_WORKDIR=")) BuildWorkdir = t.Substring(14).Trim();
                    else if (t.StartsWith("BUILD_TIMEOUT="))
                    {
                        int seconds = ParseInt(t.Substring(14), 180);
                        if (seconds > 0) BuildTimeoutMs = seconds * 1000;
                    }
                    else if (t.StartsWith("BUILD_LOG="))
                    {
                        string v = t.Substring(10).Trim();
                        if (!string.IsNullOrEmpty(v)) BuildLog = v;
                    }
                    else if (t.StartsWith("PROJECT_TYPE=")) ProjectType = t.Substring(13).Trim();
                    else if (t.StartsWith("VERIFY_MODE="))
                    {
                        string v = t.Substring(12).Trim();
                        if (!string.IsNullOrEmpty(v)) DefaultVerifyMode = v;
                    }
                    else if (t.StartsWith("REPORT_DIR="))
                    {
                        string v = t.Substring(11).Trim();
                        if (!string.IsNullOrEmpty(v)) DefaultReportDir = v;
                    }
                    else if (t.StartsWith("MAX_DISK_ROLLBACKS=")) MaxSnapshots = ParseInt(t.Substring(19), 50);
                }

                if (string.IsNullOrEmpty(BuildCmd)) BuildCmd = "auto";
                if (string.IsNullOrEmpty(BuildLog)) BuildLog = Path.Combine(BaseDir, "build.log");
                if (BuildTimeoutMs <= 0) BuildTimeoutMs = 180000;
                if (MaxRepairRounds <= 0) MaxRepairRounds = 1;
                if (MaxSnapshots <= 0) MaxSnapshots = 1;
            }
            catch { }
        }

        // ══════════════════════════════════════════════
        //  COMMAND HANDLERS
        // ══════════════════════════════════════════════

        public static void HandleBuild()
        {
            HandleBuildCommand("build");
        }

        public static void HandleVerify()
        {
            HandleVerifyCommand("verify");
        }

        public static void HandleUndo()
        {
            HandleUndoCommand("undo");
        }

        public static bool PreHandleEditOrPlan(string cmd)
        {
            return true;
        }

        public static void HandleBuildCommand(string input)
        {
            ParsedArgs args = ParseArgs(input, "build");
            ProjectContext ctx = CreateContext(args.Path);
            LastContext = ctx;

            if (args.HasOption("help"))
            {
                WriteColored(ConsoleColor.Cyan, "  Использование: /build [path] [--target name] [--report file] [--json] [--no-run] [--force]\n");
                return;
            }

            WriteColored(ConsoleColor.Cyan, "\n  [i] Build Gate: build\n");
            WriteColored(ConsoleColor.DarkGray, "  ROOT: " + ctx.RootPath + "\n");
            WriteColored(ConsoleColor.DarkGray, "  TYPE: " + ctx.ProjectType + "\n");
            WriteColored(ConsoleColor.DarkGray, "  TOOLCHAIN: " + DetectToolchain(ctx) + "\n");

            BuildResult result = RunFullPipeline(ctx, args.HasOption("no-run"), args.HasOption("force"), args.GetOption("target", null));
            string reportPath = CreateBuildReport(ctx, result, args.GetOption("report", null));

            if (args.HasOption("json"))
            {
                Console.WriteLine(BuildJsonSummary(ctx, result, reportPath));
            }
            else
            {
                ShowResult(result);
                if (!string.IsNullOrEmpty(reportPath))
                    WriteColored(ConsoleColor.DarkGray, "  REPORT: " + reportPath + "\n");
            }
        }

        public static void HandleVerifyCommand(string input)
        {
            ParsedArgs args = ParseArgs(input, "verify");
            ProjectContext ctx = CreateContext(args.Path);
            LastContext = ctx;

            if (args.HasOption("help"))
            {
                WriteColored(ConsoleColor.Cyan, "  Использование: /verify [path] [--baseline file] [--update-baseline] [--script file] [--timeout ms]\n");
                return;
            }

            WriteColored(ConsoleColor.Cyan, "\n  [i] Build Gate: verify\n");
            WriteColored(ConsoleColor.DarkGray, "  ROOT: " + ctx.RootPath + "\n");
            WriteColored(ConsoleColor.DarkGray, "  MODE: " + (string.IsNullOrEmpty(ctx.VerifyMode) ? DefaultVerifyMode : ctx.VerifyMode) + "\n");

            VerifyResult vr = RunVerify(ctx, args);
            string reportPath = CreateVerifyReport(ctx, vr, args.GetOption("report", null));

            WriteColored(vr.Success ? ConsoleColor.Green : ConsoleColor.Red, "  [" + vr.Status + "] " + (vr.Details ?? "") + "\n");
            if (!string.IsNullOrEmpty(reportPath))
                WriteColored(ConsoleColor.DarkGray, "  REPORT: " + reportPath + "\n");
        }

        public static void HandleUndoCommand(string input)
        {
            ParsedArgs args = ParseArgs(input, "undo");
            ProjectContext ctx = CreateContext(args.Path);
            LastContext = ctx;

            if (args.HasOption("help"))
            {
                WriteColored(ConsoleColor.Cyan, "  Использование: /undo [path] [--list] [--last] [--to rollbackId] [--steps N] [--preview]\n");
                return;
            }

            string snapRoot = GetSnapshotRoot(ctx);
            List<string> snapshots = GetSnapshotDirs(snapRoot);

            if (args.HasOption("list") || snapshots.Count == 0)
            {
                WriteColored(ConsoleColor.Cyan, "\n  [i] Rollback snapshots:\n");

                if (snapshots.Count == 0)
                {
                    WriteColored(ConsoleColor.Yellow, "  [!!] Снапшоты не найдены в " + snapRoot + "\n");
                    return;
                }

                for (int i = 0; i < snapshots.Count; i++)
                    WriteColored(ConsoleColor.White, "  [" + (i + 1) + "] " + Path.GetFileName(snapshots[i]) + "\n");

                return;
            }

            int index = 0;

            if (args.HasOption("steps"))
            {
                int steps = ParseInt(args.GetOption("steps", "1"), 1);
                index = Math.Max(0, steps - 1);
            }
            else if (args.HasOption("to"))
            {
                string id = args.GetOption("to", "");
                int found = -1;

                for (int i = 0; i < snapshots.Count; i++)
                {
                    if (Path.GetFileName(snapshots[i]).IndexOf(id, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        found = i;
                        break;
                    }
                }

                if (found < 0)
                {
                    WriteColored(ConsoleColor.Red, "  [XX] Снапшот не найден: " + id + "\n");
                    return;
                }

                index = found;
            }

            if (index >= snapshots.Count)
            {
                WriteColored(ConsoleColor.Red, "  [XX] Недостаточно снапшотов для отката.\n");
                return;
            }

            string targetSnap = snapshots[index];

            if (args.HasOption("preview"))
            {
                WriteColored(ConsoleColor.Cyan, "\n  [i] Preview: " + Path.GetFileName(targetSnap) + "\n");
                string manifest = Path.Combine(targetSnap, "manifest.txt");

                if (!File.Exists(manifest))
                {
                    WriteColored(ConsoleColor.Red, "  [XX] manifest.txt не найден.\n");
                    return;
                }

                string[] lines = ReadTextAuto(manifest).Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                int shown = 0;

                foreach (string raw in lines)
                {
                    string line = raw.Trim();
                    if (line.Length == 0) continue;
                    if (line.StartsWith("BASE=") || line.StartsWith("KIND=") || line.StartsWith("TIME=")) continue;

                    WriteColored(ConsoleColor.Gray, "  " + line + "\n");
                    shown++;

                    if (shown >= 200)
                    {
                        WriteColored(ConsoleColor.Yellow, "  ... и ещё файлы\n");
                        break;
                    }
                }

                return;
            }

            CreateRollbackPoint(ctx.RootPath, "emergency");

            if (RestoreSnapshot(ctx, targetSnap))
                WriteColored(ConsoleColor.Green, "  [OK] Откат выполнен: " + Path.GetFileName(targetSnap) + "\n");
            else
                WriteColored(ConsoleColor.Red, "  [XX] Откат не выполнен.\n");
        }

        public static void DrawHelp()
        {
            lock (PrintLock)
            {
                Console.ForegroundColor = ConsoleColor.DarkCyan;
                Console.WriteLine("  == BUILD GATE КОМАНДЫ ==");
                Console.ResetColor();

                WriteBuildStatusLine("/build [path]", "сборка + smoke-run", true);
                WriteBuildStatusLine("/verify [path]", "проверка поведения", true);
                WriteBuildStatusLine("/doctor [path]", "диагностика причины", true);
                WriteBuildStatusLine("/repair [path]", "авто-ремонт", true);
                WriteBuildStatusLine("/undo [path]", "откат снапшотов", true);

                Console.WriteLine();
            }
        }

        public static void DrawBuildStatus()
        {
            lock (PrintLock)
            {
                Console.ForegroundColor = ConsoleColor.DarkCyan;
                Console.WriteLine("  == BUILD GATE ==");
                Console.ResetColor();

                WriteBuildStatusLine("Build cmd", BuildCmd, true);
                WriteBuildStatusLine("Project type", ProjectType, true);
                WriteBuildStatusLine("Auto repair", AutoRepair ? "ON" : "OFF", AutoRepair);
                WriteBuildStatusLine("Max rounds", MaxRepairRounds.ToString(), true);
                WriteBuildStatusLine("After edit", AfterEditEnabled ? "ON" : "OFF", AfterEditEnabled);
                WriteBuildStatusLine("Last build", lastBuildStatus, lastBuildOk);
                WriteBuildStatusLine("Build time", lastBuildTime.HasValue ? lastBuildTime.Value.ToString("dd.MM HH:mm:ss") : "нет", lastBuildOk);
                WriteBuildStatusLine("Last error", LastError != null ? LastError.Type : "нет", LastError == null);
                WriteBuildStatusLine("Last report", string.IsNullOrEmpty(LastReportPath) ? "нет" : LastReportPath, !string.IsNullOrEmpty(LastReportPath));

                Console.WriteLine();
            }
        }

        static void WriteBuildStatusLine(string label, string value, bool ok)
        {
            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.Write("  " + label.PadRight(18));
            Console.ForegroundColor = ok ? ConsoleColor.White : ConsoleColor.Red;
            Console.WriteLine(value ?? "");
            Console.ResetColor();
        }

        // ══════════════════════════════════════════════
        //  POST EDIT PIPELINE
        // ══════════════════════════════════════════════

        public static bool ShouldRunAfterEdit(string projectPath)
        {
            return AfterEditEnabled && AutoRepair && !SuppressPostApply;
        }

        public static void RunPostApplyPipeline(string projectPath)
        {
            if (SuppressPostApply) return;

            try
            {
                ProjectContext ctx = CreateContext(projectPath);
                LastContext = ctx;

                if (!ctx.Recognized)
                {
                    WriteColored(ConsoleColor.DarkGray, "  [i] Build Gate: проект не распознан — автопроверка пропущена\n");
                    return;
                }

                WriteColored(ConsoleColor.Cyan, "  [i] Build Gate: автопроверка после изменений\n");

                BuildResult result = RunFullPipeline(ctx, false, false, null);

                if (!result.Success && ctx.RepairEnabled && AutoRepair && ctx.RepairAutoApply)
                {
                    WriteColored(ConsoleColor.Yellow, "  [!!] Сборка упала — запускаю авто-ремонт\n");

                    SuppressPostApply = true;
                    try
                    {
                        RepairLoop.RunWithContext(ctx, ctx.RepairMaxRounds, true, false);
                    }
                    finally
                    {
                        SuppressPostApply = false;
                    }
                }
            }
            catch (Exception ex)
            {
                WriteColored(ConsoleColor.Yellow, "  [!!] Build Gate post-apply: " + ex.Message + "\n");
            }
        }

        // ══════════════════════════════════════════════
        //  PROJECT CONTEXT / MANIFEST / TOOLCHAIN
        // ══════════════════════════════════════════════

        public static ProjectContext CreateContext(string pathArg)
        {
            ProjectContext ctx = new ProjectContext();

            string start = pathArg;
            if (string.IsNullOrEmpty(start)) start = ProjectPath;
            if (string.IsNullOrEmpty(start)) start = Environment.CurrentDirectory;
            if (string.IsNullOrEmpty(start)) start = BaseDir;

            try
            {
                ctx.RootPath = ResolveProjectRootFromPath(start);
            }
            catch
            {
                ctx.RootPath = BaseDir;
            }

            ctx.ManifestPath = Path.Combine(ctx.RootPath, "build.json");

            LoadManifest(ctx);
            ApplyDefaults(ctx);
            DetectProjectType(ctx);

            return ctx;
        }

        public static string ResolveProjectRoot(string preferred)
        {
            try
            {
                string start = preferred;
                if (string.IsNullOrEmpty(start)) start = ProjectPath;
                if (string.IsNullOrEmpty(start)) start = Environment.CurrentDirectory;
                if (string.IsNullOrEmpty(start)) start = BaseDir;

                return ResolveProjectRootFromPath(start);
            }
            catch
            {
                return BaseDir;
            }
        }

        public static string ResolveProjectRootFromPath(string path)
        {
            try
            {
                string dir;

                if (File.Exists(path)) dir = Path.GetDirectoryName(path);
                else dir = path;

                if (string.IsNullOrEmpty(dir)) dir = Environment.CurrentDirectory;

                dir = Path.GetFullPath(dir);

                if (!Directory.Exists(dir))
                {
                    string parent = Path.GetDirectoryName(dir);
                    if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent)) dir = parent;
                }

                DirectoryInfo d = new DirectoryInfo(dir);

                for (int i = 0; i < 10 && d != null; i++)
                {
                    if (LooksLikeProjectRoot(d.FullName)) return d.FullName;
                    d = d.Parent;
                }

                return dir;
            }
            catch
            {
                return BaseDir;
            }
        }

        static bool LooksLikeProjectRoot(string dir)
        {
            try
            {
                if (File.Exists(Path.Combine(dir, "build.json"))) return true;
                if (Directory.Exists(Path.Combine(dir, ".git"))) return true;
                if (Directory.GetFiles(dir, "*.csproj").Length > 0) return true;
                if (Directory.GetFiles(dir, "*.sln").Length > 0) return true;
                if (File.Exists(Path.Combine(dir, "package.json"))) return true;
                if (File.Exists(Path.Combine(dir, "requirements.txt"))) return true;
                if (File.Exists(Path.Combine(dir, "run.bat"))) return true;
                if (Directory.Exists(Path.Combine(dir, "cli", "main"))) return true;
            }
            catch { }

            return false;
        }

        static void LoadManifest(ProjectContext ctx)
        {
            ctx.ManifestExists = File.Exists(ctx.ManifestPath);
            if (!ctx.ManifestExists) return;

            try
            {
                string text = ReadTextAuto(ctx.ManifestPath);
                if (string.IsNullOrWhiteSpace(text))
                {
                    ctx.ManifestError = "build.json пуст";
                    return;
                }

                var ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                Dictionary<string, object> root = ser.DeserializeObject(text) as Dictionary<string, object>;

                if (root == null)
                {
                    ctx.ManifestError = "build.json не является JSON-объектом";
                    return;
                }

                Dictionary<string, object> project = JDict(root, "project");
                ctx.ProjectType = JStr(project, "type", ctx.ProjectType);
                ctx.ProjectFile = JStr(project, "file", ctx.ProjectFile);

                Dictionary<string, object> build = JDict(root, "build");
                ctx.BuildCmd = JStr(build, "cmd", ctx.BuildCmd);
                ctx.BuildArgs = JStr(build, "args", ctx.BuildArgs);
                ctx.BuildWorkdir = JStr(build, "workdir", ctx.BuildWorkdir);
                ctx.BuildTimeoutMs = JInt(build, "timeoutMs", ctx.BuildTimeoutMs);
                ctx.CompilerPreference = JStr(build, "compilerPreference", ctx.CompilerPreference);
                ctx.ProjectFile = JStr(build, "projectFile", ctx.ProjectFile);

                Dictionary<string, object> run = JDict(root, "run");
                ctx.RunCmd = JStr(run, "cmd", ctx.RunCmd);
                ctx.RunArgs = JStr(run, "args", ctx.RunArgs);
                ctx.RunWorkdir = JStr(run, "workdir", ctx.RunWorkdir);
                ctx.RunTimeoutMs = JInt(run, "timeoutMs", ctx.RunTimeoutMs);
                ctx.HealthCheck = JStr(run, "healthCheck", ctx.HealthCheck);

                string[] codes = JArr(run, "expectedExitCodes");
                if (codes != null && codes.Length > 0)
                {
                    var ints = new List<int>();

                    foreach (string s in codes)
                    {
                        int v;
                        if (int.TryParse(s, out v)) ints.Add(v);
                    }

                    if (ints.Count > 0) ctx.ExpectedExitCodes = ints.ToArray();
                }

                string[] stdoutContains = JArr(run, "expectStdoutContains");
                if (stdoutContains != null)
                {
                    ctx.ExpectStdoutContains.Clear();
                    foreach (string s in stdoutContains)
                        if (!string.IsNullOrEmpty(s)) ctx.ExpectStdoutContains.Add(s);
                }

                string[] expectFiles = JArr(run, "expectFiles");
                if (expectFiles != null)
                {
                    ctx.ExpectFiles.Clear();
                    foreach (string s in expectFiles)
                        if (!string.IsNullOrEmpty(s)) ctx.ExpectFiles.Add(s);
                }

                Dictionary<string, object> verify = JDict(root, "verify");
                ctx.VerifyMode = JStr(verify, "mode", ctx.VerifyMode);
                ctx.BaselineFile = JStr(verify, "baselineFile", ctx.BaselineFile);
                ctx.VerifyScript = JStr(verify, "script", ctx.VerifyScript);

                Dictionary<string, object> repair = JDict(root, "repair");
                ctx.RepairEnabled = JBool(repair, "enabled", ctx.RepairEnabled);
                ctx.RepairMaxRounds = JInt(repair, "maxRounds", ctx.RepairMaxRounds);
                ctx.RepairAutoApply = JBool(repair, "autoApply", ctx.RepairAutoApply);
                ctx.RepairRollback = JBool(repair, "rollback", ctx.RepairRollback);

                string[] allowed = JArr(repair, "allowedPaths");
                if (allowed != null)
                {
                    ctx.AllowedPaths.Clear();
                    foreach (string s in allowed)
                        if (!string.IsNullOrEmpty(s)) ctx.AllowedPaths.Add(s.Replace('\\', '/'));
                }

                string[] forbidden = JArr(repair, "forbiddenPaths");
                if (forbidden != null)
                {
                    ctx.ForbiddenPaths.Clear();
                    foreach (string s in forbidden)
                        if (!string.IsNullOrEmpty(s)) ctx.ForbiddenPaths.Add(s.Replace('\\', '/'));
                }

                Dictionary<string, object> report = JDict(root, "report");
                ctx.ReportDir = JStr(report, "dir", ctx.ReportDir);
                ctx.IncludeLogTail = JBool(report, "includeLogTail", ctx.IncludeLogTail);

                Dictionary<string, object> undo = JDict(root, "undo");
                ctx.UndoMaxSnapshots = JInt(undo, "maxSnapshots", ctx.UndoMaxSnapshots);
                ctx.SnapshotDir = JStr(undo, "snapshotDir", ctx.SnapshotDir);

                // Root-level overrides
                ctx.ProjectType = JStr(root, "projectType", ctx.ProjectType);
                ctx.ProjectFile = JStr(root, "projectFile", ctx.ProjectFile);

                ctx.BuildCmd = JStr(root, "buildCmd", ctx.BuildCmd);
                ctx.BuildArgs = JStr(root, "buildArgs", ctx.BuildArgs);
                ctx.BuildWorkdir = JStr(root, "buildWorkdir", ctx.BuildWorkdir);
                ctx.CompilerPreference = JStr(root, "compilerPreference", ctx.CompilerPreference);

                ctx.RepairEnabled = JBool(root, "repairEnabled", ctx.RepairEnabled);
                ctx.RepairMaxRounds = JInt(root, "repairMaxRounds", ctx.RepairMaxRounds);

                string[] rootAllowed = JArr(root, "allowedPaths");
                if (rootAllowed != null)
                {
                    ctx.AllowedPaths.Clear();
                    foreach (string s in rootAllowed)
                        if (!string.IsNullOrEmpty(s)) ctx.AllowedPaths.Add(s.Replace('\\', '/'));
                }

                string[] rootForbidden = JArr(root, "forbiddenPaths");
                if (rootForbidden != null)
                {
                    ctx.ForbiddenPaths.Clear();
                    foreach (string s in rootForbidden)
                        if (!string.IsNullOrEmpty(s)) ctx.ForbiddenPaths.Add(s.Replace('\\', '/'));
                }
            }
            catch (Exception ex)
            {
                ctx.ManifestError = ex.Message;
            }
        }

        static void ApplyDefaults(ProjectContext ctx)
        {
            if (string.IsNullOrEmpty(ctx.ProjectType) || ctx.ProjectType == "auto")
                ctx.ProjectType = ProjectType;

            if (string.IsNullOrEmpty(ctx.BuildCmd) || ctx.BuildCmd == "auto")
                ctx.BuildCmd = BuildCmd;

            if (string.IsNullOrEmpty(ctx.BuildArgs)) ctx.BuildArgs = BuildArgs;
            if (string.IsNullOrEmpty(ctx.BuildWorkdir)) ctx.BuildWorkdir = BuildWorkdir;

            if (ctx.BuildTimeoutMs <= 0) ctx.BuildTimeoutMs = BuildTimeoutMs;
            if (ctx.RunTimeoutMs <= 0) ctx.RunTimeoutMs = 30000;

            if (ctx.RepairMaxRounds <= 0) ctx.RepairMaxRounds = MaxRepairRounds;
            ctx.RepairAutoApply = ctx.RepairAutoApply && AutoRepair;

            if (string.IsNullOrEmpty(ctx.VerifyMode)) ctx.VerifyMode = DefaultVerifyMode;
            if (string.IsNullOrEmpty(ctx.ReportDir)) ctx.ReportDir = DefaultReportDir;

            if (ctx.UndoMaxSnapshots <= 0) ctx.UndoMaxSnapshots = MaxSnapshots;
            if (string.IsNullOrEmpty(ctx.SnapshotDir)) ctx.SnapshotDir = ".newera/rollback";
        }

        static void DetectProjectType(ProjectContext ctx)
        {
            if (ctx.ProjectType == "auto")
            {
                bool hasNetProject = HasFiles(ctx.RootPath, "*.sln") || HasFiles(ctx.RootPath, "*.csproj");

                if (hasNetProject)
                {
                    ctx.ProjectType = "csharp-dotnet";
                }
                else if (File.Exists(Path.Combine(ctx.RootPath, "package.json")))
                {
                    ctx.ProjectType = "node";
                }
                else if (File.Exists(Path.Combine(ctx.RootPath, "requirements.txt")))
                {
                    ctx.ProjectType = "python";
                }
                else if (Directory.Exists(Path.Combine(ctx.RootPath, "cli", "main")) && !string.IsNullOrEmpty(FindCsc()))
                {
                    ctx.ProjectType = "csharp-framework";
                }
                else if (File.Exists(Path.Combine(ctx.RootPath, "run.bat")))
                {
                    ctx.ProjectType = "bat";
                }
                else
                {
                    ctx.ProjectType = "custom";
                }
            }

            ctx.Recognized = ctx.ManifestExists || ctx.ProjectType != "custom";

            if (!string.IsNullOrEmpty(ctx.ManifestError))
                ctx.Recognized = false;
        }

        public static string DetectToolchain(ProjectContext ctx)
        {
            if (ctx.ProjectType == "csharp-framework")
            {
                string csc = FindCsc();
                return string.IsNullOrEmpty(csc) ? "csc: НЕ НАЙДЕН" : ("csc: " + csc);
            }

            if (ctx.ProjectType == "csharp-dotnet")
            {
                string dotnet = FindDotNet();
                return string.IsNullOrEmpty(dotnet) ? "dotnet: НЕ НАЙДЕН" : ("dotnet: " + dotnet);
            }

            if (ctx.ProjectType == "node") return "node/npm";
            if (ctx.ProjectType == "python") return "python";
            if (ctx.ProjectType == "bat") return "bat/custom";

            return "custom";
        }

        static bool HasFiles(string dir, string pattern)
        {
            try
            {
                return Directory.GetFiles(dir, pattern).Length > 0;
            }
            catch
            {
                return false;
            }
        }

        public static string FindCsc()
        {
            string windir = Environment.GetEnvironmentVariable("WINDIR");
            if (string.IsNullOrEmpty(windir)) windir = @"C:\Windows";

            string p1 = Path.Combine(windir, @"Microsoft.NET\Framework64\v4.0.30319\csc.exe");
            if (File.Exists(p1)) return p1;

            string p2 = Path.Combine(windir, @"Microsoft.NET\Framework\v4.0.30319\csc.exe");
            if (File.Exists(p2)) return p2;

            return null;
        }

        public static string FindDotNet()
        {
            try
            {
                string pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
                string[] dirs = pathVar.Split(';');

                foreach (string dir in dirs)
                {
                    string d = dir.Trim();
                    if (d.Length == 0) continue;

                    try
                    {
                        string p = Path.Combine(d, "dotnet.exe");
                        if (File.Exists(p)) return p;
                    }
                    catch { }
                }

                string prog = Environment.GetEnvironmentVariable("ProgramFiles");
                if (!string.IsNullOrEmpty(prog))
                {
                    string p = Path.Combine(prog, @"dotnet\dotnet.exe");
                    if (File.Exists(p)) return p;
                }

                string prog86 = Environment.GetEnvironmentVariable("ProgramFiles(x86)");
                if (!string.IsNullOrEmpty(prog86))
                {
                    string p = Path.Combine(prog86, @"dotnet\dotnet.exe");
                    if (File.Exists(p)) return p;
                }
            }
            catch { }

            return null;
        }

        static bool TryFindSingleProjectFile(ProjectContext ctx, out string pathOrError)
        {
            pathOrError = null;

            try
            {
                if (!string.IsNullOrEmpty(ctx.ProjectFile))
                {
                    string full = ctx.ProjectFile;
                    if (!Path.IsPathRooted(full)) full = Path.Combine(ctx.RootPath, full);

                    if (File.Exists(full))
                    {
                        pathOrError = full;
                        return true;
                    }

                    pathOrError = "projectFile не найден: " + full;
                    return false;
                }

                string[] slns = null;
                try { slns = Directory.GetFiles(ctx.RootPath, "*.sln"); } catch { }

                if (slns != null && slns.Length == 1)
                {
                    pathOrError = slns[0];
                    return true;
                }

                if (slns != null && slns.Length > 1)
                {
                    pathOrError = "Найдено несколько .sln. Укажи build.json: build.projectFile или путь к проекту.";
                    return false;
                }

                string[] csproj = null;
                try { csproj = Directory.GetFiles(ctx.RootPath, "*.csproj"); } catch { }

                if (csproj != null && csproj.Length == 1)
                {
                    pathOrError = csproj[0];
                    return true;
                }

                if (csproj != null && csproj.Length > 1)
                {
                    pathOrError = "Найдено несколько .csproj. Укажи build.json: build.projectFile или путь к проекту.";
                    return false;
                }

                pathOrError = "Не найден .sln/.csproj в корне проекта.";
                return false;
            }
            catch (Exception ex)
            {
                pathOrError = "Ошибка поиска project file: " + ex.Message;
                return false;
            }
        }

        // ══════════════════════════════════════════════
        //  BUILD PIPELINE
        // ══════════════════════════════════════════════

        public static BuildResult RunFullPipeline(ProjectContext ctx, bool noRun, bool force, string target)
        {
            BuildResult build = RunBuild(ctx, target, force);
            LastError = build.Error;
            UpdateLastBuild(build);

            if (!build.Success) return build;

            if (noRun) return build;

            BuildResult run = RunSmoke(ctx);
            LastError = run.Error;
            UpdateLastBuild(run);

            if (!run.Success) return run;

            run = ValidateRunResult(ctx, run);
            LastError = run.Error;
            UpdateLastBuild(run);

            return run;
        }

        public static BuildResult RunBuild(string projectRoot)
        {
            ProjectContext ctx = CreateContext(projectRoot);
            return RunBuild(ctx, null, false);
        }

        public static BuildResult RunBuild(ProjectContext ctx, string target, bool force)
        {
            try
            {
                if (!string.IsNullOrEmpty(ctx.ManifestError))
                    return MakeFail("Битый manifest build.json: " + ctx.ManifestError, false, "manifest");

                if (ctx.ProjectType == "csharp-framework")
                    return BuildCSharpFramework(ctx, target);

                if (ctx.ProjectType == "csharp-dotnet")
                    return BuildCSharpDotnet(ctx, target);

                if (ctx.ProjectType == "node")
                    return BuildNode(ctx);

                if (ctx.ProjectType == "python")
                    return BuildPython(ctx);

                if (ctx.ProjectType == "bat" || ctx.ProjectType == "custom")
                    return BuildCustom(ctx, target);

                return MakeFail("Неизвестный тип проекта: " + ctx.ProjectType, false, "config");
            }
            catch (Exception ex)
            {
                return MakeFail("Ошибка сборки: " + ex.Message, false, "unknown");
            }
        }

        static BuildResult BuildCSharpDotnet(ProjectContext ctx, string target)
        {
            string dotnet = FindDotNet();

            if (string.IsNullOrEmpty(dotnet))
            {
                // Fallback: если есть csc и исходники, попробуем собрать как csharp-framework
                if (!string.IsNullOrEmpty(FindCsc()) && CollectCsFiles(ctx.RootPath, 1).Count > 0)
                    return BuildCSharpFramework(ctx, target);

                return MakeFail(
                    "dotnet SDK не найден. Установи .NET SDK или добавь dotnet в PATH.",
                    false,
                    "toolchain");
            }

            string projectFile;
            if (!TryFindSingleProjectFile(ctx, out projectFile))
                return MakeFail(projectFile, false, "project_missing");

            string args = "build \"" + projectFile + "\" -nologo -v minimal -clp:ErrorsOnly";
            if (!string.IsNullOrEmpty(target)) args += " /p:Target=" + target;

            return RunExternal(ctx, dotnet, args, ctx.RootPath, ctx.BuildTimeoutMs, "build");
        }

        static BuildResult BuildCSharpFramework(ProjectContext ctx, string target)
        {
            string csc = FindCsc();
            if (string.IsNullOrEmpty(csc))
                return MakeFail("Компилятор csc.exe не найден. Установи .NET Framework 4.x или задай build.cmd.", false, "toolchain");

            string outDir = Path.Combine(ctx.RootPath, ".newera", "build");
            Directory.CreateDirectory(outDir);

            string cliMain = Path.Combine(ctx.RootPath, "cli", "main");

            if (Directory.Exists(cliMain))
            {
                string[] mainSources = Directory.GetFiles(cliMain, "*.cs");
                if (mainSources == null || mainSources.Length == 0)
                    return MakeFail("В cli\\main нет .cs файлов.", false, "project_missing");

                string mainExe = Path.Combine(outDir, "main.exe");
                BuildResult main = CompileCSharp(ctx, csc, mainExe, new List<string>(mainSources));
                if (!main.Success) return main;

                string helperSource = Path.Combine(ctx.RootPath, "cli", "helper", "helper.cs");
                if (File.Exists(helperSource))
                {
                    string helperExe = Path.Combine(outDir, "helper.exe");
                    BuildResult helper = CompileCSharp(ctx, csc, helperExe, new List<string> { helperSource });
                    if (!helper.Success) return helper;
                }

                return MakeSuccess("C# Framework build OK: " + mainExe);
            }

            List<string> sources = CollectCsFiles(ctx.RootPath, 500);
            if (sources.Count == 0)
                return MakeFail("Не найдены .cs файлы для сборки.", false, "project_missing");

            string appExe = Path.Combine(outDir, "app.exe");
            BuildResult result = CompileCSharp(ctx, csc, appExe, sources);
            if (!result.Success) return result;

            return MakeSuccess("C# Framework build OK: " + appExe);
        }

        static BuildResult CompileCSharp(ProjectContext ctx, string csc, string outFile, List<string> sources)
        {
            var args = new StringBuilder();
            args.Append("/nologo /optimize+ /platform:anycpu /target:exe /r:System.Web.Extensions.dll /out:\"" + outFile + "\"");

            foreach (string src in sources)
                args.Append(" \"" + src + "\"");

            return RunExternal(ctx, csc, args.ToString(), ctx.RootPath, ctx.BuildTimeoutMs, "csc");
        }

        static BuildResult BuildNode(ProjectContext ctx)
        {
            string pkg = Path.Combine(ctx.RootPath, "package.json");
            if (!File.Exists(pkg))
                return MakeFail("package.json не найден.", false, "project_missing");

            string text = ReadTextAuto(pkg);

            if (text.IndexOf("\"build\"", StringComparison.OrdinalIgnoreCase) >= 0)
                return RunExternal(ctx, "cmd.exe", "/c npm run build", ctx.BuildWorkdir, ctx.BuildTimeoutMs, "build");

            return MakeSuccess("Build skipped: в package.json нет скрипта build.");
        }

        static BuildResult BuildPython(ProjectContext ctx)
        {
            return RunExternal(ctx, "python", "-m compileall -q .", ctx.BuildWorkdir, ctx.BuildTimeoutMs, "build");
        }

        static BuildResult BuildCustom(ProjectContext ctx, string target)
        {
            if (!string.IsNullOrEmpty(ctx.BuildCmd) && ctx.BuildCmd != "auto")
            {
                string args = ctx.BuildArgs ?? "";
                if (!string.IsNullOrEmpty(target)) args += " " + target;
                return RunExternal(ctx, ctx.BuildCmd, args, ctx.BuildWorkdir, ctx.BuildTimeoutMs, "build");
            }

            string buildBat = Path.Combine(ctx.RootPath, "build.bat");
            if (File.Exists(buildBat))
                return RunExternal(ctx, buildBat, ctx.BuildArgs ?? "", ctx.BuildWorkdir, ctx.BuildTimeoutMs, "build");

            string runBat = Path.Combine(ctx.RootPath, "run.bat");
            if (File.Exists(runBat))
                return MakeFail("Найден run.bat, но он может быть интерактивным. Задай build.cmd в build.json или BUILD_CMD.", false, "config");

            return MakeSuccess("Build skipped: build command не задан.");
        }

        public static BuildResult RunSmoke(ProjectContext ctx)
        {
            if (!string.IsNullOrEmpty(ctx.RunCmd))
                return RunExternal(ctx, ctx.RunCmd, ctx.RunArgs ?? "", ctx.RunWorkdir, ctx.RunTimeoutMs, "run");

            if (ctx.ProjectType == "csharp-framework")
            {
                string main = Path.Combine(ctx.RootPath, ".newera", "build", "main.exe");
                string app = Path.Combine(ctx.RootPath, ".newera", "build", "app.exe");

                if (File.Exists(main) || File.Exists(app))
                    return MakeSuccess("Smoke skipped: binary существует, run.cmd не задан.");
            }

            return MakeSuccess("Smoke skipped: run.cmd не задан.");
        }

        static BuildResult ValidateRunResult(ProjectContext ctx, BuildResult result)
        {
            if (result == null)
                return MakeFail("Пустой результат run.", false, "unknown");

            if (ctx.ExpectedExitCodes != null && ctx.ExpectedExitCodes.Length > 0)
            {
                bool okExit = false;

                foreach (int code in ctx.ExpectedExitCodes)
                {
                    if (code == result.ExitCode)
                    {
                        okExit = true;
                        break;
                    }
                }

                if (!okExit)
                    return MakeFail("Run завершился с exit code " + result.ExitCode + ", ожидалось: " + string.Join(",", ctx.ExpectedExitCodes), true, "runtime");
            }

            string output = result.Output ?? "";

            foreach (string expected in ctx.ExpectStdoutContains)
            {
                if (output.IndexOf(expected, StringComparison.OrdinalIgnoreCase) < 0)
                    return MakeFail("В stdout не найдено: " + expected, true, "runtime");
            }

            foreach (string file in ctx.ExpectFiles)
            {
                string full = file;
                if (!Path.IsPathRooted(full)) full = Path.Combine(ctx.RootPath, file);

                if (!File.Exists(full))
                    return MakeFail("Ожидался файл: " + full, true, "runtime");
            }

            if (!string.IsNullOrEmpty(ctx.HealthCheck))
            {
                string healthError = CheckHealth(ctx.HealthCheck);
                if (!string.IsNullOrEmpty(healthError))
                    return MakeFail("Health check failed: " + healthError, false, "network");
            }

            return result;
        }

        static string CheckHealth(string url)
        {
            try
            {
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "GET";
                req.Timeout = 8000;
                req.ReadWriteTimeout = 8000;

                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                {
                    int code = (int)resp.StatusCode;
                    if (code >= 200 && code < 400) return null;
                    return "HTTP " + code;
                }
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        // ══════════════════════════════════════════════
        //  VERIFY
        // ══════════════════════════════════════════════

        public static VerifyResult RunVerify(ProjectContext ctx, ParsedArgs args)
        {
            string mode = ctx.VerifyMode;

            if (args.HasOption("script") || !string.IsNullOrEmpty(ctx.VerifyScript)) mode = "script";
            if (args.HasOption("baseline") || args.HasOption("update-baseline") || !string.IsNullOrEmpty(ctx.BaselineFile)) mode = "snapshot";

            if (mode == "script")
                return VerifyScript(ctx, args);

            if (mode == "snapshot")
                return VerifySnapshot(ctx, args);

            return VerifySmoke(ctx);
        }

        static VerifyResult VerifySmoke(ProjectContext ctx)
        {
            BuildResult build = RunBuild(ctx, null, false);
            if (!build.Success)
                return new VerifyResult { Success = false, Status = "FAIL", Details = "Build failed", LastBuildResult = build };

            BuildResult run = RunSmoke(ctx);
            if (!run.Success)
                return new VerifyResult { Success = false, Status = "FAIL", Details = "Run failed", LastBuildResult = run };

            run = ValidateRunResult(ctx, run);
            if (!run.Success)
                return new VerifyResult { Success = false, Status = "FAIL", Details = "Run validation failed", LastBuildResult = run };

            return new VerifyResult { Success = true, Status = "PASS", Details = "Smoke verify OK", LastBuildResult = run };
        }

        static VerifyResult VerifyScript(ProjectContext ctx, ParsedArgs args)
        {
            string script = args.GetOption("script", ctx.VerifyScript);

            if (string.IsNullOrEmpty(script))
                return new VerifyResult { Success = false, Status = "FAIL", Details = "Не указан --script или verify.script в build.json" };

            string full = script;
            if (!Path.IsPathRooted(full)) full = Path.Combine(ctx.RootPath, script);

            if (!File.Exists(full))
                return new VerifyResult { Success = false, Status = "FAIL", Details = "Скрипт не найден: " + full };

            BuildResult result = RunExternal(ctx, "cmd.exe", "/c \"\"" + full + "\"", ctx.RunWorkdir, ctx.RunTimeoutMs, "verify");

            if (!result.Success)
                return new VerifyResult { Success = false, Status = "FAIL", Details = "Скрипт завершился с ошибкой", LastBuildResult = result };

            return new VerifyResult { Success = true, Status = "PASS", Details = "Script verify OK", LastBuildResult = result };
        }

        static VerifyResult VerifySnapshot(ProjectContext ctx, ParsedArgs args)
        {
            string baseline = args.GetOption("baseline", ctx.BaselineFile);

            if (string.IsNullOrEmpty(baseline))
                baseline = Path.Combine(ctx.RootPath, ".newera", "baseline.txt");

            if (!Path.IsPathRooted(baseline))
                baseline = Path.Combine(ctx.RootPath, baseline);

            BuildResult build = RunBuild(ctx, null, false);
            if (!build.Success)
                return new VerifyResult { Success = false, Status = "FAIL", Details = "Build failed", LastBuildResult = build };

            BuildResult run = RunSmoke(ctx);
            run = ValidateRunResult(ctx, run);

            if (!run.Success)
                return new VerifyResult { Success = false, Status = "FAIL", Details = "Run failed", LastBuildResult = run };

            string actual = NormalizeSnapshotText(run.Output ?? "");

            if (args.HasOption("update-baseline") || !File.Exists(baseline))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(baseline));
                File.WriteAllText(baseline, actual, new UTF8Encoding(false));

                return new VerifyResult { Success = true, Status = "PASS", Details = "Baseline обновлён: " + baseline, LastBuildResult = run };
            }

            string expected = NormalizeSnapshotText(ReadTextAuto(baseline));

            if (expected == actual)
                return new VerifyResult { Success = true, Status = "PASS", Details = "Snapshot совпадает", LastBuildResult = run };

            return new VerifyResult { Success = false, Status = "DIFF", Details = "Snapshot отличается от baseline", LastBuildResult = run };
        }

        static string NormalizeSnapshotText(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            text = text.Replace("\r\n", "\n").Replace("\r", "\n").Trim();
            return text;
        }

        // ══════════════════════════════════════════════
        //  PROCESS RUNNER
        // ══════════════════════════════════════════════

        static BuildResult RunExternal(ProjectContext ctx, string fileName, string args, string workdir, int timeoutMs, string source)
        {
            if (string.IsNullOrEmpty(fileName))
                return MakeSuccess("Команда не задана");

            string cmd = fileName;

            try
            {
                if (!Path.IsPathRooted(cmd))
                {
                    string candidate = Path.Combine(ctx.RootPath, cmd);
                    if (File.Exists(candidate)) cmd = candidate;
                }
            }
            catch { }

            if (cmd.EndsWith(".bat", StringComparison.OrdinalIgnoreCase) || cmd.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase))
            {
                string comspec = Environment.GetEnvironmentVariable("COMSPEC");
                if (string.IsNullOrEmpty(comspec)) comspec = "cmd.exe";

                args = "/c \"\"" + cmd + "\" " + (args ?? "") + "\"";
                cmd = comspec;
            }

            string resolvedWorkdir = ResolveWorkdir(ctx, workdir);
            return RunProcess(ctx, cmd, args ?? "", resolvedWorkdir, timeoutMs <= 0 ? 180000 : timeoutMs, source);
        }

        static string ResolveWorkdir(ProjectContext ctx, string workdir)
        {
            if (string.IsNullOrEmpty(workdir)) return ctx.RootPath;

            try
            {
                if (Path.IsPathRooted(workdir)) return workdir;
                return Path.Combine(ctx.RootPath, workdir);
            }
            catch
            {
                return ctx.RootPath;
            }
        }

        static BuildResult RunProcess(ProjectContext ctx, string fileName, string arguments, string workingDirectory, int timeoutMs, string source)
        {
            var result = new BuildResult();
            result.StartTime = DateTime.Now;
            result.ProjectRoot = ctx != null ? ctx.RootPath : BaseDir;
            result.Command = fileName + " " + arguments;

            var sbOut = new StringBuilder();
            var sbErr = new StringBuilder();
            var sync = new object();
            var sw = Stopwatch.StartNew();

            Exception exception = null;
            int exitCode = -1;
            bool timedOut = false;

            try
            {
                var psi = new ProcessStartInfo();
                psi.FileName = fileName;
                psi.Arguments = arguments ?? "";
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.CreateNoWindow = true;
                psi.StandardOutputEncoding = Encoding.UTF8;
                psi.StandardErrorEncoding = Encoding.UTF8;

                if (!string.IsNullOrEmpty(workingDirectory) && Directory.Exists(workingDirectory))
                    psi.WorkingDirectory = workingDirectory;

                using (var p = Process.Start(psi))
                {
                    p.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e)
                    {
                        if (e.Data != null)
                        {
                            lock (sync) { sbOut.AppendLine(e.Data); }
                        }
                    };

                    p.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e)
                    {
                        if (e.Data != null)
                        {
                            lock (sync) { sbErr.AppendLine(e.Data); }
                        }
                    };

                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();

                    bool exited = p.WaitForExit(timeoutMs);

                    if (exited)
                    {
                        p.WaitForExit();
                        exitCode = p.ExitCode;
                    }
                    else
                    {
                        timedOut = true;
                        try { p.Kill(); } catch { }
                        try { p.WaitForExit(2000); } catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                exception = ex;
            }

            sw.Stop();
            result.DurationMs = sw.ElapsedMilliseconds;

            var output = new StringBuilder();
            output.AppendLine("CMD: " + fileName + " " + arguments);
            output.AppendLine("TIME: " + result.DurationMs + " ms");
            output.AppendLine();

            if (sbOut.Length > 0)
            {
                output.AppendLine("STDOUT:");
                output.AppendLine(sbOut.ToString());
            }

            if (sbErr.Length > 0)
            {
                output.AppendLine("STDERR:");
                output.AppendLine(sbErr.ToString());
            }

            if (timedOut)
                output.AppendLine("TIMEOUT: процесс не завершился за " + timeoutMs + " ms");

            if (exception != null)
                output.AppendLine("EXCEPTION: " + exception.Message);

            result.Output = Sanitize(output.ToString());
            result.ExitCode = exitCode;

            try
            {
                string logDir = GetReportDir(ctx);
                string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
                result.LogFile = Path.Combine(logDir, source + "_" + stamp + ".log");
                File.WriteAllText(result.LogFile, result.Output, new UTF8Encoding(false));
            }
            catch { }

            bool success = !timedOut && exception == null && (source == "run" || source == "verify" || exitCode == 0);
            result.Success = success;

            if (!success)
                result.Error = ClassifyError(result.Output, exception, timedOut, exitCode);

            return result;
        }

        static void UpdateLastBuild(BuildResult result)
        {
            lock (GateLock)
            {
                lastBuildTime = DateTime.Now;
                lastBuildOk = result.Success;
                lastBuildStatus = result.Success ? "OK" : ("FAIL" + (result.Error != null ? ":" + result.Error.Type : ""));
            }
        }

        // ══════════════════════════════════════════════
        //  ERROR CLASSIFIER
        // ══════════════════════════════════════════════

        public static ErrorInfo ClassifyError(string output, Exception ex, bool timeout, int exitCode)
        {
            string full = (output ?? "") + (ex != null ? " EXCEPTION: " + ex.Message : "");
            string t = full.ToLowerInvariant();

            if (timeout)
            {
                return new ErrorInfo
                {
                    Type = "timeout",
                    Cause = "Операция не завершилась за таймаут",
                    NextStep = "Увеличить timeoutMs в build.json или проверить зависший процесс",
                    CanAutoRepair = false
                };
            }

            if (t.Contains("dotnet sdk не найден") || t.Contains("dotnet не найден"))
            {
                return new ErrorInfo
                {
                    Type = "toolchain",
                    Cause = "dotnet SDK не найден",
                    NextStep = "Установи .NET SDK или добавь dotnet в PATH",
                    CanAutoRepair = false
                };
            }

            if ((t.Contains("dotnet") || t.Contains("npm") || t.Contains("python")) &&
                (t.Contains("не удается найти") || t.Contains("could not find") || t.Contains("not found") || t.Contains("не является внутренней")))
            {
                return new ErrorInfo
                {
                    Type = "toolchain",
                    Cause = "Инструмент сборки не найден",
                    NextStep = "Проверь PATH и установку тулчейна",
                    CanAutoRepair = false
                };
            }

            if (t.Contains("waf") || t.Contains("rgv587_flag") || t.Contains("aliyun") || t.Contains("captcha"))
            {
                return new ErrorInfo
                {
                    Type = "waf",
                    Cause = "WAF/антибот блокирует запрос",
                    NextStep = "Выждать cooldown, уменьшить частоту запросов",
                    CanAutoRepair = false
                };
            }

            if (t.Contains("401") || t.Contains("403") || t.Contains("auth") || t.Contains("token"))
            {
                return new ErrorInfo
                {
                    Type = "auth",
                    Cause = "Проблема аутентификации или токен истёк",
                    NextStep = "Обнови TOKEN / COOKIE в qwen_config.txt",
                    CanAutoRepair = false
                };
            }

            if (ex is WebException || t.Contains("network") || t.Contains("unable to connect") || t.Contains("dns"))
            {
                return new ErrorInfo
                {
                    Type = "network",
                    Cause = "Сетевая ошибка",
                    NextStep = "Проверить интернет/proxy/DNS",
                    CanAutoRepair = false
                };
            }

            if (t.Contains("pipe"))
            {
                return new ErrorInfo
                {
                    Type = "pipe",
                    Cause = "Ошибка named pipe / IPC",
                    NextStep = "Перезапустить main/helper",
                    CanAutoRepair = false
                };
            }

            if (t.Contains("port") && (t.Contains("in use") || t.Contains("занят")))
            {
                return new ErrorInfo
                {
                    Type = "port_conflict",
                    Cause = "Порт уже используется",
                    NextStep = "Остановить другой процесс или поменять порт",
                    CanAutoRepair = false
                };
            }

            if (t.Contains("access denied") || t.Contains("отказано в доступе") || t.Contains("unauthorizedaccess"))
            {
                return new ErrorInfo
                {
                    Type = "permissions",
                    Cause = "Нет доступа к файлу или папке",
                    NextStep = "Проверить права и занятость файлов",
                    CanAutoRepair = false
                };
            }

            // Ошибки сборки/компиляции должны ловиться ДО file_io
            if (t.Contains("error cs") || t.Contains("error msb") || t.Contains("error nu") ||
                t.Contains("build failed") || t.Contains("ошибка сборки") ||
                t.Contains("compilation") || t.Contains("compile") ||
                t.Contains("syntax") || t.Contains("compiler"))
            {
                return new ErrorInfo
                {
                    Type = "build",
                    Cause = "Ошибка сборки/компиляции",
                    NextStep = "Запустить /repair или посмотреть отчёт сборки",
                    CanAutoRepair = true
                };
            }

            if (t.Contains("build.json") || t.Contains("manifest"))
            {
                return new ErrorInfo
                {
                    Type = "manifest",
                    Cause = "Ошибка manifest/build.json",
                    NextStep = "Проверить синтаксис build.json (/doctor)",
                    CanAutoRepair = false
                };
            }

            if ((t.Contains(".csproj") || t.Contains(".sln")) &&
                (t.Contains("не найден") || t.Contains("not found") || t.Contains("could not find")))
            {
                return new ErrorInfo
                {
                    Type = "project_missing",
                    Cause = "Не найден project file",
                    NextStep = "Укажи путь к .csproj/.sln или build.json",
                    CanAutoRepair = false
                };
            }

            if (ex is IOException || ex is UnauthorizedAccessException ||
                t.Contains("could not find file") || t.Contains("directory not found") ||
                t.Contains("не удается найти указанный файл"))
            {
                return new ErrorInfo
                {
                    Type = "file_io",
                    Cause = "Ошибка доступа к файлу/диску",
                    NextStep = "Проверить путь, права и наличие файла",
                    CanAutoRepair = false
                };
            }

            if (exitCode != 0)
            {
                return new ErrorInfo
                {
                    Type = "runtime",
                    Cause = "Процесс завершился с ненулевым кодом",
                    NextStep = "Смотри stdout/stderr и отчёт",
                    CanAutoRepair = true
                };
            }

            return new ErrorInfo
            {
                Type = "unknown",
                Cause = "Неизвестная ошибка",
                NextStep = "Запустить /doctor",
                CanAutoRepair = false
            };
        }

        static BuildResult MakeFail(string message, bool canAutoRepair)
        {
            return MakeFail(message, canAutoRepair, "unknown");
        }

        static BuildResult MakeFail(string message, bool canAutoRepair, string type)
        {
            var r = new BuildResult();
            r.Success = false;
            r.ExitCode = -1;
            r.Output = message;
            r.StartTime = DateTime.Now;
            r.Command = "buildgate";
            r.ProjectRoot = LastContext != null ? LastContext.RootPath : BaseDir;

            r.Error = new ErrorInfo
            {
                Type = type,
                Cause = message,
                NextStep = "Смотри отчёт сборки или выполни /doctor",
                CanAutoRepair = canAutoRepair
            };

            if (type == "toolchain")
                r.Error.NextStep = "Установи/добавь в PATH нужный тулчейн (dotnet SDK, csc, npm, python)";

            if (type == "project_missing")
                r.Error.NextStep = "Укажи корректный путь к проекту или build.json";

            if (type == "manifest")
                r.Error.NextStep = "Исправь build.json или удали его для auto-detect";

            LastError = r.Error;
            return r;
        }

        static BuildResult MakeSuccess(string message)
        {
            var r = new BuildResult();
            r.Success = true;
            r.ExitCode = 0;
            r.Output = message;
            r.StartTime = DateTime.Now;
            r.Command = "buildgate";
            r.ProjectRoot = LastContext != null ? LastContext.RootPath : BaseDir;
            return r;
        }

        public static void ShowResult(BuildResult result)
        {
            if (result == null)
            {
                WriteColored(ConsoleColor.Red, "  [XX] Пустой результат\n");
                return;
            }

            if (result.Success)
                WriteColored(ConsoleColor.Green, "  [OK] Успешно\n");
            else
                WriteColored(ConsoleColor.Red, "  [XX] Ошибка\n");

            if (result.Error != null)
            {
                WriteColored(ConsoleColor.Yellow, "  TYPE : " + result.Error.Type + "\n");
                WriteColored(ConsoleColor.Yellow, "  CAUSE: " + result.Error.Cause + "\n");
                WriteColored(ConsoleColor.Yellow, "  NEXT : " + result.Error.NextStep + "\n");
            }

            if (!string.IsNullOrEmpty(result.LogFile))
                WriteColored(ConsoleColor.DarkGray, "  LOG  : " + result.LogFile + "\n");

            WriteColored(ConsoleColor.DarkGray, "  TIME : " + result.DurationMs + " ms\n");
        }

        // ══════════════════════════════════════════════
        //  REPORTS
        // ══════════════════════════════════════════════

        public static string GetReportDir(ProjectContext ctx)
        {
            string dir = ctx != null ? ctx.ReportDir : DefaultReportDir;
            if (string.IsNullOrEmpty(dir)) dir = DefaultReportDir;

            try
            {
                if (!Path.IsPathRooted(dir))
                {
                    string root = ctx != null ? ctx.RootPath : BaseDir;
                    dir = Path.Combine(root, dir);
                }

                Directory.CreateDirectory(dir);
                return dir;
            }
            catch
            {
                string fallback = Path.Combine(BaseDir, ".newera", "reports");
                Directory.CreateDirectory(fallback);
                return fallback;
            }
        }

        public static string CreateTextReport(ProjectContext ctx, string command, string status, string content, string customPath)
        {
            try
            {
                string basePath;

                if (!string.IsNullOrEmpty(customPath))
                {
                    basePath = customPath;
                    if (!Path.IsPathRooted(basePath)) basePath = Path.Combine(ctx.RootPath, basePath);
                }
                else
                {
                    string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
                    basePath = Path.Combine(GetReportDir(ctx), command + "_" + stamp + ".md");
                }

                string dir = Path.GetDirectoryName(basePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                var md = new StringBuilder();
                md.AppendLine("# NEW ERA BUILD GATE REPORT");
                md.AppendLine("- time: " + DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss"));
                md.AppendLine("- command: " + command);
                md.AppendLine("- status: " + status);
                md.AppendLine("- root: " + ctx.RootPath);
                md.AppendLine("- project_type: " + ctx.ProjectType);
                md.AppendLine("- toolchain: " + DetectToolchain(ctx));
                md.AppendLine();
                md.AppendLine("```text");
                md.AppendLine(Sanitize(content));
                md.AppendLine("```");

                File.WriteAllText(basePath, md.ToString(), new UTF8Encoding(false));

                string jsonPath = Path.ChangeExtension(basePath, ".json");

                var json = new StringBuilder();
                json.Append("{");
                json.Append("\"time\":\"" + JsonEscape(DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss")) + "\",");
                json.Append("\"command\":\"" + JsonEscape(command) + "\",");
                json.Append("\"status\":\"" + JsonEscape(status) + "\",");
                json.Append("\"root\":\"" + JsonEscape(ctx.RootPath) + "\",");
                json.Append("\"project_type\":\"" + JsonEscape(ctx.ProjectType) + "\",");
                json.Append("\"toolchain\":\"" + JsonEscape(DetectToolchain(ctx)) + "\"");
                json.Append("}");

                File.WriteAllText(jsonPath, json.ToString(), new UTF8Encoding(false));

                LastReportPath = basePath;
                return basePath;
            }
            catch
            {
                return null;
            }
        }

        static string CreateBuildReport(ProjectContext ctx, BuildResult result, string customPath)
        {
            var content = new StringBuilder();

            if (result != null)
            {
                content.AppendLine("SUCCESS: " + (result.Success ? "YES" : "NO"));
                content.AppendLine("EXIT_CODE: " + result.ExitCode);
                content.AppendLine("DURATION: " + result.DurationMs + " ms");
                content.AppendLine("COMMAND: " + result.Command);
                content.AppendLine("LOG_FILE: " + result.LogFile);
                content.AppendLine();

                if (result.Error != null)
                {
                    content.AppendLine("ERROR_TYPE: " + result.Error.Type);
                    content.AppendLine("ERROR_CAUSE: " + result.Error.Cause);
                    content.AppendLine("NEXT_STEP: " + result.Error.NextStep);
                    content.AppendLine("AUTO_REPAIR: " + (result.Error.CanAutoRepair ? "YES" : "NO"));
                    content.AppendLine();
                }

                content.AppendLine("CHANGED_FILES:");

                lock (GateLock)
                {
                    if (ChangedFiles.Count == 0) content.AppendLine("- нет");
                    else foreach (string f in ChangedFiles) content.AppendLine("- " + f);
                }

                content.AppendLine();

                if (ctx.IncludeLogTail && !string.IsNullOrEmpty(result.Output))
                {
                    content.AppendLine("OUTPUT_TAIL:");
                    content.AppendLine(TailText(result.Output, 4000));
                }
            }

            return CreateTextReport(ctx, "build", result != null && result.Success ? "OK" : "FAIL", content.ToString(), customPath);
        }

        static string CreateVerifyReport(ProjectContext ctx, VerifyResult vr, string customPath)
        {
            var content = new StringBuilder();

            content.AppendLine("VERIFY_STATUS: " + vr.Status);
            content.AppendLine("DETAILS: " + vr.Details);
            content.AppendLine();

            if (vr.LastBuildResult != null)
            {
                content.AppendLine("LAST_COMMAND: " + vr.LastBuildResult.Command);
                content.AppendLine("EXIT_CODE: " + vr.LastBuildResult.ExitCode);
                content.AppendLine();

                if (ctx.IncludeLogTail && !string.IsNullOrEmpty(vr.LastBuildResult.Output))
                {
                    content.AppendLine("OUTPUT_TAIL:");
                    content.AppendLine(TailText(vr.LastBuildResult.Output, 4000));
                }
            }

            return CreateTextReport(ctx, "verify", vr.Status, content.ToString(), customPath);
        }

        static string BuildJsonSummary(ProjectContext ctx, BuildResult result, string reportPath)
        {
            var sb = new StringBuilder();

            sb.Append("{");
            sb.Append("\"success\":" + (result.Success ? "true" : "false") + ",");
            sb.Append("\"exit_code\":" + result.ExitCode + ",");
            sb.Append("\"duration_ms\":" + result.DurationMs + ",");
            sb.Append("\"root\":\"" + JsonEscape(ctx.RootPath) + "\",");
            sb.Append("\"project_type\":\"" + JsonEscape(ctx.ProjectType) + "\",");
            sb.Append("\"toolchain\":\"" + JsonEscape(DetectToolchain(ctx)) + "\",");

            if (result.Error != null)
            {
                sb.Append("\"error\":{");
                sb.Append("\"type\":\"" + JsonEscape(result.Error.Type) + "\",");
                sb.Append("\"cause\":\"" + JsonEscape(result.Error.Cause) + "\",");
                sb.Append("\"next_step\":\"" + JsonEscape(result.Error.NextStep) + "\",");
                sb.Append("\"can_auto_repair\":" + (result.Error.CanAutoRepair ? "true" : "false"));
                sb.Append("},");
            }

            sb.Append("\"report\":\"" + JsonEscape(reportPath ?? "") + "\"");
            sb.Append("}");

            return sb.ToString();
        }

        static string TailText(string text, int maxChars)
        {
            if (string.IsNullOrEmpty(text)) return "";
            if (text.Length <= maxChars) return text;
            return text.Substring(text.Length - maxChars);
        }

        static string Sanitize(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";

            if (!string.IsNullOrEmpty(Token)) text = text.Replace(Token, "***");
            if (!string.IsNullOrEmpty(Token2)) text = text.Replace(Token2, "***");
            if (!string.IsNullOrEmpty(CookieHeader)) text = text.Replace(CookieHeader, "***");

            try
            {
                text = Regex.Replace(text, @"Bearer\s+[A-Za-z0-9\-._~+/]+=*", "Bearer ***");
            }
            catch { }

            return text;
        }

        static string JsonEscape(string s)
        {
            if (s == null) return "";

            var sb = new StringBuilder();

            foreach (char c in s)
            {
                if (c == '"') sb.Append("\\\"");
                else if (c == '\\') sb.Append("\\\\");
                else if (c == '\n') sb.Append("\\n");
                else if (c == '\r') sb.Append("\\r");
                else if (c == '\t') sb.Append("\\t");
                else if (c < 32) sb.Append("\\u" + ((int)c).ToString("x4"));
                else sb.Append(c);
            }

            return sb.ToString();
        }

        // ══════════════════════════════════════════════
        //  ARGUMENT PARSER
        // ══════════════════════════════════════════════

        public static ParsedArgs ParseArgs(string input, string command)
        {
            var result = new ParsedArgs();

            string rest = (input ?? "").Trim();
            string lower = rest.ToLowerInvariant();

            if (lower.StartsWith(command))
                rest = rest.Substring(command.Length).Trim();

            List<string> tokens = Tokenize(rest);

            for (int i = 0; i < tokens.Count; i++)
            {
                string token = tokens[i];

                if (token.StartsWith("--"))
                {
                    string key = token.Substring(2);

                    if (IsBoolOption(key))
                    {
                        result.Options[key] = "1";
                    }
                    else
                    {
                        string value = "";

                        if (i + 1 < tokens.Count && !tokens[i + 1].StartsWith("--"))
                        {
                            value = tokens[i + 1];
                            i++;
                        }

                        result.Options[key] = value;
                    }
                }
                else
                {
                    if (string.IsNullOrEmpty(result.Path))
                        result.Path = token;
                }
            }

            return result;
        }

        static bool IsBoolOption(string key)
        {
            foreach (string b in BoolOptions)
            {
                if (b == key) return true;
            }

            return false;
        }

        static List<string> Tokenize(string s)
        {
            var list = new List<string>();
            var sb = new StringBuilder();
            bool inQuotes = false;

            foreach (char c in s)
            {
                if (c == '"')
                {
                    inQuotes = !inQuotes;
                    continue;
                }

                if (c == ' ' && !inQuotes)
                {
                    if (sb.Length > 0)
                    {
                        list.Add(sb.ToString());
                        sb.Length = 0;
                    }

                    continue;
                }

                sb.Append(c);
            }

            if (sb.Length > 0)
                list.Add(sb.ToString());

            return list;
        }

        // ══════════════════════════════════════════════
        //  JSON HELPERS
        // ══════════════════════════════════════════════

        static Dictionary<string, object> JDict(Dictionary<string, object> d, string key)
        {
            if (d != null && d.ContainsKey(key))
                return d[key] as Dictionary<string, object>;

            return null;
        }

        static string JStr(Dictionary<string, object> d, string key, string fallback)
        {
            if (d != null && d.ContainsKey(key))
            {
                string s = d[key] as string;
                if (s != null) return s;
            }

            return fallback;
        }

        static int JInt(Dictionary<string, object> d, string key, int fallback)
        {
            if (d != null && d.ContainsKey(key))
            {
                object o = d[key];

                if (o is int) return (int)o;
                if (o is double) return (int)(double)o;

                int v;
                if (int.TryParse(o.ToString(), out v)) return v;
            }

            return fallback;
        }

        static bool JBool(Dictionary<string, object> d, string key, bool fallback)
        {
            if (d != null && d.ContainsKey(key))
            {
                object o = d[key];

                if (o is bool) return (bool)o;

                bool v;
                if (bool.TryParse(o.ToString(), out v)) return v;

                string s = o.ToString().ToLowerInvariant();
                if (s == "1" || s == "yes" || s == "on") return true;
                if (s == "0" || s == "no" || s == "off") return false;
            }

            return fallback;
        }

        static string[] JArr(Dictionary<string, object> d, string key)
        {
            if (d == null || !d.ContainsKey(key)) return null;

            object[] arr = d[key] as object[];
            if (arr == null) return null;

            var list = new List<string>();

            foreach (object o in arr)
            {
                if (o != null) list.Add(o.ToString());
            }

            return list.ToArray();
        }

        // ══════════════════════════════════════════════
        //  ROLLBACK / UNDO
        // ══════════════════════════════════════════════

        public static string CreateRollbackPoint(string target)
        {
            return CreateRollbackPoint(target, "auto");
        }

        public static string CreateRollbackPoint(string target, string kind)
        {
            try
            {
                string root = ResolveProjectRootFromPath(target);
                if (string.IsNullOrEmpty(root)) return null;

                string snapRoot = Path.Combine(root, ".newera", "rollback");
                Directory.CreateDirectory(snapRoot);

                string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
                string snapDir = Path.Combine(snapRoot, stamp + "_" + kind);
                string filesDir = Path.Combine(snapDir, "files");
                Directory.CreateDirectory(filesDir);

                List<string> files = CollectRollbackFiles(root, 500);
                if (files.Count == 0) return null;

                var manifest = new StringBuilder();
                manifest.AppendLine("BASE=" + root);
                manifest.AppendLine("KIND=" + kind);
                manifest.AppendLine("TIME=" + DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss"));

                foreach (string file in files)
                {
                    string rel = MakeRelativePath(root, file).Replace('\\', '/');
                    string dest = Path.Combine(filesDir, rel.Replace('/', Path.DirectorySeparatorChar));

                    string destDir = Path.GetDirectoryName(dest);
                    if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
                        Directory.CreateDirectory(destDir);

                    File.Copy(file, dest, true);
                    manifest.AppendLine(rel);
                }

                File.WriteAllText(Path.Combine(snapDir, "manifest.txt"), manifest.ToString(), new UTF8Encoding(false));

                PruneSnapshots(snapRoot, MaxSnapshots);

                return snapDir;
            }
            catch
            {
                return null;
            }
        }

        static string GetSnapshotRoot(ProjectContext ctx)
        {
            string dir = ctx.SnapshotDir;
            if (string.IsNullOrEmpty(dir)) dir = ".newera/rollback";

            if (Path.IsPathRooted(dir)) return dir;

            return Path.Combine(ctx.RootPath, dir);
        }

        static List<string> GetSnapshotDirs(string snapRoot)
        {
            var list = new List<string>();

            try
            {
                if (!Directory.Exists(snapRoot)) return list;

                string[] dirs = Directory.GetDirectories(snapRoot);
                Array.Sort(dirs, StringComparer.OrdinalIgnoreCase);
                Array.Reverse(dirs);

                list.AddRange(dirs);
            }
            catch { }

            return list;
        }

        static void PruneSnapshots(string snapRoot, int max)
        {
            try
            {
                List<string> dirs = GetSnapshotDirs(snapRoot);
                if (dirs.Count <= max) return;

                for (int i = max; i < dirs.Count; i++)
                {
                    try { Directory.Delete(dirs[i], true); } catch { }
                }
            }
            catch { }
        }

        static bool RestoreSnapshot(ProjectContext ctx, string snapDir)
        {
            try
            {
                string manifestPath = Path.Combine(snapDir, "manifest.txt");
                if (!File.Exists(manifestPath)) return false;

                string filesDir = Path.Combine(snapDir, "files");
                if (!Directory.Exists(filesDir)) return false;

                string[] lines = ReadTextAuto(manifestPath).Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

                string baseDir = null;
                var rels = new List<string>();

                foreach (string raw in lines)
                {
                    string line = raw.Trim();
                    if (line.Length == 0) continue;

                    if (line.StartsWith("BASE=")) baseDir = line.Substring(5).Trim();
                    else if (!line.StartsWith("KIND=") && !line.StartsWith("TIME=")) rels.Add(line);
                }

                if (string.IsNullOrEmpty(baseDir)) baseDir = ctx.RootPath;

                if (!Directory.Exists(baseDir))
                    Directory.CreateDirectory(baseDir);

                int restored = 0;

                foreach (string rel in rels)
                {
                    try
                    {
                        string src = Path.Combine(filesDir, rel.Replace('/', Path.DirectorySeparatorChar));
                        string dst = Path.Combine(baseDir, rel.Replace('/', Path.DirectorySeparatorChar));

                        if (!File.Exists(src)) continue;

                        string dstDir = Path.GetDirectoryName(dst);
                        if (!string.IsNullOrEmpty(dstDir) && !Directory.Exists(dstDir))
                            Directory.CreateDirectory(dstDir);

                        File.Copy(src, dst, true);
                        restored++;
                    }
                    catch { }
                }

                return restored > 0;
            }
            catch
            {
                return false;
            }
        }

        static List<string> CollectRollbackFiles(string root, int max)
        {
            var files = new List<string>();

            try
            {
                CollectRollbackFilesRecursive(root, root, files, max, 0);
            }
            catch { }

            return files;
        }

        static void CollectRollbackFilesRecursive(string root, string dir, List<string> files, int max, int depth)
        {
            if (depth > 6 || files.Count >= max) return;

            try
            {
                foreach (string f in Directory.GetFiles(dir))
                {
                    if (files.Count >= max) break;

                    string name = Path.GetFileName(f);
                    if (string.IsNullOrEmpty(name) || name.StartsWith(".")) continue;

                    string ext = (Path.GetExtension(f) ?? "").ToLowerInvariant();
                    bool ok = false;

                    foreach (string ce in ContextExtensions)
                    {
                        if (ce == ext)
                        {
                            ok = true;
                            break;
                        }
                    }

                    if (name == "build.json") ok = true;
                    if (!ok) continue;

                    string rel = MakeRelativePath(root, f).Replace('\\', '/');
                    if (IsDefaultForbiddenRelPath(rel)) continue;

                    files.Add(f);
                }

                foreach (string d in Directory.GetDirectories(dir))
                {
                    if (files.Count >= max) break;

                    string name = Path.GetFileName(d);
                    if (string.IsNullOrEmpty(name)) continue;

                    string lower = name.ToLowerInvariant();

                    if (lower == "bin" || lower == "obj" || lower == ".git" || lower == ".vs" ||
                        lower == ".vscode" || lower == ".idea" || lower == "node_modules" ||
                        lower == "program_from_the_cli" || lower == ".newera")
                        continue;

                    CollectRollbackFilesRecursive(root, d, files, max, depth + 1);
                }
            }
            catch { }
        }

        // ══════════════════════════════════════════════
        //  FILE COLLECTION / PATH SAFETY
        // ══════════════════════════════════════════════

        static List<string> CollectCsFiles(string root, int max)
        {
            var list = new List<string>();

            try
            {
                CollectCsFilesRecursive(root, list, max, 0);
            }
            catch { }

            return list;
        }

        static void CollectCsFilesRecursive(string dir, List<string> list, int max, int depth)
        {
            if (depth > 6 || list.Count >= max) return;

            try
            {
                foreach (string f in Directory.GetFiles(dir, "*.cs"))
                {
                    if (list.Count >= max) break;

                    string rel = f.Replace('\\', '/').ToLowerInvariant();

                    if (rel.Contains("/bin/") || rel.Contains("/obj/") || rel.Contains("/.newera/") ||
                        rel.Contains("/program_from_the_cli/") || rel.Contains("/node_modules/"))
                        continue;

                    list.Add(f);
                }

                foreach (string d in Directory.GetDirectories(dir))
                {
                    if (list.Count >= max) break;

                    string name = Path.GetFileName(d);
                    if (string.IsNullOrEmpty(name)) continue;

                    string lower = name.ToLowerInvariant();

                    if (lower == "bin" || lower == "obj" || lower == ".git" || lower == ".vs" ||
                        lower == ".vscode" || lower == ".idea" || lower == "node_modules" ||
                        lower == "program_from_the_cli" || lower == ".newera")
                        continue;

                    CollectCsFilesRecursive(d, list, max, depth + 1);
                }
            }
            catch { }
        }

        public static bool IsPathAllowed(ProjectContext ctx, string relPath, out string fullPath)
        {
            fullPath = null;

            if (ctx == null || string.IsNullOrEmpty(relPath))
                return false;

            if (!TryResolveSafeOutputPath(ctx.RootPath, relPath, out fullPath))
                return false;

            string rel = MakeRelativePath(ctx.RootPath, fullPath).Replace('\\', '/').ToLowerInvariant();

            if (IsDefaultForbiddenRelPath(rel))
                return false;

            foreach (string forbidden in ctx.ForbiddenPaths)
            {
                string f = (forbidden ?? "").Replace('\\', '/').Trim().ToLowerInvariant();
                if (string.IsNullOrEmpty(f)) continue;

                if (rel.StartsWith(f) || rel.Contains("/" + f) || rel.Contains(f + "/"))
                    return false;
            }

            if (ctx.AllowedPaths.Count > 0)
            {
                bool allowed = false;

                foreach (string a in ctx.AllowedPaths)
                {
                    string allow = (a ?? "").Replace('\\', '/').Trim().ToLowerInvariant();
                    if (string.IsNullOrEmpty(allow)) continue;

                    if (rel.StartsWith(allow) || rel.Contains("/" + allow) || rel.Contains(allow + "/"))
                    {
                        allowed = true;
                        break;
                    }
                }

                if (!allowed) return false;
            }

            return true;
        }

        static bool IsDefaultForbiddenRelPath(string rel)
        {
            rel = rel.Replace('\\', '/').ToLowerInvariant();

            if (rel.StartsWith("program_from_the_cli/")) return true;
            if (rel.StartsWith(".newera/")) return true;
            if (rel.StartsWith(".git/")) return true;
            if (rel.StartsWith("bin/")) return true;
            if (rel.StartsWith("obj/")) return true;
            if (rel.StartsWith("node_modules/")) return true;

            if (rel.EndsWith(".exe")) return true;
            if (rel.EndsWith(".dll")) return true;
            if (rel.EndsWith(".pdb")) return true;

            if (rel == "qwen_config.txt") return true;
            if (rel.EndsWith(".env")) return true;

            return false;
        }

        // ══════════════════════════════════════════════
        //  CHANGE TRACKING / RELIABILITY
        // ══════════════════════════════════════════════

        public static void BeginChangedFiles()
        {
            lock (GateLock) { ChangedFiles.Clear(); }
        }

        public static void AddChangedFiles(CodeWriterResult result)
        {
            if (result == null || result.IsEmpty) return;

            lock (GateLock)
            {
                foreach (var op in result.Operations)
                {
                    if (!string.IsNullOrEmpty(op.Path))
                        ChangedFiles.Add(op.Path);
                }
            }
        }

        public static void AppendReliabilitySafe(string source, string message)
        {
            try
            {
                string line = "[" + DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss") + "] " + source + ": " + message + Environment.NewLine;
                File.AppendAllText(Path.Combine(BaseDir, "reliability.log"), line, new UTF8Encoding(false));
            }
            catch { }
        }
    }
}