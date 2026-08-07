// RepairLoop.cs — автоматический ремонт сборки через AI
// New Era v7.2+ · C# 5 / .NET Framework 4.x
using System;
using System.Text;

partial class MainConsole
{
    static class RepairLoop
    {
        public static void HandleRepair()
        {
            HandleRepairCommand("repair");
        }

        public static void HandleRepairCommand(string input)
        {
            ParsedArgs args = BuildGate.ParseArgs(input, "repair");
            ProjectContext ctx = BuildGate.CreateContext(args.Path);
            BuildGate.LastContext = ctx;

            if (args.HasOption("help"))
            {
                WriteColored(ConsoleColor.Cyan, "  Использование: /repair [path] [--max N] [--auto] [--ask]\n");
                return;
            }

            WriteColored(ConsoleColor.Cyan, "\n  [i] Build Gate: repair\n");
            WriteColored(ConsoleColor.DarkGray, "  ROOT: " + ctx.RootPath + "\n");

            int max = ParseInt(args.GetOption("max", ""), ctx.RepairMaxRounds);
            if (max <= 0) max = BuildGate.MaxRepairRounds;

            bool ask = args.HasOption("ask");
            bool auto = args.HasOption("auto") || (ctx.RepairAutoApply && !ask);

            if (!auto && !ask)
                ask = true;

            RunWithContext(ctx, max, auto, ask);
        }

        public static bool Run(string projectRoot, bool force)
        {
            ProjectContext ctx = BuildGate.CreateContext(projectRoot);
            return RunWithContext(ctx, BuildGate.MaxRepairRounds, force, false);
        }

        public static bool RunWithContext(ProjectContext ctx, int maxRounds, bool autoApply, bool ask)
        {
            if (ctx == null) return false;
            if (maxRounds <= 0) maxRounds = BuildGate.MaxRepairRounds;

            BuildGate.BeginChangedFiles();
            BuildGate.AppendReliabilitySafe("repair", "start root=" + ctx.RootPath + " max=" + maxRounds);

            for (int round = 1; round <= maxRounds; round++)
            {
                if (StopRequested) return false;

                WriteColored(ConsoleColor.Cyan, "\n  [i] Ремонт: раунд " + round + "/" + maxRounds + "\n");

                try
                {
                    string rb = BuildGate.CreateRollbackPoint(ctx.RootPath, "repair");
                    if (!string.IsNullOrEmpty(rb))
                        WriteColored(ConsoleColor.Green, "  [OK] Точка отката: " + rb + "\n");
                }
                catch { }

                BuildResult result = BuildGate.RunFullPipeline(ctx, false, false, null);

                if (result.Success)
                {
                    WriteColored(ConsoleColor.Green, "  [OK] Сборка успешна после ремонта/проверки\n");
                    BuildGate.AppendReliabilitySafe("repair", "success round=" + round);
                    return true;
                }

                BuildGate.ShowResult(result);

                if (result.Error != null && !result.Error.CanAutoRepair)
                {
                    WriteColored(ConsoleColor.Yellow, "  [!!] Ошибка не подлежит авто-ремонту\n");
                    BuildGate.AppendReliabilitySafe("repair", "not repairable: " + result.Error.Type);
                    return false;
                }

                if (!autoApply && !ask)
                {
                    WriteColored(ConsoleColor.Yellow, "  [!!] Авто-ремонт выключен\n");
                    return false;
                }

                if (round >= maxRounds)
                    break;

                if (ask && !AskYesNo("  Применить автоматический ремонт? [y/N] "))
                {
                    WriteColored(ConsoleColor.Yellow, "  [!!] Ремонт отклонён пользователем\n");
                    BuildGate.AppendReliabilitySafe("repair", "rejected by user");
                    return false;
                }

                string response = RequestFix(ctx, result, round);

                if (string.IsNullOrWhiteSpace(response))
                {
                    WriteColored(ConsoleColor.Yellow, "  [!!] Пустой ответ от AI — ремонт остановлен\n");
                    break;
                }

                AddHistory("assistant", response);

                CodeWriterResult code = ExtractCodeOrLocal(response);
                if (code == null || code.IsEmpty)
                {
                    WriteColored(ConsoleColor.Yellow, "  [!!] AI не вернул файловые правки\n");
                    break;
                }

                CodeWriterResult filtered = FilterAllowedOperations(ctx, code);
                if (filtered == null || filtered.IsEmpty)
                {
                    WriteColored(ConsoleColor.Yellow, "  [!!] Все предложенные правки вне allowedPaths/forbiddenPaths\n");
                    BuildGate.AppendReliabilitySafe("repair", "path restricted");
                    break;
                }

                BuildGate.AddChangedFiles(filtered);

                bool applied = false;
                bool previousSuppress = BuildGate.SuppressPostApply;

                BuildGate.SuppressPostApply = true;

                try
                {
                    applied = ApplyValidatedFiles(filtered, ctx.RootPath, true);
                }
                catch (Exception ex)
                {
                    WriteColored(ConsoleColor.Red, "  [XX] Ошибка применения правок: " + ex.Message + "\n");
                }
                finally
                {
                    BuildGate.SuppressPostApply = previousSuppress;
                }

                if (!applied)
                {
                    WriteColored(ConsoleColor.Red, "  [XX] Правки не применены\n");
                    break;
                }

                WriteColored(ConsoleColor.Green, "  [OK] Правки применены, повторяю сборку\n");
            }

            WriteColored(ConsoleColor.Red, "\n  [XX] Ремонт не завершён успешно. Можно сделать /undo.\n");
            BuildGate.AppendReliabilitySafe("repair", "failed");

            return false;
        }

        static CodeWriterResult FilterAllowedOperations(ProjectContext ctx, CodeWriterResult code)
        {
            var result = new CodeWriterResult { RawText = code.RawText };

            foreach (var op in code.Operations)
            {
                string fullPath;

                if (BuildGate.IsPathAllowed(ctx, op.Path, out fullPath))
                    result.Operations.Add(op);
            }

            return result;
        }

        static string RequestFix(ProjectContext ctx, BuildResult result, int round)
        {
            string log = result.Output ?? "";

            if (log.Length > 9000)
                log = log.Substring(log.Length - 9000);

            var sb = new StringBuilder();

            sb.Append("Ты — ремонтный агент сборки. Исправь код проекта так, чтобы сборка и smoke-run прошли.\n");
            sb.Append("Проект: " + ctx.RootPath + "\n");
            sb.Append("Тип проекта: " + ctx.ProjectType + "\n");
            sb.Append("Раунд: " + round + "\n\n");

            sb.Append("Ошибка сборки:\n");
            sb.Append(log);
            sb.Append("\n\n");

            sb.Append("Верни ТОЛЬКО блоки в формате:\n");
            sb.Append("FILE: relative/path\n");
            sb.Append("ACTION: CREATE|MODIFY|DELETE\n");
            sb.Append("CONTENT:\n");
            sb.Append("...\n");
            sb.Append("END_FILE\n");
            sb.Append("\nЕсли код не нужен, верни: NO_CODE\n");

            PauseBeforePrimary("repair");
            StartSpinner("repair");

            string response = null;

            try
            {
                response = PostMessageWithRetry(sb.ToString(), LastResponseId);
            }
            catch (Exception ex)
            {
                WriteColored(ConsoleColor.Red, "  [XX] AI ошибка: " + ex.Message + "\n");
            }

            StopSpinner();

            return response;
        }

        static bool AskYesNo(string prompt)
        {
            WriteColored(ConsoleColor.Yellow, prompt);
            string s = Console.ReadLine();

            return s != null && s.Trim().ToLowerInvariant() == "y";
        }
    }
}