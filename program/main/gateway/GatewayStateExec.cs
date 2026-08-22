using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace MainApp;

internal sealed partial class GatewayState
{
    public async Task<CommandResult> RunProcessAsync(string command, string? cwd, int timeoutMs)
    {
        var shell =
            command.Trim().StartsWith("powershell", StringComparison.OrdinalIgnoreCase) ||
            command.Trim().StartsWith("pwsh", StringComparison.OrdinalIgnoreCase)
                ? "PowerShell" : "CMD";

        var workDir = Directory.Exists(cwd) ? cwd : AppContext.BaseDirectory;

        try
        {
            var psi = new ProcessStartInfo("cmd.exe", $"/c {command}")
            {
                WorkingDirectory = workDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            using var p = new Process { StartInfo = psi };
            p.Start();

            var stdoutTask = p.StandardOutput.ReadToEndAsync();
            var stderrTask = p.StandardError.ReadToEndAsync();
            var exited = await Task.Run(() => p.WaitForExit(timeoutMs));

            if (!exited)
            {
                try { p.Kill(true); } catch { }
                return new CommandResult
                {
                    ExitCode = -1,
                    StdOut = "",
                    StdErr = $"Команда не завершилась за {timeoutMs} мс и была остановлена.",
                    Output = $"Таймаут {timeoutMs} мс.",
                    Shell = shell,
                    TimedOut = true
                };
            }

            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            var outputBuilder = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(stdout)) outputBuilder.AppendLine(stdout.TrimEnd());
            if (!string.IsNullOrWhiteSpace(stderr))
            {
                if (outputBuilder.Length > 0) outputBuilder.AppendLine();
                outputBuilder.AppendLine("stderr:");
                outputBuilder.AppendLine(stderr.TrimEnd());
            }

            return new CommandResult
            {
                ExitCode = p.ExitCode,
                StdOut = Truncate(stdout, 8000),
                StdErr = Truncate(stderr, 8000),
                Output = Truncate(outputBuilder.ToString(), 10000),
                Shell = shell,
                TimedOut = false
            };
        }
        catch (Exception ex)
        {
            return new CommandResult
            {
                ExitCode = -1,
                StdOut = "",
                StdErr = ex.Message,
                Output = "Ошибка запуска команды: " + ex.Message,
                Shell = shell,
                TimedOut = false
            };
        }
    }

    public async Task<string> ExecuteApprovedToolAsync(AgentSession s, PendingTool c)
    {
        var exec = await ExecuteToolAsync(s, c);
        s.Cards.Add(exec.Card);
        s.ToolLog.Add(exec.Log);
        AgentLog($"[TOOL] {exec.Log}");
        if (!string.IsNullOrEmpty(s.Role))
            LogRole(s.Role, $"[TOOL]: {exec.Log}");

        var result = exec.Output;
        if (exec.Mutated && exec.Path != null)
            s.ChangedFiles.Add(DisplayPath(s, exec.Path));

        if (exec.Mutated)
        {
            var checkNote = await RunProjectCheckAsync(s, c.Name);
            if (!string.IsNullOrWhiteSpace(checkNote))
                result += "\n" + checkNote;
        }
        return ToolResultPrompt(c.Name, result);
    }

    public async Task<string> RunProjectCheckAsync(AgentSession s, string trigger)
    {
        if (s.Root == null || s.Mode is "chat" or "plan") return "";
        var cmd = EnsureCheckCommand(s.Root);
        if (string.IsNullOrWhiteSpace(cmd) || IsDangerousCommand(cmd)) return "";

        var res = await RunProcessAsync(cmd, s.Root, 180000);
        s.Cards.Add(new ActionCard
        {
            Type = res.ExitCode == 0 ? "command" : "repair",
            Icon = res.ExitCode == 0 ? "▶️" : "🔧",
            Title = "Проверка проекта",
            Status = res.ExitCode == 0 ? "OK" : $"exit {res.ExitCode}",
            Command = cmd,
            Shell = res.Shell,
            ExitCode = res.ExitCode,
            Details = res.Output
        });
        s.ToolLog.Add($"check \"{cmd}\" → exit {res.ExitCode}");
        AgentLog($"[CHECK] {cmd} → exit {res.ExitCode}");

        if (res.ExitCode == 0)
        {
            s.RepairMode = false;
            s.RepairAttempts = 0;
            return $"Автоматическая проверка проекта '{cmd}' прошла успешно.";
        }

        s.RepairMode = true;
        s.RepairAttempts++;
        var errorText = Tail(string.IsNullOrWhiteSpace(res.StdErr) ? res.StdOut : res.StdErr, 4000);

        if (s.RepairAttempts >= 3)
        {
            return
                $"Автоматическая проверка '{cmd}' упала {s.RepairAttempts} раза.\n" +
                $"Ошибка:\n{errorText}\n" +
                "Дальше автоматический ремонт продолжать нельзя. " +
                "Вызови request_user_input и задай пользователю один диагностический вопрос.";
        }

        return
            $"Автоматическая проверка '{cmd}' упала (попытка {s.RepairAttempts}).\n" +
            $"Ошибка:\n{errorText}\n" +
            "Исправь причину минимально через patch_file и повтори проверку.";
    }
}