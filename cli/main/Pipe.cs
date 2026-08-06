// Pipe.cs — pipe listener (helper→main), запуск/остановка helper
// New Era v7.1
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;

partial class MainConsole
{
    static void PipeListener(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && !StopRequested) {
            NamedPipeServerStream server = null;
            try {
                server = new NamedPipeServerStream(
                    PipeName, PipeDirection.In, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

                try {
                    server.WaitForConnection();
                } catch {
                    if (ct.IsCancellationRequested || StopRequested) break;
                    Thread.Sleep(500);
                    continue;
                }

                if (ct.IsCancellationRequested) break;

                using (var reader = new StreamReader(server, Encoding.UTF8)) {
                    string headerLine = reader.ReadLine();
                    if (headerLine == null || !headerLine.StartsWith("[BATCH")) continue;

                    var messages = new List<string>();
                    string line;
                    while ((line = reader.ReadLine()) != null) {
                        if (line == "[END]") break;
                        if (line.StartsWith("[#")) {
                            int spaceIdx = line.IndexOf(']');
                            if (spaceIdx >= 0 && spaceIdx + 2 < line.Length)
                                messages.Add(PipeDecode(line.Substring(spaceIdx + 2)));
                        }
                    }

                    if (messages.Count > 0) {
                        WriteColored(ConsoleColor.DarkGray,
                            "\r\n\u25CC Pipe: +" + messages.Count + " сообщ.\n");

                        // P0: порядок HistoryLock → PrintLock
                        foreach (string msg in messages) {
                            AddHistory("assistant", msg);
                            RenderAssistantMessage(msg);
                        }
                    }
                }
            } catch {
                if (ct.IsCancellationRequested || StopRequested) break;
                Thread.Sleep(500);
            } finally {
                if (server != null) { try { server.Dispose(); } catch { } }
            }
        }
    }

    static string PipeDecode(string text)
    {
        if (text == null) return "";
        var sb = new StringBuilder();
        for (int i = 0; i < text.Length; i++) {
            if (text[i] == '\\' && i + 1 < text.Length) {
                char next = text[i + 1];
                if (next == '\\') { sb.Append('\\'); i++; }
                else if (next == 'n') { sb.Append('\n'); i++; }
                else if (next == 'r') { sb.Append('\r'); i++; }
                else sb.Append(text[i]);
            } else sb.Append(text[i]);
        }
        return sb.ToString();
    }

    static void LaunchHelper(string args)
    {
        string helperPath = Path.Combine(BaseDir, "helper.exe");
        if (!File.Exists(helperPath)) {
            WriteColored(ConsoleColor.Red, "  \u2716 helper.exe не найден.\n");
            return;
        }

        try {
            var psi = new ProcessStartInfo {
                FileName = helperPath,
                Arguments = args,
                WorkingDirectory = BaseDir,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            Process.Start(psi);
            WriteColored(ConsoleColor.DarkGray, "  \u25CC helper запущен (snapshot).\n");
        } catch (Exception ex) {
            WriteColored(ConsoleColor.Red, "  \u2716 Не удалось запустить helper: " + ex.Message + "\n");
        }
    }

    static void LaunchHelperLive(string args)
    {
        StopLive();

        string helperPath = Path.Combine(BaseDir, "helper.exe");
        if (!File.Exists(helperPath)) {
            WriteColored(ConsoleColor.Red, "  \u2716 helper.exe не найден.\n");
            return;
        }

        try {
            var psi = new ProcessStartInfo {
                FileName = helperPath,
                Arguments = args,
                WorkingDirectory = BaseDir,
                UseShellExecute = true
            };

            liveHelper = Process.Start(psi);
            LiveRequested = true;
            LastLiveArgs = args;

            WriteColored(ConsoleColor.Green, "  \u2714 LIVE запущен (PID " + liveHelper.Id + ").\n");
        } catch (Exception ex) {
            WriteColored(ConsoleColor.Red, "  \u2716 Не удалось запустить live: " + ex.Message + "\n");
        }
    }

    static void StopLive()
    {
        LiveRequested = false;

        if (liveHelper != null) {
            try {
                if (!liveHelper.HasExited) {
                    liveHelper.Kill();
                    liveHelper.WaitForExit(3000);
                }
            } catch { }
            try { liveHelper.Dispose(); } catch { }
            liveHelper = null;
            WriteColored(ConsoleColor.DarkGray, "  \u25C2 live остановлен.\n");
        }
    }

    static bool IsLiveRunning()
    {
        if (liveHelper == null) return false;
        try { return !liveHelper.HasExited; } catch { return false; }
    }
}