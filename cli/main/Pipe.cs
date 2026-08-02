// Pipe.cs — pipe-listener (helper→main), запуск/остановка helper
// New Era CLI v4.2 · partial class MainConsole
// C# 5 / .NET Framework 4.x

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;

partial class MainConsole
{
    // ══════════════════════════════════════════════════════════
    //  PIPE LISTENER (helper → main)
    // ══════════════════════════════════════════════════════════

    static void PipeListener(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && !StopRequested)
        {
            try
            {
                using (var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous))
                {
                    try { server.WaitForConnection(); }
                    catch { if (ct.IsCancellationRequested || StopRequested) break; Thread.Sleep(500); continue; }

                    if (ct.IsCancellationRequested) break;

                    using (var reader = new StreamReader(server, Encoding.UTF8))
                    {
                        string headerLine = reader.ReadLine();
                        if (headerLine == null) continue;
                        if (!headerLine.StartsWith("[BATCH")) continue;

                        var messages = new List<string>();
                        string line;
                        while ((line = reader.ReadLine()) != null)
                        {
                            if (line == "[END]") break;
                            if (line.StartsWith("[#"))
                            {
                                int spaceIdx = line.IndexOf(']');
                                if (spaceIdx >= 0 && spaceIdx + 2 < line.Length)
                                {
                                    string payload = line.Substring(spaceIdx + 2);
                                    messages.Add(PipeDecode(payload));
                                }
                            }
                        }

                        if (messages.Count > 0)
                        {
                            WriteColored(ConsoleColor.DarkGray, "\r\n  \u25CC Pipe: +" + messages.Count + " \u0441\u043E\u043E\u0431\u0449.\n");
                            foreach (string msg in messages)
                            {
                                AddHistory("assistant", msg);
                                RenderAssistantMessage(msg);
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
                if (ct.IsCancellationRequested || StopRequested) break;
                Thread.Sleep(500);
            }
        }
    }

    static string PipeDecode(string text)
    {
        if (text == null) return "";
        var sb = new StringBuilder();
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\\' && i + 1 < text.Length)
            {
                char next = text[i + 1];
                if (next == '\\') { sb.Append('\\'); i++; }
                else if (next == 'n') { sb.Append('\n'); i++; }
                else if (next == 'r') { sb.Append('\r'); i++; }
                else sb.Append(text[i]);
            }
            else sb.Append(text[i]);
        }
        return sb.ToString();
    }

    // ══════════════════════════════════════════════════════════
    //  HELPER LAUNCH / STOP
    // ══════════════════════════════════════════════════════════

    static void LaunchHelper(string args)
    {
        string helperPath = Path.Combine(BaseDir, "helper.exe");
        if (!File.Exists(helperPath))
        {
            WriteColored(ConsoleColor.Red, "  \u2716 helper.exe \u043D\u0435 \u043D\u0430\u0439\u0434\u0435\u043D: " + helperPath + "\n");
            return;
        }

        try
        {
            var psi = new ProcessStartInfo();
            psi.FileName = helperPath;
            psi.Arguments = args;
            psi.WorkingDirectory = BaseDir;
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            Process.Start(psi);
            WriteColored(ConsoleColor.DarkGray, "  \u25CC helper \u0437\u0430\u043F\u0443\u0449\u0435\u043D (snapshot).\n");
        }
        catch (Exception ex)
        {
            WriteColored(ConsoleColor.Red, "  \u2716 \u041D\u0435 \u0443\u0434\u0430\u043B\u043E\u0441\u044C \u0437\u0430\u043F\u0443\u0441\u0442\u0438\u0442\u044C helper: " + ex.Message + "\n");
        }
    }

    static void LaunchHelperLive(string args)
    {
        StopLive();

        string helperPath = Path.Combine(BaseDir, "helper.exe");
        if (!File.Exists(helperPath))
        {
            WriteColored(ConsoleColor.Red, "  \u2716 helper.exe \u043D\u0435 \u043D\u0430\u0439\u0434\u0435\u043D: " + helperPath + "\n");
            return;
        }

        try
        {
            var psi = new ProcessStartInfo();
            psi.FileName = helperPath;
            psi.Arguments = args;
            psi.WorkingDirectory = BaseDir;
            psi.UseShellExecute = true;
            liveHelper = Process.Start(psi);
            WriteColored(ConsoleColor.Green, "  \u2714 LIVE \u0437\u0430\u043F\u0443\u0449\u0435\u043D (PID " + liveHelper.Id + ").\n");
        }
        catch (Exception ex)
        {
            WriteColored(ConsoleColor.Red, "  \u2716 \u041D\u0435 \u0443\u0434\u0430\u043B\u043E\u0441\u044C \u0437\u0430\u043F\u0443\u0441\u0442\u0438\u0442\u044C live: " + ex.Message + "\n");
        }
    }

    static void StopLive()
    {
        if (liveHelper != null)
        {
            try
            {
                if (!liveHelper.HasExited)
                {
                    liveHelper.Kill();
                    liveHelper.WaitForExit(3000);
                }
            }
            catch { }
            try { liveHelper.Dispose(); } catch { }
            liveHelper = null;
            WriteColored(ConsoleColor.DarkGray, "  \u25C2 live \u043E\u0441\u0442\u0430\u043D\u043E\u0432\u043B\u0435\u043D.\n");
        }
    }

    static bool IsLiveRunning()
    {
        if (liveHelper == null) return false;
        try { return !liveHelper.HasExited; }
        catch { return false; }
    }
}