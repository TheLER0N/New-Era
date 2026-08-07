// Diagnostics.cs — /doctor для Build Gate
// New Era v7.2+ · C# 5 / .NET Framework 4.x
using System;
using System.IO;
using System.Text;

partial class MainConsole
{
    static class Diagnostics
    {
        public static void HandleDoctor()
        {
            HandleDoctorCommand("doctor");
        }

        public static void HandleDoctorCommand(string input)
        {
            ParsedArgs args = BuildGate.ParseArgs(input, "doctor");
            ProjectContext ctx = BuildGate.CreateContext(args.Path);
            BuildGate.LastContext = ctx;

            if (args.HasOption("help"))
            {
                WriteColored(ConsoleColor.Cyan, "  Использование: /doctor [path] [--report file] [--fix-safe]\n");
                return;
            }

            WriteColored(ConsoleColor.Cyan, "\n  [i] Build Gate: doctor\n");
            WriteColored(ConsoleColor.DarkGray, "  ROOT: " + ctx.RootPath + "\n");

            var sb = new StringBuilder();

            sb.AppendLine("NEW ERA BUILD DOCTOR");
            sb.AppendLine("TIME: " + DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss"));
            sb.AppendLine("ROOT: " + ctx.RootPath);
            sb.AppendLine("PROJECT_TYPE: " + ctx.ProjectType);
            sb.AppendLine("TOOLCHAIN: " + BuildGate.DetectToolchain(ctx));
            sb.AppendLine();

            if (ctx.ManifestExists)
            {
                sb.AppendLine("[OK] build.json найден");

                if (!string.IsNullOrEmpty(ctx.ManifestError))
                    sb.AppendLine("[XX] Ошибка build.json: " + ctx.ManifestError);
                else
                    sb.AppendLine("[OK] build.json читается");
            }
            else
            {
                sb.AppendLine("[i] build.json отсутствует — используется auto-detect");
            }

            if (ctx.ProjectType == "csharp-framework")
            {
                string csc = BuildGate.FindCsc();

                if (string.IsNullOrEmpty(csc))
                    sb.AppendLine("[XX] csc.exe не найден");
                else
                    sb.AppendLine("[OK] csc.exe: " + csc);
            }

            if (ctx.ProjectType == "csharp-dotnet")
                sb.AppendLine("[i] Требуется dotnet SDK. Проверка запускается только при реальном build.");

            if (ctx.ProjectType == "node")
            {
                if (!File.Exists(Path.Combine(ctx.RootPath, "package.json")))
                    sb.AppendLine("[XX] package.json не найден");
                else
                    sb.AppendLine("[OK] package.json найден");
            }

            if (ctx.ProjectType == "python")
            {
                if (!File.Exists(Path.Combine(ctx.RootPath, "requirements.txt")))
                    sb.AppendLine("[i] requirements.txt не найден");
                else
                    sb.AppendLine("[OK] requirements.txt найден");
            }

            try
            {
                string tmp = Path.Combine(ctx.RootPath, "__doctor_test.tmp");
                File.WriteAllText(tmp, "test", new UTF8Encoding(false));
                File.Delete(tmp);

                sb.AppendLine("[OK] Запись в root разрешена");
            }
            catch (Exception ex)
            {
                sb.AppendLine("[XX] Запись в root запрещена: " + ex.Message);
            }

            try
            {
                string reportDir = BuildGate.GetReportDir(ctx);
                sb.AppendLine("[OK] Report dir доступен: " + reportDir);
            }
            catch (Exception ex)
            {
                sb.AppendLine("[XX] Report dir недоступен: " + ex.Message);
            }

            sb.AppendLine();
            sb.AppendLine("LAST ERROR CHAIN:");

            if (BuildGate.LastError != null)
            {
                sb.AppendLine("symptom: " + BuildGate.LastError.Type);
                sb.AppendLine("probable_cause: " + BuildGate.LastError.Cause);
                sb.AppendLine("evidence: " + (BuildGate.LastReportPath ?? "нет последнего отчёта"));
                sb.AppendLine("recommended_action: " + BuildGate.LastError.NextStep);
                sb.AppendLine("can_auto_repair: " + (BuildGate.LastError.CanAutoRepair ? "YES" : "NO"));
            }
            else
            {
                sb.AppendLine("Нет информации о последней ошибке в этой сессии.");
                sb.AppendLine("Если ошибка была раньше, выполни /build или /verify, затем снова /doctor.");
            }

            if (args.HasOption("fix-safe"))
            {
                sb.AppendLine();
                sb.AppendLine("SAFE FIXES:");

                try
                {
                    Directory.CreateDirectory(BuildGate.GetReportDir(ctx));
                    sb.AppendLine("[OK] Создана/проверена папка отчётов");
                }
                catch
                {
                    sb.AppendLine("[XX] Не удалось создать папку отчётов");
                }

                try
                {
                    Directory.CreateDirectory(Path.Combine(ctx.RootPath, ".newera", "rollback"));
                    sb.AppendLine("[OK] Создана/проверена папка rollback");
                }
                catch
                {
                    sb.AppendLine("[XX] Не удалось создать папку rollback");
                }
            }

            string status = "OK";

            if (sb.ToString().IndexOf("[XX]", StringComparison.OrdinalIgnoreCase) >= 0)
                status = "ISSUES_FOUND";

            string reportPath = BuildGate.CreateTextReport(ctx, "doctor", status, sb.ToString(), args.GetOption("report", null));

            WriteColored(ConsoleColor.Gray, "\n" + sb.ToString() + "\n");

            if (!string.IsNullOrEmpty(reportPath))
                WriteColored(ConsoleColor.Green, "  [OK] Отчёт: " + reportPath + "\n");
        }
    }
}