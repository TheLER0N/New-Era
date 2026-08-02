// Repl.cs — REPL-цикл, все slash-команды
// New Era CLI v6.0 · partial class MainConsole
// C# 5 / .NET Framework 4.x
using System;

partial class MainConsole
{
    static void Repl()
    {
        while (!StopRequested)
        {
            DrawPrompt();
            string input;
            try { input = Console.ReadLine(); }
            catch { break; }
            if (input == null) break;
            string trimmed = input.Trim();
            if (trimmed.Length == 0) continue;

            string cmd = trimmed;
            if (cmd.StartsWith("/")) cmd = cmd.Substring(1);
            string lower = cmd.ToLowerInvariant();

            // ── Системные ──
            if (lower == "exit" || lower == "quit" || lower == "q") break;
            if (lower == "clear" || lower == "cls") { try { Console.Clear(); } catch { } DrawBanner(); continue; }
            if (lower == "help" || lower == "?") { DrawHelp(); continue; }
            if (lower == "status") { DrawStatus(); continue; }

            // ── История ──
            if (lower == "history" || lower == "hist") { ShowHistory(); continue; }
            if (lower == "history clear") { ClearHistory(); continue; }

            // ── Helper / Live ──
            if (lower == "fetch") { LaunchHelper("--no-pause"); continue; }
            if (lower == "live")  { LaunchHelperLive("--watch --no-pause"); continue; }
            if (lower == "tail")  { LaunchHelperLive("--watch --tail --no-pause"); continue; }
            if (lower == "stop")  { StopLive(); continue; }

            // ── Настройки ──
            if (lower == "think on")  { ShowThinking = true;  WriteColored(ConsoleColor.Green, "  ✔ Ход мыслей: ON\n"); continue; }
            if (lower == "think off") { ShowThinking = false; WriteColored(ConsoleColor.DarkGray, "  ✔ Ход мыслей: OFF\n"); continue; }
            if (lower == "anim on")   { AnimationsEnabled = true;  WriteColored(ConsoleColor.Green, "  ✔ Анимации: ON\n"); continue; }
            if (lower == "anim off")  { AnimationsEnabled = false; WriteColored(ConsoleColor.DarkGray, "  ✔ Анимации: OFF\n"); continue; }

            // ── Orchestrator ──
            if (lower == "orch on")
            {
                OrchestratorEnabled = true;
                WriteColored(ConsoleColor.Green, "  ✔ Оркестратор: ON");
                if (!string.IsNullOrEmpty(OrchestratorModel))
                    WriteColored(ConsoleColor.DarkGray, " (" + OrchestratorModel + ")");
                WriteColored(ConsoleColor.Green, "\n");
                continue;
            }
            if (lower == "orch off")
            {
                OrchestratorEnabled = false;
                WriteColored(ConsoleColor.DarkGray, "  ✔ Оркестратор: OFF\n");
                continue;
            }
            if (lower == "orch status") { DrawOrchestratorStatus(); continue; }

            // ── SYSTEM_GUARDIAN ──
            if (lower == "guardian on")
            {
                GuardianEnabled = true;
                WriteColored(ConsoleColor.Green, "  ✔ Guardian: ON");
                if (!string.IsNullOrEmpty(GuardianModel))
                    WriteColored(ConsoleColor.DarkGray, " (" + GuardianModel + ")");
                WriteColored(ConsoleColor.Green, "\n");
                if (ArcMode)
                    WriteColored(ConsoleColor.Magenta, "    ◆ Аркест: активен (авто-применение)\n");
                continue;
            }
            if (lower == "guardian off")
            {
                GuardianEnabled = false;
                WriteColored(ConsoleColor.DarkGray, "  ✔ Guardian: OFF\n");
                continue;
            }
            if (lower == "guardian status") { DrawGuardianStatus(); continue; }

            // ── Аркест-режим ──
            if (lower == "arc on")
            {
                ArcMode = true;
                WriteColored(ConsoleColor.Magenta, "  ✔ Аркест: ON (авто-применение без подтверждения)\n");
                if (!GuardianEnabled)
                    WriteColored(ConsoleColor.DarkGray, "    ◌ Guardian выключен — включи: /guardian on\n");
                continue;
            }
            if (lower == "arc off")
            {
                ArcMode = false;
                WriteColored(ConsoleColor.DarkGray, "  ✔ Аркест: OFF\n");
                continue;
            }

            // ── Dispatcher v6.0 ──
            if (lower == "dispatcher on")
            {
                DispatcherEnabled = true;
                WriteColored(ConsoleColor.Magenta, "  ✔ Dispatcher: ON\n");
                if (CompressEnabled) WriteColored(ConsoleColor.DarkGray, "    ◌ compress: ON\n");
                if (ExtractEnabled) WriteColored(ConsoleColor.DarkGray, "    ◌ extract: ON\n");
                continue;
            }
            if (lower == "dispatcher off")
            {
                DispatcherEnabled = false;
                WriteColored(ConsoleColor.DarkGray, "  ✔ Dispatcher: OFF\n");
                continue;
            }
            if (lower == "dispatcher status") { DrawDispatcherStatus(); continue; }

            // ── Тест ИИ ──
            if (lower.StartsWith("test ") || lower == "test")
            {
                HandleTest(trimmed.StartsWith("/") ? trimmed.Substring(1) : trimmed);
                continue;
            }

            // ── Файлы / Код ──
            if (lower.StartsWith("scan ") || lower == "scan") { HandleScan(trimmed.StartsWith("/") ? trimmed.Substring(1) : trimmed); continue; }
            if (lower.StartsWith("plan ") || lower == "plan") { HandlePlan(trimmed.StartsWith("/") ? trimmed.Substring(1) : trimmed); continue; }
            if (lower.StartsWith("edit ") || lower == "edit") { HandleEdit(trimmed.StartsWith("/") ? trimmed.Substring(1) : trimmed); continue; }

            // ── Чат ──
            string message = trimmed;
            if (lower.StartsWith("say "))  message = trimmed.Substring(trimmed.StartsWith("/") ? 5 : 4).Trim();
            else if (lower.StartsWith("send ")) message = trimmed.Substring(trimmed.StartsWith("/") ? 6 : 5).Trim();
            if (message.Length == 0) continue;
            Say(message);
        }
        WriteColored(ConsoleColor.DarkGray, "\r  ◂ выход.\n");
    }
}