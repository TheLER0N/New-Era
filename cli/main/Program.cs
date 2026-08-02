// Program.cs — точка входа, состояния, конфигурация
// New Era CLI v5.3 · partial class MainConsole
// C# 5 / .NET Framework 4.x
//
// v5.3:
//   - Добавлен OrchestratorChatId.
//   - LoadOrchestratorConfig() теперь делает fallback на AI#2,
//     если отдельный оркестратор не сконфигурирован.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;

partial class MainConsole
{
    // ── Константы ──────────────────────────────────────────────
    const string PipeName       = "NewEraMainPipe";
    const string DefaultApiBase = "https://chat.qwen.ai";
    const int MaxHistoryEntries = 200;

    static readonly string BaseDir     = AppDomain.CurrentDomain.BaseDirectory;
    static readonly string ConfigFile  = Path.Combine(BaseDir, "qwen_config.txt");
    static readonly string HistoryFile = Path.Combine(BaseDir, "chat_history.dat");
    static readonly string DumpFile    = Path.Combine(BaseDir, "last_sse.json");
    static readonly string VersionFile = Path.Combine(BaseDir, "version.txt");

    // ── Состояние ──────────────────────────────────────────────
    static string Token        = null;
    static string ChatId       = null;
    static string ApiBaseUrl   = DefaultApiBase;
    static string CookieHeader = null;
    static string AppVersion   = "4.2";

    // ── Orchestrator (Dual-LLM) ───────────────────────────────
    static bool   OrchestratorEnabled = false;
    static string OrchestratorModel   = null;
    static string OrchestratorApiUrl  = null;
    static string OrchestratorToken   = null;
    static string OrchestratorChatId  = null;

    // ── Guardian / ArcMode: состояния объявлены в Config.cs ──

    static readonly object PrintLock   = new object();
    static readonly object HistoryLock = new object();
    static readonly CancellationTokenSource Cts = new CancellationTokenSource();

    static Process liveHelper = null;
    static volatile bool StopRequested = false;

    static List<HistoryEntry> History  = new List<HistoryEntry>();
    static volatile string LastResponseId = null;

    static volatile bool SpinnerActive = false;
    static Thread SpinnerThread        = null;

    // Настройки анимаций
    static bool AnimationsEnabled  = true;
    static int  TypewriterDelayMs  = 3;
    static bool ShowThinking       = false;

    // ══════════════════════════════════════════════════════════
    //  ENTRY
    // ══════════════════════════════════════════════════════════
    static int Main(string[] args)
    {
        try { Console.OutputEncoding = Encoding.UTF8; } catch { }
        try { Console.InputEncoding  = Encoding.UTF8; } catch { }

        Console.Title = "New Era v5";

        try { Console.CursorVisible = true; } catch { }

        try { ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072 | (SecurityProtocolType)768; }
        catch { }

        Console.CancelKeyPress += delegate(object s, ConsoleCancelEventArgs e)
        {
            StopRequested = true;
            e.Cancel = false;
        };

        try { LoadAppVersion(); }         catch { }
        try { LoadConfig(); }             catch { }
        try { LoadOrchestratorConfig(); } catch { }
        try { LoadGuardianConfig(); }     catch { }
        try { LoadHistory(); }            catch { }

        try { Console.Clear(); }          catch { }

        DrawBanner();

        Thread listener = new Thread(() => PipeListener(Cts.Token));
        listener.IsBackground = true;
        listener.Start();

        int exitCode = 0;

        try { Repl(); }
        catch (Exception ex)
        {
            WriteColored(ConsoleColor.Red, "  ✖ Критическая ошибка: " + ex.Message + "\n");
            exitCode = 1;
        }
        finally
        {
            StopLive();
            try { SaveHistory(); } catch { }
            try { Cts.Cancel(); }  catch { }
            try { listener.Join(1500); } catch { }
        }

        return exitCode;
    }

    static void LoadAppVersion()
    {
        try
        {
            if (File.Exists(VersionFile))
            {
                string v = ReadTextAuto(VersionFile).Trim();
                if (v.Length > 0 && v.Length <= 20) AppVersion = v;
            }
        }
        catch { }
    }

    // ══════════════════════════════════════════════════════════
    //  ORCHESTRATOR CONFIG
    // ══════════════════════════════════════════════════════════
    static void LoadOrchestratorConfig()
    {
        if (!File.Exists(ConfigFile)) return;

        try
        {
            string[] lines = ReadTextAuto(ConfigFile).Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

            foreach (string line in lines)
            {
                string t = line.Trim();

                if (t.StartsWith("MODEL="))
                {
                    string val = t.Substring(6).Trim();
                    if (val.Length > 0) PrimaryModel = val;
                }
                else if (t.StartsWith("QWEN_VERSION="))
                {
                    string val = t.Substring(13).Trim();
                    if (val.Length > 0) QwenVersion = val;
                }
                else if (t.StartsWith("ORCH_ENABLED="))
                {
                    string val = t.Substring(13).Trim().ToLowerInvariant();
                    OrchestratorEnabled = (val == "1" || val == "true" || val == "on" || val == "yes");
                }
                else if (t.StartsWith("ORCH_LINK="))
                {
                    string url = t.Substring(10).Trim();
                    if (url.StartsWith("http://") || url.StartsWith("https://"))
                    {
                        string tmpBase = OrchestratorApiUrl;
                        string tmpId = OrchestratorChatId;
                        ParseChatLink(url, ref tmpBase, ref tmpId);
                        OrchestratorApiUrl = tmpBase;
                        if (!string.IsNullOrEmpty(tmpId)) OrchestratorChatId = tmpId;
                    }
                }
                else if (t.StartsWith("ORCH_CHAT_ID=") && string.IsNullOrEmpty(OrchestratorChatId))
                {
                    OrchestratorChatId = ExtractChatId(t.Substring(13).Trim());
                }
                else if (t.StartsWith("ORCH_MODEL="))
                {
                    string val = t.Substring(11).Trim();
                    if (val.Length > 0) OrchestratorModel = val;
                }
                else if (t.StartsWith("ORCH_API_URL="))
                {
                    string val = t.Substring(13).Trim();
                    if (val.StartsWith("http://") || val.StartsWith("https://")) OrchestratorApiUrl = val;
                }
                else if (t.StartsWith("ORCH_TOKEN="))
                {
                    string val = t.Substring(11).Trim();
                    if (val.Length > 0) OrchestratorToken = val;
                }
            }

            // ── Fallback на AI#2, если отдельный оркестратор не задан ──
            // Это чинит ситуацию, когда "второй ИИ" сконфигурирован как AI#2,
            // но оркестратор продолжал ходить в primary.
            if (string.IsNullOrEmpty(OrchestratorToken) && !string.IsNullOrEmpty(Token2))
                OrchestratorToken = Token2;

            if ((string.IsNullOrEmpty(OrchestratorApiUrl) || OrchestratorApiUrl == DefaultApiBase)
                && !string.IsNullOrEmpty(ApiBaseUrl2) && ApiBaseUrl2 != DefaultApiBase)
            {
                OrchestratorApiUrl = ApiBaseUrl2;
            }

            if (string.IsNullOrEmpty(OrchestratorChatId) && !string.IsNullOrEmpty(ChatId2))
                OrchestratorChatId = ChatId2;

            if (string.IsNullOrEmpty(OrchestratorModel) && !string.IsNullOrEmpty(Ai2Model))
                OrchestratorModel = Ai2Model;
        }
        catch { }
    }

    // ══════════════════════════════════════════════════════════
    //  GUARDIAN CONFIG (SYSTEM_GUARDIAN + ArcMode)
    //  Синхронизировано с LoadConfig() в Config.cs
    // ══════════════════════════════════════════════════════════
    static void LoadGuardianConfig()
    {
        if (!File.Exists(ConfigFile)) return;

        try
        {
            string[] lines = ReadTextAuto(ConfigFile).Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

            foreach (string line in lines)
            {
                string t = line.Trim();

                if (t.StartsWith("GUARDIAN_ENABLED="))
                {
                    string val = t.Substring(17).Trim().ToLowerInvariant();
                    GuardianEnabled = (val == "1" || val == "true" || val == "on" || val == "yes");
                }
                else if (t.StartsWith("ARC_MODE="))
                {
                    string val = t.Substring(9).Trim().ToLowerInvariant();
                    ArcMode = (val == "1" || val == "true" || val == "on" || val == "yes");
                }
                else if (t.StartsWith("GUARDIAN_MODEL="))
                {
                    string val = t.Substring(15).Trim();
                    if (val.Length > 0) GuardianModel = val;
                }
                else if (t.StartsWith("GUARDIAN_API_URL="))
                {
                    string val = t.Substring(17).Trim();
                    if (val.StartsWith("http://") || val.StartsWith("https://")) GuardianApiUrl = val;
                }
                else if (t.StartsWith("GUARDIAN_TOKEN="))
                {
                    string val = t.Substring(15).Trim();
                    if (val.Length > 0) GuardianToken = val;
                }
            }
        }
        catch { }
    }
}