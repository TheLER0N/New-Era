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
            // Раунд 4: мутации запрещены, пока агент не получил контекст проекта
            // (list_files / grep / read_file / read_files). Без чтения правки идут вслепую.
            if (!s.HasContext && IsMutating(c.Name))
            {
                exec.Output =
                    "Мутация заблокирована: у сессии ещё нет контекста проекта. " +
                    "Сначала вызови list_files и/или read_files/grep, получи файлы проекта, " +
                    "затем повторяй эту правку.";
                exec.Log = $"{c.Name} → заблокировано до чтения контекста";
                exec.Card = ErrorCard("Мутация", "нет контекста — сначала чтение", c.Name);
                return exec;
            }
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
                    // Всегда отдаём содержимое: заглушка «файл не менялся»
                    // заставляла модель повторять read_file в цикле и жгла шаги.
                    var text = File.ReadAllText(p);
                    var fullLen = text.Length;
                    if (fullLen > 50000) text = text.Substring(0, 50000) +
                        $"\n…[обрезано: показано первые 50000 из {fullLen} символов]";
                    s.HasContext = true;
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
                case "read_files":
                {
                    var pathsNode = c.Args["paths"];
                    var rawPaths = new List<string>();
                    if (pathsNode is JsonArray arr)
                    {
                        foreach (var item in arr)
                        {
                            try { var v = item?.GetValue<string>(); if (!string.IsNullOrEmpty(v)) rawPaths.Add(v); } catch { }
                        }
                    }
                    if (rawPaths.Count == 0)
                    {
                        exec.Output = "Укажи параметр paths (массив путей).";
                        exec.Log = "read_files → нет путей";
                        exec.Card = ErrorCard("Чтение файлов", "нет путей", "");
                        return exec;
                    }
                    if (rawPaths.Count > 10) rawPaths = rawPaths.Take(10).ToList();
                    var sb = new StringBuilder();
                    int totalChars = 0;
                    // Раунд 4: пакетный лимит поднят с 12000 до 50000 символов —
                    // большой проект читается 1–3 пакетами вместо 5–10.
                    const int MAX_CHARS = 50000;
                    int read = 0;
                    var notFit = new List<string>();
                    foreach (var raw in rawPaths)
                    {
                        var p = ResolveSessionPath(s, raw, "read");
                        if (p == null)
                        {
                            sb.AppendLine($"=== файл: {raw} ===");
                            sb.AppendLine("Доступ отклонён: путь вне проекта.");
                            sb.AppendLine();
                            read++;
                            continue;
                        }
                        if (!File.Exists(p))
                        {
                            sb.AppendLine($"=== файл: {raw} ===");
                            sb.AppendLine($"Файл не найден: {raw}");
                            sb.AppendLine();
                            read++;
                            continue;
                        }
                        var text = File.ReadAllText(p);
                        var header = $"=== файл: {DisplayPath(s, p)} ===\n";
                        var needed = header.Length + text.Length + 2;
                        if (totalChars + needed > MAX_CHARS)
                        {
                            // Файл не влезает в пакет даже один и пакет ещё пуст —
                            // отдаём начало, полный текст — только read_file.
                            if (totalChars == 0 && text.Length > MAX_CHARS)
                            {
                                var headLen = Math.Max(0, MAX_CHARS - header.Length - 200);
                                sb.Append(header);
                                sb.AppendLine(text.Substring(0, headLen));
                                sb.AppendLine($"…[показаны первые {headLen} из {text.Length} символов — остальное через read_file]");
                                sb.AppendLine();
                                totalChars = MAX_CHARS;
                                read++;
                                continue;
                            }
                            // Не влез в остаток пакета — копим список невлезших.
                            notFit.Add(DisplayPath(s, p));
                            continue;
                        }
                        sb.Append(header);
                        sb.AppendLine(text);
                        sb.AppendLine();
                        totalChars += needed;
                        read++;
                    }
                    if (notFit.Count > 0)
                    {
                        sb.AppendLine("⚠ Не влезли в этот пакет: " + string.Join(", ", notFit));
                        sb.AppendLine("Следующим шагом вызови read_files именно с этими файлами — они придут следующим пакетом.");
                    }
                    s.HasContext = true;
                    exec.Output = sb.ToString();
                    exec.Log = $"read_files → прочитано {read}/{rawPaths.Count}, не влезло {notFit.Count}";
                    exec.Card = new ActionCard
                    {
                        Type = "read", Icon = "📖", Title = "Чтение файлов (пакет)",
                        Status = $"{read} файлов",
                        Details = Truncate(sb.ToString(), 4000)
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
                    s.HasContext = true;
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
                    var patternsNode = c.Args["patterns"];
                    var singlePattern = Arg("pattern");
                    var patterns = new List<string>();
                    if (patternsNode is JsonArray arr)
                    {
                        foreach (var item in arr)
                        {
                            try { var v = item?.GetValue<string>(); if (!string.IsNullOrEmpty(v)) patterns.Add(v); } catch { }
                        }
                    }
                    else if (!string.IsNullOrWhiteSpace(singlePattern))
                    {
                        patterns.Add(singlePattern);
                    }
                    // «|» внутри паттерна = варианты ИЛИ: модель пишет в regex-стиле,
                    // а поиск подстрочный — без разбиения было 0 совпадений и пустые шаги.
                    patterns = patterns
                        .SelectMany(p => p.Split('|', StringSplitOptions.RemoveEmptyEntries))
                        .Select(p => p.Trim())
                        .Where(p => p.Length > 0)
                        .Distinct()
                        .Take(20)
                        .ToList();
                    if (patterns.Count == 0)
                    {
                        exec.Output = "Укажи параметр patterns (массив) или pattern.";
                        exec.Log = "grep → нет паттернов";
                        exec.Card = ErrorCard("Поиск", "нет паттернов", "");
                        return exec;
                    }
                    var rawPath = string.IsNullOrWhiteSpace(Arg("path")) ? "." : Arg("path");
                    var caseSensitive = GetBool(c.Args, "case_sensitive", false);
                    var p = ResolveSessionPath(s, rawPath, "read");
                    if (p == null)
                    {
                        exec.Output = "Доступ отклонён: путь вне проекта.";
                        exec.Log = $"grep {rawPath} → отклонено";
                        exec.Card = ErrorCard("Поиск", "доступ вне проекта", rawPath);
                        return exec;
                    }
                    var sb = new StringBuilder();
                    int totalCount = 0;
                    foreach (var pattern in patterns)
                    {
                        sb.AppendLine($"=== паттерн: {pattern} ===");
                        string output; int count; bool truncated;
                        if (File.Exists(p))
                        {
                            var cmp = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
                            var localSb = new StringBuilder();
                            count = 0; truncated = false;
                            try
                            {
                                int lineNo = 0;
                                foreach (var line in File.ReadLines(p))
                                {
                                    lineNo++;
                                    if (!line.Contains(pattern, cmp)) continue;
                                    count++;
                                    var t = line.Trim();
                                    if (t.Length > 300) t = t.Substring(0, 300) + "…";
                                    localSb.AppendLine($"{DisplayPath(s, p)}:{lineNo}:{t}");
                                    if (count >= 200) { truncated = true; break; }
                                }
                            }
                            catch { }
                            output = localSb.ToString();
                        }
                        else if (Directory.Exists(p))
                        {
                            (output, count, truncated) = GrepFiles(s, p, pattern, caseSensitive);
                        }
                        else
                        {
                            sb.AppendLine($"Путь не найден: {rawPath}");
                            sb.AppendLine();
                            continue;
                        }
                        if (string.IsNullOrWhiteSpace(output)) output = "Совпадений нет.";
                        if (truncated) output += "\n…[вывод обрезан]";
                        sb.Append(output);
                        sb.AppendLine();
                        totalCount += count;
                    }
                    s.HasContext = true;
                    exec.Output = sb.ToString();
                    exec.Log = $"grep → {patterns.Count} паттернов, {totalCount} совпадений";
                    exec.Path = p;
                    exec.Card = new ActionCard
                    {
                        Type = "grep", Icon = "🔎", Title = "Поиск по файлам (пакет)",
                        Status = $"{totalCount} совпадений", Path = DisplayPath(s, p),
                        Count = totalCount, Details = Truncate(sb.ToString(), 4000)
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
                    var card = new ActionCard
                    {
                        Type = "write", Icon = "✏️", Title = "Запись файла", Status = "OK",
                        Path = DisplayPath(s, p), NewText = Truncate(content, 6000),
                        Details = $"{content.Length} символов"
                    };
                    if (File.Exists(p)) BackupFullPath(s, p, card);
                    File.WriteAllText(p, content);
                    s.SelfModified.Add(p);
                    exec.Output = $"Файл записан: {raw}" + (card.Backup ? "\nСоздан бэкап предыдущей версии." : "");
                    exec.Log = $"write_file {raw} → OK";
                    exec.Path = p;
                    exec.Mutated = true;
                    exec.Card = card;
                    return exec;
                }
                case "write_files":
                {
                    if (!ModeAllowsEdit(s.Mode))
                    {
                        exec.Output = "Запрещено: текущий режим не позволяет изменять файлы.";
                        exec.Log = "write_files → запрещено режимом";
                        exec.Card = ErrorCard("Запись файлов", "запрещено режимом", "");
                        return exec;
                    }
                    var filesNode = c.Args["files"];
                    if (filesNode is not JsonArray arr)
                    {
                        exec.Output = "Укажи параметр files (массив объектов {path, content}).";
                        exec.Log = "write_files → нет массива";
                        exec.Card = ErrorCard("Запись файлов", "нет массива", "");
                        return exec;
                    }
                    var sb = new StringBuilder();
                    int written = 0;
                    foreach (var item in arr)
                    {
                        if (item is not JsonObject obj) continue;
                        var rawPath = GetStr(obj, "path");
                        var content = GetStr(obj, "content");
                        if (string.IsNullOrWhiteSpace(rawPath)) continue;
                        var p = ResolveSessionPath(s, rawPath, "write");
                        if (p == null)
                        {
                            sb.AppendLine($"[{rawPath}] доступ отклонён: путь вне проекта");
                            continue;
                        }
                        var dir = Path.GetDirectoryName(p);
                        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                        var card = new ActionCard
                        {
                            Type = "write", Icon = "✏️", Title = "Запись файла", Status = "OK",
                            Path = DisplayPath(s, p), NewText = Truncate(content, 4000),
                            Details = $"{content.Length} символов"
                        };
                        if (File.Exists(p)) BackupFullPath(s, p, card);
                        File.WriteAllText(p, content);
                        s.SelfModified.Add(p);
                        s.ChangedFiles.Add(DisplayPath(s, p));
                        sb.AppendLine($"[{DisplayPath(s, p)}] записан ({content.Length} символов)" + (card.Backup ? " + бэкап" : ""));
                        s.Cards.Add(card);
                        s.ToolLog.Add($"write_file {rawPath} → OK");
                        AgentLog($"[TOOL] write_file {rawPath} → OK");
                        if (!string.IsNullOrEmpty(s.Role))
                            LogRole(s.Role, $"[TOOL]: write_file {rawPath} → OK");
                        written++;
                    }
                    exec.Output = sb.ToString();
                    exec.Log = $"write_files → записано {written} файлов";
                    exec.Mutated = written > 0;
                    exec.Card = new ActionCard
                    {
                        Type = "write", Icon = "✏️", Title = "Запись файлов (пакет)",
                        Status = $"{written} файлов",
                        Details = sb.ToString()
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
                    s.SelfModified.Add(p);
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
                    s.SelfModified.Add(dest);
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
                    // Чтение/разведка через shell отключены: вместо вывода — заглушка
                    // с указанием инструмента чтения, чтобы спираль «чтения по частям»
                    // (type / Get-Content -Skip …) не съедала запросы.
                    if (IsReadShellCommand(command))
                    {
                        exec.Output =
                            "Чтение и разведка через shell отключены (экономия запросов). " +
                            "Используй read_file/read_files — вернут полное содержимое файла в UTF-8 за один запрос; " +
                            "для поиска по многим файлам — grep с массивом patterns. " +
                            "Повтори чтение инструментом чтения.";
                        exec.Log = $"run_command \"{Truncate(command, 80)}\" → заглушка чтения";
                        exec.Card = new ActionCard
                        {
                            Type = "command", Icon = "🚫", Title = "Команда",
                            Status = "заглушка: чтение через shell", Command = command,
                            Shell = "CMD", ExitCode = 1, Details = exec.Output
                        };
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
                case "update_file_summaries":
                {
                    if (s.Root == null)
                    {
                        exec.Output = "Нет корня проекта: индекс файлов доступен только в режиме проекта.";
                        exec.Log = "update_file_summaries → нет корня";
                        exec.Card = ErrorCard("Файловый индекс", "нет корня проекта", "");
                        return exec;
                    }
                    var summariesNode = c.Args["summaries"];
                    if (summariesNode is not JsonArray sArr)
                    {
                        exec.Output = "Укажи параметр summaries (массив объектов {path, summary}).";
                        exec.Log = "update_file_summaries → нет массива";
                        exec.Card = ErrorCard("Файловый индекс", "нет summaries", "");
                        return exec;
                    }
                    var index = GetFileIndex(s.Root);
                    int updated = 0;
                    var sbi = new StringBuilder();
                    foreach (var item in sArr)
                    {
                        if (item is not JsonObject obj) continue;
                        var rawPath = GetStr(obj, "path");
                        var summary = GetStr(obj, "summary");
                        if (string.IsNullOrWhiteSpace(rawPath)) continue;
                        var rel = rawPath.Replace('\\', '/').Trim('/');
                        var entry = new FileIndexEntry { Summary = summary };
                        try
                        {
                            var full = Path.GetFullPath(Path.Combine(s.Root, rel.Replace('/', Path.DirectorySeparatorChar)));
                            if (File.Exists(full))
                            {
                                var fi = new FileInfo(full);
                                entry.MTime = fi.LastWriteTime;
                                entry.Size = fi.Length;
                            }
                        }
                        catch { }
                        index.Files[rel] = entry;
                        updated++;
                        sbi.AppendLine($"[{rel}] описание обновлено");
                    }
                    SaveFileIndex(s.Root);
                    exec.Output = sbi.Length > 0 ? sbi.ToString() : "Нечего обновлять: пустой массив summaries.";
                    exec.Log = $"update_file_summaries → {updated}";
                    exec.Card = new ActionCard
                    {
                        Type = "info", Icon = "🗂", Title = "Файловый индекс",
                        Status = $"{updated} описаний", Details = sbi.ToString()
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