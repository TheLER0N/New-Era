// Repl.cs — REPL-цикл, все slash-команды
// New Era CLI v6.0 · partial class MainConsole
// C# 5 / .NET Framework 4.x

using System;

partial class MainConsole
{
    static void Repl()
    {
        string NL = Environment.NewLine;

        while (!StopRequested)
        {
            DrawPrompt();

            string input;

            try { input = Console.ReadLine(); }
            catch { break; }

            if (input == null)
                break;

            string trimmed = input.Trim();

            if (trimmed.Length == 0)
                continue;

            string cmd = trimmed;

            if (cmd.StartsWith("/"))
                cmd = cmd.Substring(1);

            string lower = cmd.ToLowerInvariant();

            // ── Системные ──
            if (lower == "exit" || lower == "quit" || lower == "q")
                break;

            if (lower == "clear" || lower == "cls")
            {
                try { Console.Clear(); } catch { }

                DrawBanner();

                continue;
            }

            if (lower == "help" || lower == "?")
            {
                DrawHelp();

                continue;
            }

            if (lower == "status")
            {
                DrawStatus();

                continue;
            }

            // ── История ──
            if (lower == "history" || lower == "hist")
            {
                ShowHistory();

                continue;
            }

            if (lower == "history clear")
            {
                ClearHistory();

                continue;
            }

            // ── Helper / Live ──
            if (lower == "fetch")
            {
                LaunchHelper("--no-pause");

                continue;
            }

            if (lower == "live")
            {
                LaunchHelperLive("--watch --no-pause");

                continue;
            }

            if (lower == "tail")
            {
                LaunchHelperLive("--watch --tail --no-pause");

                continue;
            }

            if (lower == "stop")
            {
                StopLive();

                continue;
            }

            // ── Настройки ──
            if (lower == "think on")
            {
                ShowThinking = true;

                WriteColored(ConsoleColor.Green,
                    "  ✔ Ход мыслей: ON" + NL);

                continue;
            }

            if (lower == "think off")
            {
                ShowThinking = false;

                WriteColored(ConsoleColor.DarkGray,
                    "  ✔ Ход мыслей: OFF" + NL);

                continue;
            }

            if (lower == "anim on")
            {
                AnimationsEnabled = true;

                WriteColored(ConsoleColor.Green,
                    "  ✔ Анимации: ON" + NL);

                continue;
            }

            if (lower == "anim off")
            {
                AnimationsEnabled = false;

                WriteColored(ConsoleColor.DarkGray,
                    "  ✔ Анимации: OFF" + NL);

                continue;
            }

            // ── Dispatcher v6.0 ──
            if (lower == "dispatcher on")
            {
                DispatcherEnabled = true;

                WriteColored(ConsoleColor.Magenta,
                    "  ✔ Dispatcher: ON" + NL);

                if (CompressEnabled)
                {
                    WriteColored(ConsoleColor.DarkGray,
                        "    ◌ compress: ON" + NL);
                }

                if (ExtractEnabled)
                {
                    WriteColored(ConsoleColor.DarkGray,
                        "    ◌ extract: ON" + NL);
                }

                continue;
            }

            if (lower == "dispatcher off")
            {
                DispatcherEnabled = false;

                WriteColored(ConsoleColor.DarkGray,
                    "  ✔ Dispatcher: OFF" + NL);

                continue;
            }

            if (lower == "dispatcher status")
            {
                DrawDispatcherStatus();

                continue;
            }

            // ── Compress / Extract runtime toggles ──
            if (lower == "compress on")
            {
                CompressEnabled = true;

                WriteColored(ConsoleColor.Magenta,
                    "  ✔ Compress: ON" + NL);

                continue;
            }

            if (lower == "compress off")
            {
                CompressEnabled = false;

                WriteColored(ConsoleColor.DarkGray,
                    "  ✔ Compress: OFF" + NL);

                continue;
            }

            if (lower == "extract on")
            {
                ExtractEnabled = true;

                WriteColored(ConsoleColor.Magenta,
                    "  ✔ Extract: ON" + NL);

                continue;
            }

            if (lower == "extract off")
            {
                ExtractEnabled = false;

                WriteColored(ConsoleColor.DarkGray,
                    "  ✔ Extract: OFF" + NL);

                continue;
            }

            // ── Аркест-режим ──
            if (lower == "arc on")
            {
                ArcMode = true;

                WriteColored(ConsoleColor.Magenta,
                    "  ✔ Аркест: ON (авто-применение без подтверждения)" + NL);

                continue;
            }

            if (lower == "arc off")
            {
                ArcMode = false;

                WriteColored(ConsoleColor.DarkGray,
                    "  ✔ Аркест: OFF" + NL);

                continue;
            }

            // ── Тест ИИ ──
            if (lower.StartsWith("test ") || lower == "test")
            {
                HandleTest(trimmed.StartsWith("/") ? trimmed.Substring(1) : trimmed);

                continue;
            }

            // ── Файлы / Код ──
            if (lower.StartsWith("scan ") || lower == "scan")
            {
                HandleScan(trimmed.StartsWith("/") ? trimmed.Substring(1) : trimmed);

                continue;
            }

            if (lower.StartsWith("plan ") || lower == "plan")
            {
                HandlePlan(trimmed.StartsWith("/") ? trimmed.Substring(1) : trimmed);

                continue;
            }

            if (lower.StartsWith("edit ") || lower == "edit")
            {
                HandleEdit(trimmed.StartsWith("/") ? trimmed.Substring(1) : trimmed);

                continue;
            }

            // ── Чат ──
            string message = trimmed;

            if (lower.StartsWith("say "))
            {
                message = trimmed.Substring(trimmed.StartsWith("/") ? 5 : 4).Trim();
            }
            else if (lower.StartsWith("send "))
            {
                message = trimmed.Substring(trimmed.StartsWith("/") ? 6 : 5).Trim();
            }

            if (message.Length == 0)
                continue;

            Say(message);
        }

        WriteColored(ConsoleColor.DarkGray,
            "\r  ◂ выход." + Environment.NewLine);
    }
}