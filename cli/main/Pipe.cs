// Pipe.cs — именованный канал для связи с helper.exe
// New Era v7.2
using System;
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
server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
server.WaitForConnection();
using (var reader = new StreamReader(server, Encoding.UTF8)) {
string header = reader.ReadLine();
if (header == null) continue;
                if (header.StartsWith("[BATCH")) {
                    int count = 0;
                    var messages = new System.Collections.Generic.List<string>();
                    string line;
                    while ((line = reader.ReadLine()) != null) {
                        if (line == "[END]") break;
                        if (line.StartsWith("[#")) {
                            string text = line.Substring(line.IndexOf(']') + 1).Trim();
                            text = text.Replace("\\\\", "\\").Replace("\\n", "\n");
                            messages.Add(text);
                        }
                    }
                    if (messages.Count > 0) {
                        lock (PrintLock) {
                            Console.WriteLine();
                            foreach (string msg in messages) {
                                Console.ForegroundColor = ConsoleColor.Magenta;
                                Console.Write("  \u25C6 ");
                                Console.ForegroundColor = ConsoleColor.White;
                                Console.WriteLine(msg);
                                Console.WriteLine();
                            }
                            Console.ResetColor();
                        }
                        foreach (string msg in messages) AddHistory("helper", msg);
                    }
                }
            }
        } catch {
        } finally {
            if (server != null) {
                try { server.Dispose(); } catch { }
            }
        }
    }
}

static void LaunchHelper(string args)
{
    string helperPath = Path.Combine(BaseDir, "helper.exe");
    if (!File.Exists(helperPath)) {
        WriteColored(ConsoleColor.Red, "  \u2716 helper.exe не найден.\n");
        return;
    }
    try {
        var psi = new ProcessStartInfo(helperPath, args);
        psi.UseShellExecute = true;
        Process.Start(psi);
        WriteColored(ConsoleColor.Green, "  \u2714 helper.exe запущен.\n");
    } catch (Exception ex) {
        WriteColored(ConsoleColor.Red, "  \u2716 Ошибка запуска: " + ex.Message + "\n");
    }
}

static void LaunchHelperLive(string args)
{
    string helperPath = Path.Combine(BaseDir, "helper.exe");
    if (!File.Exists(helperPath)) {
        WriteColored(ConsoleColor.Red, "  \u2716 helper.exe не найден.\n");
        return;
    }
    lock (HelperLock) {
        try {
            if (liveHelper != null && !liveHelper.HasExited) {
                try { liveHelper.Kill(); } catch { }
                liveHelper = null;
            }
            var psi = new ProcessStartInfo(helperPath, args);
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = false;
            psi.RedirectStandardError = false;
            liveHelper = Process.Start(psi);
            LiveRequested = true;
            LastLiveArgs = args;
            WriteColored(ConsoleColor.Green, "  \u2714 LIVE запущен.\n");
        } catch (Exception ex) {
            WriteColored(ConsoleColor.Red, "  \u2716 Ошибка запуска: " + ex.Message + "\n");
        }
    }
}

static void StopLive()
{
    lock (HelperLock) {
        LiveRequested = false;
        if (liveHelper != null) {
            try { if (!liveHelper.HasExited) liveHelper.Kill(); } catch { }
            liveHelper = null;
        }
    }
}

static bool IsLiveRunning()
{
    lock (HelperLock) {
        if (liveHelper == null) return false;
        try { return !liveHelper.HasExited; } catch { return false; }
    }
}
}