using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace MainApp;

internal sealed partial class GatewayState
{
    public string? BackupFullPath(AgentSession s, string fullPath, ActionCard card)
    {
        try
        {
            if (s.Root == null) return null;
            var rootFull = Path.GetFullPath(s.Root);
            if (!fullPath.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase)) return null;

            var rel = Path.GetRelativePath(rootFull, fullPath);
            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
            var dest = Path.Combine(rootFull, ".leron", "backup", stamp, rel);

            if (File.Exists(fullPath))
            {
                var destDir = Path.GetDirectoryName(dest);
                if (!string.IsNullOrEmpty(destDir)) Directory.CreateDirectory(destDir);
                File.Copy(fullPath, dest, true);
            }
            else if (Directory.Exists(fullPath))
            {
                CopyDirectory(fullPath, dest);
            }
            else return null;

            card.Backup = true;
            return $".leron/backup/{stamp}/{rel.Replace('\\', '/')}";
        }
        catch { return null; }
    }

    private (string output, int count, bool truncated) GrepFiles(
        AgentSession s, string baseDir, string pattern, bool caseSensitive)
    {
        var sb = new StringBuilder();
        int count = 0, filesSeen = 0;
        bool truncated = false;
        var cmp = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        var stack = new Stack<string>();
        stack.Push(baseDir);

        while (stack.Count > 0 && !truncated)
        {
            var dir = stack.Pop();
            IEnumerable<string> dirs, files;
            try
            {
                dirs = Directory.EnumerateDirectories(dir);
                files = Directory.EnumerateFiles(dir);
            }
            catch { continue; }

            foreach (var d in dirs)
            {
                if (!SkipDir(Path.GetFileName(d))) stack.Push(d);
            }

            foreach (var f in files)
            {
                if (filesSeen++ > 3000) { truncated = true; break; }
                if (IsBinaryExt(f)) continue;
                try
                {
                    int lineNo = 0;
                    foreach (var line in File.ReadLines(f))
                    {
                        lineNo++;
                        if (!line.Contains(pattern, cmp)) continue;
                        count++;
                        var t = line.Trim();
                        if (t.Length > 300) t = t.Substring(0, 300) + "…";
                        sb.AppendLine($"{DisplayPath(s, f)}:{lineNo}:{t}");
                        if (count >= 200) { truncated = true; break; }
                    }
                }
                catch { }
                if (truncated) break;
            }
        }
        return (sb.ToString(), count, truncated);
    }

    public async Task<ToolExecution> ExecuteToolAsync(AgentSession s, PendingTool c)
    {
        string Arg(string key) => GetStr(c.Args, key);

        var exec = new ToolExecution
        {
            Tool = c.Name,
            Card = new ActionCard { Type = "info", Icon = "🛠", Title = c.Name, Status = "выполняется" }
        };

        try
        {
            switch (c.Name)
            {
                case "read_file":
                {
                    var raw = Arg("path");
                    var p = ResolveSessionPath(s, raw, "read");
                    if (p == null)
                    {
                        exec.Output = "Доступ отклонён: путь вне проекта.";
                        exec.Log = $"read_file {raw} → отклонено";
                        exec.Card = ErrorCard("Чтение файла", "доступ вне проекта", raw);
                        return exec;
                    }
                    if (!File.Exists(p))
                    {
                        exec.Output = $"Файл не найден: {raw}";
                        exec.Log = $"read_file {raw} → не найден";
                        exec.Card = ErrorCard("Чтение файла", "файл не найден", DisplayPath(s, p));
                        return exec;
                    }
                    var text = File.ReadAllText(p);
                    if (text.Length > 20000) text = text.Substring(0, 20000) + "\n…[обрезано]";
                    exec.Output = text;
                    exec.Log = $"read_file {raw} → OK";
                    exec.Path = p;
                    exec.Card = new ActionCard
                    {
                        Type = "read", Icon = "📖", Title = "Чтение файла", Status = "OK",
                        Path = DisplayPath(s, p), Details = Truncate(text, 4000)
                    };
                    return exec;
                }

                case "list_files":
                {
                    var raw = string.IsNullOrWhiteSpace(Arg("path")) ? "." : Arg("path");
                    var p = ResolveSessionPath(s, raw, "read");
                    if (p == null)
                    {
                        exec.Output = "Доступ отклонён: путь вне проекта.";
                        exec.Log = $"list_files {raw} → отклонено";
                        exec.Card = ErrorCard("Список файлов", "доступ вне проекта", raw);
                        return exec;
                    }
                    if (!Directory.Exists(p))
                    {
                        exec.Output = $"Папка не найдена: {raw}";
                        exec.Log = $"list_files {raw} → не найдена";
                        exec.Card = ErrorCard("Список файлов", "папка не найдена", raw);
                        return exec;
                    }
                    var dirs = Directory.GetDirectories(p).OrderBy(x => x).Take(500).ToList();
                    var files = Directory.GetFiles(p).OrderBy(x => x).Take(500).ToList();
                    var sb = new StringBuilder();
                    foreach (var d in dirs) sb.AppendLine("<DIR> " + Path.GetFileName(d));
                    foreach (var f in files) sb.AppendLine(Path.GetFileName(f));
                    var list = sb.ToString();
                    if (string.IsNullOrWhiteSpace(list)) list = "(пусто)";
                    exec.Output = list;
                    exec.Log = $"list_files {raw} → OK";
                    exec.Path = p;
                    exec.Card = new ActionCard
                    {
                        Type = "list", Icon = "📂", Title = "Список файлов", Status = "OK",
                        Path = DisplayPath(s, p), Count = dirs.Count + files.Count,
                        Details = Truncate(list, 4000)
                    };
                    return exec;
                }

                case "grep":
                {
                    var pattern = Arg("pattern");
                    var rawPath = string.IsNullOrWhiteSpace(Arg("path")) ? "." : Arg("path");
                    var caseSensitive = GetBool(c.Args, "case_sensitive", false);
                    if (string.IsNullOrWhiteSpace(pattern))
                    {
                        exec.Output = "Укажи параметр pattern для grep.";
                        exec.Log = "grep → нет паттерна";
                        exec.Card = ErrorCard("Поиск", "нет паттерна", rawPath);
                        return exec;
                    }
                    var p = ResolveSessionPath(s, rawPath, "read");
                    if (p == null)
                    {
                        exec.Output = "Доступ отклонён: путь вне проекта.";
                        exec.Log = $"grep {rawPath} → отклонено";
                        exec.Card = ErrorCard("Поиск", "доступ вне проекта", rawPath);
                        return exec;
                    }

                    string output; int count; bool truncated;
                    if (File.Exists(p))
                    {
                        var dir = Path.GetDirectoryName(p) ?? ".";
                        var fileName = Path.GetFileName(p);
                        var all = GrepFiles(s, dir, pattern, caseSensitive);
                        var lines = all.output.Split('\n')
                            .Where(x => x.StartsWith(fileName + ":", StringComparison.OrdinalIgnoreCase))
                            .ToList();
                        output = string.Join('\n', lines);
                        count = lines.Count;
                        truncated = all.truncated;
                    }
                    else if (Directory.Exists(p))
                    {
                        (output, count, truncated) = GrepFiles(s, p, pattern, caseSensitive);
                    }
                    else
                    {
                        exec.Output = $"Путь не найден: {rawPath}";
                        exec.Log = $"grep {rawPath} → не найден";
                        exec.Card = ErrorCard("Поиск", "путь не найден", rawPath);
                        return exec;
                    }

                    if (string.IsNullOrWhiteSpace(output)) output = "Совпадений нет.";
                    if (truncated) output += "\n…[вывод обрезан]";
                    exec.Output = output;
                    exec.Log = $"grep \"{pattern}\" → {count} совпадений";
                    exec.Path = p;
                    exec.Card = new ActionCard
                    {
                        Type = "grep", Icon = "🔎", Title = "Поиск по файлам",
                        Status = $"{count} совпадений", Path = DisplayPath(s, p),
                        Count = count, Details = Truncate(output, 4000)
                    };
                    return exec;
                }

                case "write_file":
                {
                    if (!ModeAllowsEdit(s.Mode))
                    {
                        exec.Output = "Запрещено: текущий режим не позволяет изменять файлы.";
                        exec.Log = "write_file → запрещено режимом";
                        exec.Card = ErrorCard("Запись файла", "запрещено режимом", Arg("path"));
                        return exec;
                    }
                    var raw = Arg("path");
                    var p = ResolveSessionPath(s, raw, "write");
                    if (p == null)
                    {
                        exec.Output = "Доступ отклонён: путь вне проекта.";
                        exec.Log = $"write_file {raw} → отклонено";
                        exec.Card = ErrorCard("Запись файла", "доступ вне проекта", raw);
                        return exec;
                    }
                    var dir = Path.GetDirectoryName(p);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    var content = Arg("content");
                    File.WriteAllText(p, content);
                    exec.Output = $"Файл записан: {raw}";
                    exec.Log = $"write_file {raw} → OK";
                    exec.Path = p;
                    exec.Mutated = true;
                    exec.Card = new ActionCard
                    {
                        Type = "write", Icon = "✏️", Title = "Запись файла", Status = "OK",
                        Path = DisplayPath(s, p), NewText = Truncate(content, 6000),
                        Details = $"{content.Length} символов"
                    };
                    return exec;
                }

                case "patch_file":
                case "edit_file":
                {
                    if (!ModeAllowsEdit(s.Mode))
                    {
                        exec.Output = "Запрещено: текущий режим не позволяет изменять файлы.";
                        exec.Log = $"{c.Name} → запрещено режимом";
                        exec.Card = ErrorCard("Патч файла", "запрещено режимом", Arg("path"));
                        return exec;
                    }
                    var raw = Arg("path");
                    var p = ResolveSessionPath(s, raw, "write");
                    if (p == null)
                    {
                        exec.Output = "Доступ отклонён: путь вне проекта.";
                        exec.Log = $"{c.Name} {raw} → отклонено";
                        exec.Card = ErrorCard("Патч файла", "доступ вне проекта", raw);
                        return exec;
                    }
                    if (!File.Exists(p))
                    {
                        exec.Output = $"Файл не найден: {raw}. Сначала прочитай его через read_file.";
                        exec.Log = $"{c.Name} {raw} → не найден";
                        exec.Card = ErrorCard("Патч файла", "файл не найден", raw);
                        return exec;
                    }
                    var text = File.ReadAllText(p);
                    var old = Arg("old_text");
                    var newText = Arg("new_text");
                    if (string.IsNullOrEmpty(old) || !text.Contains(old))
                    {
                        exec.Output = "old_text не найден в файле. Прочитай файл через read_file и попробуй снова.";
                        exec.Log = $"{c.Name} {raw} → old_text не найден";
                        exec.Card = ErrorCard("Патч файла", "фрагмент не найден", DisplayPath(s, p));
                        return exec;
                    }
                    var card = new ActionCard
                    {
                        Type = "patch", Icon = "✏️", Title = "Патч файла", Status = "OK",
                        Path = DisplayPath(s, p),
                        OldText = Truncate(old, 4000), NewText = Truncate(newText, 4000),
                        Details = $"фрагмент заменён ({old.Length} → {newText.Length} символов)"
                    };
                    var backup = BackupFullPath(s, p, card);
                    int idx = text.IndexOf(old, StringComparison.Ordinal);
                    text = text.Substring(0, idx) + newText + text.Substring(idx + old.Length);
                    File.WriteAllText(p, text);
                    exec.Output = $"Файл изменён: {raw}" + (backup != null ? $"\nСоздан бэкап: {backup}" : "");
                    exec.Log = $"{c.Name} {raw} → OK";
                    exec.Path = p;
                    exec.Mutated = true;
                    exec.Card = card;
                    return exec;
                }

                case "rename_file":
                {
                    if (!ModeAllowsEdit(s.Mode))
                    {
                        exec.Output = "Запрещено: текущий режим не позволяет изменять файлы.";
                        exec.Log = "rename_file → запрещено режимом";
                        exec.Card = ErrorCard("Переименование", "запрещено режимом", Arg("path"));
                        return exec;
                    }
                    var rawSource = Arg("path");
                    var rawDest = Arg("new_path");
                    var source = ResolveSessionPath(s, rawSource, "delete");
                    var dest = ResolveSessionPath(s, rawDest, "write");
                    if (source == null || dest == null)
                    {
                        exec.Output = "Доступ отклонён: путь вне проекта.";
                        exec.Log = "rename_file → отклонено";
                        exec.Card = ErrorCard("Переименование", "доступ вне проекта", rawSource);
                        return exec;
                    }
                    if (!File.Exists(source) && !Directory.Exists(source))
                    {
                        exec.Output = $"Не найдено: {rawSource}";
                        exec.Log = $"rename_file {rawSource} → не найдено";
                        exec.Card = ErrorCard("Переименование", "не найдено", rawSource);
                        return exec;
                    }
                    if (File.Exists(dest) || Directory.Exists(dest))
                    {
                        exec.Output = $"Назначение уже существует: {rawDest}";
                        exec.Log = $"rename_file {rawSource} → назначение занято";
                        exec.Card = ErrorCard("Переименование", "назначение занято", rawDest);
                        return exec;
                    }
                    var card = new ActionCard
                    {
                        Type = "rename", Icon = "📦", Title = "Переименование / перемещение",
                        Status = "OK", Path = DisplayPath(s, source),
                        Details = $"{DisplayPath(s, source)} → {DisplayPath(s, dest)}"
                    };
                    var backup = BackupFullPath(s, source, card);
                    var destDir = Path.GetDirectoryName(dest);
                    if (!string.IsNullOrEmpty(destDir)) Directory.CreateDirectory(destDir);
                    if (File.Exists(source)) File.Move(source, dest);
                    else Directory.Move(source, dest);
                    exec.Output = $"Переименовано: {rawSource} → {rawDest}" +
                                  (backup != null ? $"\nСоздан бэкап: {backup}" : "");
                    exec.Log = $"rename_file {rawSource} → OK";
                    exec.Path = dest;
                    exec.Mutated = true;
                    exec.Card = card;
                    return exec;
                }

                case "delete_file":
                {
                    if (!ModeAllowsEdit(s.Mode))
                    {
                        exec.Output = "Запрещено: текущий режим не позволяет изменять файлы.";
                        exec.Log = "delete_file → запрещено режимом";
                        exec.Card = ErrorCard("Удаление", "запрещено режимом", Arg("path"));
                        return exec;
                    }
                    var raw = Arg("path");
                    var p = ResolveSessionPath(s, raw, "delete");
                    if (p == null)
                    {
                        exec.Output = "Доступ отклонён: путь вне проекта.";
                        exec.Log = $"delete_file {raw} → отклонено";
                        exec.Card = ErrorCard("Удаление", "доступ вне проекта", raw);
                        return exec;
                    }
                    if (s.Root != null &&
                        Path.GetFullPath(p).Equals(Path.GetFullPath(s.Root), StringComparison.OrdinalIgnoreCase))
                    {
                        exec.Output = "Нельзя удалить корень проекта.";
                        exec.Log = "delete_file → попытка удалить корень";
                        exec.Card = ErrorCard("Удаление", "нельзя удалить корень", raw);
                        return exec;
                    }
                    var card = new ActionCard
                    {
                        Type = "delete", Icon = "🗑", Title = "Удаление",
                        Status = "OK", Path = DisplayPath(s, p)
                    };
                    var backup = BackupFullPath(s, p, card);
                    if (File.Exists(p)) File.Delete(p);
                    else if (Directory.Exists(p)) Directory.Delete(p, true);
                    else
                    {
                        exec.Output = $"Не найдено: {raw}";
                        exec.Log = $"delete_file {raw} → не найдено";
                        exec.Card = ErrorCard("Удаление", "не найдено", raw);
                        return exec;
                    }
                    exec.Output = $"Удалено: {raw}" + (backup != null ? $"\nСоздан бэкап: {backup}" : "");
                    exec.Log = $"delete_file {raw} → OK";
                    exec.Path = p;
                    exec.Mutated = true;
                    exec.Card = card;
                    return exec;
                }

                case "create_directory":
                {
                    if (!ModeAllowsEdit(s.Mode))
                    {
                        exec.Output = "Запрещено: текущий режим не позволяет изменять файлы.";
                        exec.Log = "create_directory → запрещено режимом";
                        exec.Card = ErrorCard("Создание папки", "запрещено режимом", Arg("path"));
                        return exec;
                    }
                    var raw = Arg("path");
                    var p = ResolveSessionPath(s, raw, "write");
                    if (p == null)
                    {
                        exec.Output = "Доступ отклонён: путь вне проекта.";
                        exec.Log = $"create_directory {raw} → отклонено";
                        exec.Card = ErrorCard("Создание папки", "доступ вне проекта", raw);
                        return exec;
                    }
                    Directory.CreateDirectory(p);
                    exec.Output = $"Папка создана: {raw}";
                    exec.Log = $"create_directory {raw} → OK";
                    exec.Path = p;
                    exec.Mutated = true;
                    exec.Card = new ActionCard
                    {
                        Type = "create", Icon = "📁", Title = "Создание папки",
                        Status = "OK", Path = DisplayPath(s, p)
                    };
                    return exec;
                }

                case "run_command":
                {
                    if (!ModeAllowsEdit(s.Mode))
                    {
                        exec.Output = "Запрещено: команды недоступны в этом режиме.";
                        exec.Log = "run_command → запрещено режимом";
                        exec.Card = ErrorCard("Команда", "запрещено режимом", Arg("command"));
                        return exec;
                    }
                    var command = Arg("command");
                    if (string.IsNullOrWhiteSpace(command))
                    {
                        exec.Output = "Укажи параметр command.";
                        exec.Log = "run_command → пустая команда";
                        exec.Card = ErrorCard("Команда", "пустая команда", "");
                        return exec;
                    }
                    var cwdRaw = Arg("cwd");
                    string? cwd = s.Root;
                    if (!string.IsNullOrWhiteSpace(cwdRaw))
                    {
                        cwd = ResolveSessionPath(s, cwdRaw, "execute");
                        if (cwd == null)
                        {
                            exec.Output = "Доступ отклонён: рабочая папка вне проекта.";
                            exec.Log = $"run_command cwd={cwdRaw} → отклонено";
                            exec.Card = ErrorCard("Команда", "рабочая папка вне проекта", command);
                            return exec;
                        }
                    }
                    var timeout = GetInt(c.Args, "timeout_ms", 120000);
                    var result = await RunProcessAsync(command, cwd, timeout);
                    exec.Output = $"exit_code: {result.ExitCode}\nstdout:\n{result.StdOut}\nstderr:\n{result.StdErr}";
                    exec.Log = $"run_command \"{command}\" → exit {result.ExitCode}";
                    exec.Card = new ActionCard
                    {
                        Type = "command", Icon = "▶️", Title = "Команда",
                        Status = $"exit {result.ExitCode}", Command = command,
                        Shell = result.Shell, ExitCode = result.ExitCode, Details = result.Output
                    };
                    return exec;
                }

                default:
                    exec.Output = $"Неизвестный инструмент: {c.Name}";
                    exec.Log = $"{c.Name} → неизвестный";
                    exec.Card = ErrorCard("Инструмент", "неизвестный", c.Name);
                    return exec;
            }
        }
        catch (Exception ex)
        {
            exec.Output = "Ошибка выполнения инструмента: " + ex.Message;
            exec.Log = $"{c.Name} → ошибка";
            exec.Card = ErrorCard(c.Name, ex.Message, "");
            return exec;
        }
    }
}