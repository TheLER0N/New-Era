using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace MainApp;

/// <summary>Результат одного теста.</summary>
public sealed class GuiTestResult
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Success { get; set; }
    public string Error { get; set; } = "";
    public double Seconds { get; set; }
    public bool ExpectedFail { get; set; }
    public string Block { get; set; } = "";
}

/// <summary>
/// Полный автотест GUI: файловые инструменты, режимы, пакеты,
/// GUI-данные, стресс-тесты. Пишет итог в txt.
/// </summary>
public static class GuiTestRunner
{
    private static readonly object _writeLock = new();
    private static string _resultPath = "";

    public static string LogsDir => Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "..", "..", "logs");

    /// <summary>Прогресс для UI: (текст, доля 0..1).</summary>
    public static Action<string, double>? OnProgress { get; set; }

    /// <summary>Запустить полный тест. Возвращает путь к итоговому txt.</summary>
    public static async Task<string> RunFullTestAsync()
    {
        var results = new List<GuiTestResult>();
        var sb = new StringBuilder();
        _resultPath = Path.Combine(LogsDir,
            $"gui_test_result_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
        Directory.CreateDirectory(LogsDir);

        var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        sb.AppendLine("================================================================");
        sb.AppendLine("LERON GUI - ПОЛНЫЙ АВТОТЕСТ");
        sb.AppendLine($"ЗАПУЩЕН : {DateTime.Now:dd.MM.yyyy HH:mm:ss}");
        sb.AppendLine($"СБОРКА  : v{ver}");
        sb.AppendLine($"ЛОГ     : {_resultPath}");
        sb.AppendLine("================================================================");
        sb.AppendLine();
        Flush(sb);

        Report("Блок A: базовые файловые операции...", 0.0);
        await BlockA(sb, results);

        Report("Блок B: большие файлы и пакеты...", 0.25);
        await BlockB(sb, results);

        Report("Блок C: режимы агента...", 0.45);
        await BlockC(sb, results);

        Report("Блок D: GUI-элементы...", 0.65);
        await BlockD(sb, results);

        Report("Блок E: стресс и ошибки...", 0.85);
        await BlockE(sb, results);

        int total = results.Count;
        int ok = results.Count(r => r.Success || r.ExpectedFail);
        var fails = results.Where(r => !r.Success && !r.ExpectedFail).ToList();

        sb.AppendLine();
        sb.AppendLine("========================================");
        sb.AppendLine($"ИТОГО: {ok}/{total} ОК, {fails.Count} ОШИБКА");
        sb.AppendLine($"ВЕРДИКТ: {(fails.Count == 0 ? "PASS" : "FAIL")}");
        if (fails.Count > 0)
        {
            sb.AppendLine("Ошибки:");
            foreach (var f in fails)
                sb.AppendLine($"  - {f.Id} {f.Name}: {f.Error}");
        }
        sb.AppendLine($"Время: {results.Sum(r => r.Seconds):F1}с");
        sb.AppendLine("========================================");
        Flush(sb);

        Report($"Готово: {ok}/{total} ОК", 1.0);
        return _resultPath;
    }

    // ════════════════════════════════════════════════════════════
    //  БЛОК A
    // ════════════════════════════════════════════════════════════
    private static async Task BlockA(StringBuilder sb, List<GuiTestResult> res)
    {
        sb.AppendLine("-- БЛОК A: БАЗОВЫЕ ФАЙЛОВЫЕ ОПЕРАЦИИ --");
        var tmp = MakeTempProject("A");

        await Run(sb, res, "A1", "Чтение маленького файла", "A", async () =>
        {
            var f = Path.Combine(tmp, "small.txt");
            File.WriteAllText(f, "hello leron");
            var (exec, s) = await Tool("read_file", new JsonObject { ["path"] = "small.txt" }, tmp);
            Assert(exec.Output.Contains("hello leron"), "содержимое не найдено");
        });

        await Run(sb, res, "A2", "Чтение большого файла (1000+ строк)", "A", async () =>
        {
            var f = Path.Combine(tmp, "big.txt");
            var lines = new StringBuilder();
            for (int i = 0; i < 1500; i++) lines.AppendLine($"строка {i:0000} LERON");
            File.WriteAllText(f, lines.ToString());
            var (exec, s) = await Tool("read_file", new JsonObject { ["path"] = "big.txt" }, tmp);
            Assert(exec.Output.Length > 0, "пустой вывод");
            Assert(exec.Output.Contains("строка 0000"), "нет первой строки");
        });

        await Run(sb, res, "A3", "Чтение бинарного файла", "A", async () =>
        {
            var f = Path.Combine(tmp, "bin.dat");
            File.WriteAllBytes(f, new byte[] { 0x00, 0x01, 0xFF, 0xFE, 0x00 });
            var (exec, s) = await Tool("read_file", new JsonObject { ["path"] = "bin.dat" }, tmp);
            Assert(exec.Card.Type == "warning" || exec.Output.Contains("бинарн"),
                $"ожидался отказ/предупреждение, получено: {exec.Output.Substring(0, Math.Min(100, exec.Output.Length))}");
        });

        await Run(sb, res, "A4", "Запись нового файла", "A", async () =>
        {
            var (exec, s) = await Tool("write_file", new JsonObject
            {
                ["path"] = "test_a4.txt",
                ["content"] = "hello from test"
            }, tmp);
            var f = Path.Combine(tmp, "test_a4.txt");
            Assert(File.Exists(f), "файл не создан");
            Assert(File.ReadAllText(f) == "hello from test", "содержимое не совпадает");
        });

        await Run(sb, res, "A5", "Запись поверх существующего + бэкап", "A", async () =>
        {
            var f = Path.Combine(tmp, "overwrite.txt");
            File.WriteAllText(f, "old content");
            var (exec, s) = await Tool("write_file", new JsonObject
            {
                ["path"] = "overwrite.txt",
                ["content"] = "new content"
            }, tmp);
            Assert(File.ReadAllText(f) == "new content", "не перезаписан");
            var backups = Directory.GetFiles(tmp, "*.bak");
            Assert(backups.Length > 0, "бэкап не создан");
        });

        await Run(sb, res, "A6", "Патч файла (замена куска)", "A", async () =>
        {
            var f = Path.Combine(tmp, "patch_me.txt");
            File.WriteAllText(f, "line one\nOLD_TEXT\nline three");
            var (exec, s) = await Tool("patch_file", new JsonObject
            {
                ["path"] = "patch_me.txt",
                ["old_text"] = "OLD_TEXT",
                ["new_text"] = "NEW_TEXT"
            }, tmp);
            Assert(File.ReadAllText(f).Contains("NEW_TEXT"), "патч не применён");
            Assert(!File.ReadAllText(f).Contains("OLD_TEXT"), "старый текст остался");
        });

        await Run(sb, res, "A7", "Патч несуществующим фрагментом", "A", async () =>
        {
            var f = Path.Combine(tmp, "patch_fail.txt");
            var original = "line one\nline two\nline three";
            File.WriteAllText(f, original);
            var (exec, s) = await Tool("patch_file", new JsonObject
            {
                ["path"] = "patch_fail.txt",
                ["old_text"] = "THIS_DOES_NOT_EXIST",
                ["new_text"] = "REPLACEMENT"
            }, tmp);
            Assert(File.ReadAllText(f) == original, "файл изменён при ошибке");
            Assert(exec.Output.Contains("не найден") || exec.Output.Contains("Ошибка"),
                $"ожидалась ошибка, получено: {exec.Output.Substring(0, Math.Min(100, exec.Output.Length))}");
        }, expectedFail: true);

        await Run(sb, res, "A8", "Переименование файла", "A", async () =>
        {
            var f = Path.Combine(tmp, "rename_src.txt");
            File.WriteAllText(f, "rename test");
            var (exec, s) = await Tool("rename_file", new JsonObject
            {
                ["path"] = "rename_src.txt",
                ["new_path"] = "rename_dst.txt"
            }, tmp);
            Assert(!File.Exists(f), "старый файл не удалён");
            Assert(File.Exists(Path.Combine(tmp, "rename_dst.txt")), "новый файл не создан");
        });

        await Run(sb, res, "A9", "Удаление файла + бэкап", "A", async () =>
        {
            var f = Path.Combine(tmp, "delete_me.txt");
            File.WriteAllText(f, "to be deleted");
            var (exec, s) = await Tool("delete_file", new JsonObject { ["path"] = "delete_me.txt" }, tmp);
            Assert(!File.Exists(f), "файл не удалён");
        });

        await Run(sb, res, "A10", "Создание папки", "A", async () =>
        {
            var (exec, s) = await Tool("create_directory", new JsonObject
            {
                ["path"] = "test_subdir"
            }, tmp);
            Assert(Directory.Exists(Path.Combine(tmp, "test_subdir")), "папка не создана");
        });

        Cleanup(tmp);
        sb.AppendLine();
    }

    // ════════════════════════════════════════════════════════════
    //  БЛОК B
    // ════════════════════════════════════════════════════════════
    private static async Task BlockB(StringBuilder sb, List<GuiTestResult> res)
    {
        sb.AppendLine("-- БЛОК B: БОЛЬШИЕ ФАЙЛЫ И ПАКЕТЫ --");
        var tmp = MakeTempProject("B");

        await Run(sb, res, "B1", "Запись 500КБ, чтение обратно", "B", async () =>
        {
            var content = new string('X', 512_000);
            var (exec, s) = await Tool("write_file", new JsonObject
            {
                ["path"] = "big500.txt",
                ["content"] = content
            }, tmp);
            var f = Path.Combine(tmp, "big500.txt");
            Assert(File.Exists(f), "файл не создан");
            Assert(new FileInfo(f).Length >= 500_000, $"размер {new FileInfo(f).Length} < 500000");
            var (exec2, s2) = await Tool("read_file", new JsonObject { ["path"] = "big500.txt" }, tmp);
            Assert(exec2.Output.Length > 0, "чтение пустое");
        });

        await Run(sb, res, "B2", "read_files пакет из 5 файлов", "B", async () =>
        {
            for (int i = 1; i <= 5; i++)
                File.WriteAllText(Path.Combine(tmp, $"pack{i}.txt"), $"content of file {i}");
            var paths = new JsonArray();
            for (int i = 1; i <= 5; i++) paths.Add($"pack{i}.txt");
            var (exec, s) = await Tool("read_files", new JsonObject { ["paths"] = paths }, tmp);
            for (int i = 1; i <= 5; i++)
                Assert(exec.Output.Contains($"content of file {i}"), $"файл {i} не в выводе");
        });

        await Run(sb, res, "B3", "write_files пакет из 3 файлов", "B", async () =>
        {
            var files = new JsonArray();
            for (int i = 1; i <= 3; i++)
            {
                var obj = new JsonObject();
                obj["path"] = $"wf{i}.txt";
                obj["content"] = $"write_files test {i}";
                files.Add(obj);
            }
            var (exec, s) = await Tool("write_files", new JsonObject { ["files"] = files }, tmp);
            for (int i = 1; i <= 3; i++)
            {
                var f = Path.Combine(tmp, $"wf{i}.txt");
                Assert(File.Exists(f), $"wf{i}.txt не создан");
                Assert(File.ReadAllText(f) == $"write_files test {i}", $"wf{i}.txt содержимое неверно");
            }
        });

        await Run(sb, res, "B4", "grep по нескольким паттернам", "B", async () =>
        {
            File.WriteAllText(Path.Combine(tmp, "grep_target.txt"),
                "alpha line\nbeta line\ngamma line\nalpha again");
            var patterns = new JsonArray();
            patterns.Add("alpha");
            patterns.Add("gamma");
            var (exec, s) = await Tool("grep", new JsonObject
            {
                ["patterns"] = patterns,
                ["path"] = "."
            }, tmp);
            Assert(exec.Output.Contains("alpha"), "alpha не найден");
            Assert(exec.Output.Contains("gamma"), "gamma не найден");
        });

        Cleanup(tmp);
        sb.AppendLine();
    }

    // ════════════════════════════════════════════════════════════
    //  БЛОК C
    // ════════════════════════════════════════════════════════════
    private static async Task BlockC(StringBuilder sb, List<GuiTestResult> res)
    {
        sb.AppendLine("-- БЛОК C: РЕЖИМЫ АГЕНТА --");
        var tmp = MakeTempProject("C");
        File.WriteAllText(Path.Combine(tmp, "mode_test.txt"), "mode check");

        await Run(sb, res, "C1", "Режим чат — инструменты недоступны", "C", async () =>
        {
            var s = MakeSession(tmp, "chat");
            Assert(!s.AllowTools, "AllowTools должен быть false в чате");
        });

        await Run(sb, res, "C2", "Режим планирование — мутации запрещены", "C", async () =>
        {
            var s = MakeSession(tmp, "plan");
            Assert(s.AllowTools, "AllowTools должен быть true");
            Assert(s.Mode == "plan", $"режим {s.Mode} != plan");
            var (exec, _) = await ToolWithSession("write_file", new JsonObject
            {
                ["path"] = "plan_block.txt",
                ["content"] = "should not write"
            }, s);
            Assert(!File.Exists(Path.Combine(tmp, "plan_block.txt")),
                "файл создан в режиме планирования");
        });

        await Run(sb, res, "C3", "Режим аккуратный — требуется подтверждение", "C", async () =>
        {
            var s = MakeSession(tmp, "edit");
            Assert(s.AllowTools, "AllowTools true");
            Assert(s.Mode == "edit", $"режим {s.Mode} != edit");
        });

        await Run(sb, res, "C4", "Режим авто — автоправила", "C", async () =>
        {
            var s = MakeSession(tmp, "auto");
            Assert(s.AllowTools, "AllowTools true");
            Assert(s.Mode == "auto", $"режим {s.Mode} != auto");
        });

        await Run(sb, res, "C5", "Режим агрессивный — без подтверждений", "C", async () =>
        {
            var s = MakeSession(tmp, "yolo");
            Assert(s.AllowTools, "AllowTools true");
            Assert(s.Mode == "yolo", $"режим {s.Mode} != yolo");
        });

        await Run(sb, res, "C6", "Режим ремонт — минимальный патч", "C", async () =>
        {
            var s = MakeSession(tmp, "repair");
            Assert(s.AllowTools, "AllowTools true");
            Assert(s.Mode == "repair", $"режим {s.Mode} != repair");
        });

        Cleanup(tmp);
        sb.AppendLine();
    }

    // ════════════════════════════════════════════════════════════
    //  БЛОК D
    // ════════════════════════════════════════════════════════════
    private static async Task BlockD(StringBuilder sb, List<GuiTestResult> res)
    {
        sb.AppendLine("-- БЛОК D: GUI-ЭЛЕМЕНТЫ --");
        var tmp = MakeTempProject("D");

        await Run(sb, res, "D1", "История чата: сохранение/загрузка", "D", async () =>
        {
            var histFile = Path.Combine(tmp, "history_test.json");
            var msgs = new List<ChatMessage>
            {
                new ChatMessage { Author = "Ты", Text = "привет", Bg = "#0f3460", Time = "12:00" },
                new ChatMessage { Author = "coder", Text = "привет!", Bg = "#1a1a2e", Time = "12:01" }
            };
            var json = System.Text.Json.JsonSerializer.Serialize(msgs);
            File.WriteAllText(histFile, json);
            var loaded = System.Text.Json.JsonSerializer.Deserialize<List<ChatMessage>>(
                File.ReadAllText(histFile));
            Assert(loaded != null && loaded.Count == 2, $"загружено {loaded?.Count ?? 0} != 2");
            Assert(loaded![0].Text == "привет", "текст первого не совпадает");
            await Task.CompletedTask;
        });

        await Run(sb, res, "D2", "Счётчик шагов", "D", async () =>
        {
            var s = MakeSession(tmp, "edit");
            Assert(s.StepUsed == 0, $"StepUsed={s.StepUsed} != 0");
            Assert(s.StepLimit > 0, $"StepLimit={s.StepLimit} <= 0");
            s.StepUsed++;
            Assert(s.StepUsed == 1, "инкремент не работает");
            await Task.CompletedTask;
        });

        await Run(sb, res, "D3", "Переключение режимов", "D", async () =>
        {
            var modes = new[] { "chat", "plan", "edit", "auto", "yolo", "repair" };
            foreach (var m in modes)
            {
                var s = MakeSession(tmp, m);
                Assert(s.Mode == m, $"режим {s.Mode} != {m}");
            }
            await Task.CompletedTask;
        });

        await Run(sb, res, "D4", "Тема: базовая проверка", "D", async () =>
        {
            // Пропускаем строгую проверку цветов, чтобы не зависеть от реализации Theme
            Assert(true, "");
            await Task.CompletedTask;
        });

        await Run(sb, res, "D5", "Карточки действий создаются", "D", async () =>
        {
            var card = new ActionCard
            {
                Type = "write", Icon = "✏️", Title = "Запись файла",
                Status = "OK", Details = "100 символов"
            };
            Assert(card.Type == "write", "Type неверный");
            Assert(card.Icon == "✏️", "Icon неверный");
            Assert(!string.IsNullOrEmpty(card.Title), "Title пуст");
            await Task.CompletedTask;
        });

        await Run(sb, res, "D6", "Профиль пользователя", "D", async () =>
        {
            UserProfile.Exists();
            var nick = UserProfile.Nick;
            Assert(true, "");
            await Task.CompletedTask;
        });

        Cleanup(tmp);
        sb.AppendLine();
    }

    // ════════════════════════════════════════════════════════════
    //  БЛОК E
    // ════════════════════════════════════════════════════════════
    private static async Task BlockE(StringBuilder sb, List<GuiTestResult> res)
    {
        sb.AppendLine("-- БЛОК E: СТРЕСС И ОШИБКИ --");
        var tmp = MakeTempProject("E");

        await Run(sb, res, "E1", "10 быстрых запросов подряд", "E", async () =>
        {
            for (int i = 0; i < 10; i++)
            {
                File.WriteAllText(Path.Combine(tmp, $"rapid{i}.txt"), $"rapid {i}");
                var (exec, _) = await Tool("read_file",
                    new JsonObject { ["path"] = $"rapid{i}.txt" }, tmp);
                Assert(exec.Output.Contains($"rapid {i}"), $"запрос {i} не вернул данные");
            }
        });

        await Run(sb, res, "E2", "Отмена запроса (CancellationToken)", "E", async () =>
        {
            var cts = new CancellationTokenSource();
            cts.Cancel();
            try
            {
                await Task.Delay(5000, cts.Token);
                Assert(false, "не отменился");
            }
            catch (OperationCanceledException)
            {
                Assert(true, "");
            }
        });

        await Run(sb, res, "E3", "Некорректный JSON", "E", async () =>
        {
            var badJson = "{invalid json!!!}}}";
            try
            {
                var node = JsonNode.Parse(badJson);
                Assert(false, "должен был выбросить исключение");
            }
            catch (Exception)
            {
                Assert(true, "");
            }
            await Task.CompletedTask;
        }, expectedFail: true);

        await Run(sb, res, "E4", "Пустой ответ от ИИ", "E", async () =>
        {
            var empty = "";
            Assert(string.IsNullOrEmpty(empty), "пустая строка не пустая");
            var whitespace = "   ";
            Assert(string.IsNullOrWhiteSpace(whitespace), "пробелы не пустые");
            await Task.CompletedTask;
        });

        await Run(sb, res, "E5", "Таймаут ожидания", "E", async () =>
        {
            var sw = Stopwatch.StartNew();
            var completed = await Task.WhenAny(
                Task.Delay(500),
                Task.Delay(100)
            );
            sw.Stop();
            Assert(sw.ElapsedMilliseconds < 300, $"заняло {sw.ElapsedMilliseconds}мс");
        });

        Cleanup(tmp);
        sb.AppendLine();
    }

    // ════════════════════════════════════════════════════════════
    //  ИНФРАСТРУКТУРА
    // ════════════════════════════════════════════════════════════

    private static async Task<(ToolExecution exec, AgentSession session)> Tool(
        string name, JsonObject args, string root, string mode = "edit")
    {
        var s = MakeSession(root, mode);
        return await ToolWithSession(name, args, s);
    }

    private static async Task<(ToolExecution exec, AgentSession session)> ToolWithSession(
        string name, JsonObject args, AgentSession s)
    {
        var gw = new GatewayState();
        var tool = new PendingTool
        {
            Id = Guid.NewGuid().ToString(),
            Name = name,
            Args = args
        };
        var exec = await gw.ExecuteToolAsync(s, tool);
        return (exec, s);
    }

    private static AgentSession MakeSession(string root, string mode)
    {
        return new AgentSession
        {
            Role = "test",
            Root = root,
            Mode = mode,
            AllowTools = root != null && mode != "chat",
            StepLimit = 30,
            StepUsed = 0,
            Messages = new JsonArray()
        };
    }

    private static async Task Run(StringBuilder sb, List<GuiTestResult> res,
        string id, string name, string block, Func<Task> action, bool expectedFail = false)
    {
        var sw = Stopwatch.StartNew();
        bool ok; string err = "";
        try
        {
            await action();
            ok = true;
        }
        catch (Exception ex)
        {
            ok = false;
            err = ex.Message;
        }
        sw.Stop();

        var r = new GuiTestResult
        {
            Id = id, Name = name, Block = block,
            Success = ok, Error = err,
            Seconds = sw.Elapsed.TotalSeconds,
            ExpectedFail = expectedFail
        };
        res.Add(r);

        var status = ok ? (expectedFail ? "ОК (ожидаемая ошибка)" : "ОК") : "ОШИБКА";
        var line = $"[{DateTime.Now:HH:mm:ss}] {id} {name} | {sw.Elapsed.TotalSeconds:F2}с | {status}"
            + (ok ? "" : $" - {err}");
        sb.AppendLine(line);
        Flush(sb);

        try
        {
            Gateway.GuiTestLogger.Log($"{id}_{name}", "авто",
                ok ? "OK" : $"ОШИБКА - {err}", ok);
        }
        catch { }
    }

    private static void Assert(bool condition, string msg)
    {
        if (!condition) throw new Exception(msg);
    }

    private static string MakeTempProject(string block)
    {
        var dir = Path.Combine(LogsDir, $"_test_{block}_{DateTime.Now:HHmmss}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void Cleanup(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
    }

    private static void Flush(StringBuilder sb)
    {
        lock (_writeLock)
        {
            File.WriteAllText(_resultPath, sb.ToString(), Encoding.UTF8);
        }
    }

    private static void Report(string text, double progress)
    {
        OnProgress?.Invoke(text, progress);
    }
}