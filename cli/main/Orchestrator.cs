// Orchestrator.cs — Dual-LLM оркестратор: улучшение промптов, выбор файлов, селективный контекст
// New Era CLI v5.3 · partial class MainConsole
// C# 5 / .NET Framework 4.x
//
// v5.3:
//   - Добавлены видимые диагностики при ошибках улучшения промпта.
//   - Поведение выбора файлов осталось прежним, но теперь ошибки не тихие.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

partial class MainConsole
{
    // ══════════════════════════════════════════════════════════
    //  ORCHESTRATOR SYSTEM PROMPTS
    // ══════════════════════════════════════════════════════════
    const string OrchSystemPromptEnhance =
        "You are a prompt optimizer for a CLI code editor tool. " +
        "Your job: rewrite the user's task description to be maximally clear and actionable for a code-generation LLM. Rules:\n" +
        "- Keep the original intent exactly.\n" +
        "- Add specificity: mention file types, expected format, constraints.\n" +
        "- If the task is about editing code, emphasize: return ONLY code, no explanations.\n" +
        "- If the task is about creating files, emphasize: use === FILE: name === / === END === format.\n" +
        "- Output ONLY the rewritten task text. No preamble, no markdown fences.\n" +
        "- Language: same as the input task.";

    const string OrchSystemPromptSelectFiles =
        "You are a file selector for a code project. " +
        "Given a task description and a project file tree, return ONLY the files that are relevant to completing the task. Rules:\n" +
        "- Return a JSON object: {\"files\": [\"relative/path1\", \"relative/path2\", ...]}\n" +
        "- Include only files that need to be READ or MODIFIED for the task.\n" +
        "- Typically 3-10 files. Never more than 15.\n" +
        "- Use relative paths from the project root.\n" +
        "- Do NOT include binary files, images, or generated output.\n" +
        "- Output ONLY valid JSON. No markdown, no explanation.";

    // ══════════════════════════════════════════════════════════
    //  ORCHESTRATE REQUEST (entry point)
    // ══════════════════════════════════════════════════════════
    static string OrchestrateRequest(string userInput, string projectPath)
    {
        if (!OrchestratorEnabled || string.IsNullOrWhiteSpace(userInput))
            return userInput;

        try
        {
            string enhanced = EnhancePrompt(userInput);
            if (!string.IsNullOrWhiteSpace(enhanced))
                return enhanced;
        }
        catch { }

        return userInput;
    }

    // ══════════════════════════════════════════════════════════
    //  ENHANCE PROMPT
    // ══════════════════════════════════════════════════════════
    static string EnhancePrompt(string task)
    {
        if (string.IsNullOrWhiteSpace(task) || !OrchestratorEnabled)
            return null;

        try
        {
            string response = PostOrchestratorMessage(OrchSystemPromptEnhance, task);

            if (string.IsNullOrWhiteSpace(response))
            {
                WriteColored(ConsoleColor.Yellow, "  ⚠ orchestrator: пустой ответ при улучшении промпта\n");
                return null;
            }

            // Очистка от markdown-обёрток (```)
            string cleaned = StripMarkdownFences(response);

            // Защита от мусора: если результат короче 1/3 оригинала — отбрасываем
            if (cleaned.Length < task.Length / 3)
            {
                WriteColored(ConsoleColor.Yellow, "  ⚠ orchestrator: слишком короткий ответ при улучшении промпта — bypass\n");
                return null;
            }

            return cleaned;
        }
        catch (Exception ex)
        {
            WriteColored(ConsoleColor.Yellow, "  ⚠ orchestrator: ошибка улучшения (" + ex.Message + ")\n");
            return null;
        }
    }

    // ══════════════════════════════════════════════════════════
    //  ANALYZE AND SELECT FILES
    // ══════════════════════════════════════════════════════════
    static List<string> AnalyzeAndSelectFiles(string task, string projectPath)
    {
        var empty = new List<string>();

        if (string.IsNullOrWhiteSpace(task) || string.IsNullOrWhiteSpace(projectPath) || !OrchestratorEnabled)
            return empty;

        try
        {
            // Построить структуру проекта, ограничить 8000 символов
            string structure = ScanDirectory(projectPath, 0);
            if (structure.Length > 8000)
                structure = structure.Substring(0, 8000) + "\n... [truncated]";

            string userPrompt =
                "Task: " + task + "\n" +
                "Project structure:\n" + structure + "\n" +
                "Select the files needed for this task. Return JSON: {\"files\": [...]}";

            string response = PostOrchestratorMessage(OrchSystemPromptSelectFiles, userPrompt);

            if (string.IsNullOrWhiteSpace(response))
            {
                WriteColored(ConsoleColor.Yellow, "  ⚠ orchestrator: пустой ответ при выборе файлов\n");
                return empty;
            }

            List<string> result = ParseFileSelectionResponse(response);

            if (result.Count == 0)
            {
                WriteColored(ConsoleColor.Yellow, "  ⚠ orchestrator: файлы не выбраны (ответ не распознан)\n");
            }

            return result;
        }
        catch (Exception ex)
        {
            WriteColored(ConsoleColor.Yellow, "  ⚠ orchestrator: ошибка выбора файлов (" + ex.Message + ")\n");
            return empty;
        }
    }

    // ══════════════════════════════════════════════════════════
    //  PARSE FILE SELECTION RESPONSE (private helper)
    // ══════════════════════════════════════════════════════════
    static List<string> ParseFileSelectionResponse(string response)
    {
        var result = new List<string>();

        if (string.IsNullOrWhiteSpace(response))
            return result;

        // Убрать markdown-обёртки
        string cleaned = StripMarkdownFences(response);

        // Попытка 1: десериализация JSON через JavaScriptSerializer
        try
        {
            var ser = new JavaScriptSerializer();
            ser.MaxJsonLength = int.MaxValue;

            var obj = ser.DeserializeObject(cleaned) as Dictionary<string, object>;
            if (obj != null && obj.ContainsKey("files"))
            {
                object[] arr = obj["files"] as object[];
                if (arr != null)
                {
                    foreach (object item in arr)
                    {
                        string path = item as string;
                        if (!string.IsNullOrWhiteSpace(path))
                        {
                            result.Add(path.Replace('\\', '/'));
                            if (result.Count >= 15) break;
                        }
                    }
                }
            }

            if (result.Count > 0)
                return result;
        }
        catch { }

        // Попытка 2: fallback regex по строкам-путям
        try
        {
            MatchCollection matches = Regex.Matches(cleaned, @"""([^""]+\.\w+)""");

            foreach (Match m in matches)
            {
                string path = m.Groups[1].Value;

                if (!string.IsNullOrWhiteSpace(path) && !result.Contains(path))
                {
                    result.Add(path.Replace('\\', '/'));
                    if (result.Count >= 15) break;
                }
            }
        }
        catch { }

        return result;
    }

    // ══════════════════════════════════════════════════════════
    //  BUILD SELECTIVE PAYLOAD
    // ══════════════════════════════════════════════════════════
    static string BuildSelectivePayload(List<string> fileList, string projectPath)
    {
        if (fileList == null || fileList.Count == 0 || string.IsNullOrWhiteSpace(projectPath))
            return null;

        // Определить baseDir
        string baseDir = null;

        try
        {
            baseDir = Directory.Exists(projectPath) ? projectPath : Path.GetDirectoryName(projectPath);
        }
        catch { }

        if (string.IsNullOrEmpty(baseDir))
            baseDir = BaseDir;

        const int maxTotalChars = 120000;
        const int maxFileChars = 40000;

        var sb = new StringBuilder();

        long totalChars = 0;
        int included = 0;
        int skipped = 0;

        foreach (string relPath in fileList)
        {
            if (string.IsNullOrWhiteSpace(relPath))
            {
                skipped++;
                continue;
            }

            // Резолвить путь
            string fullPath;

            try
            {
                string normalized = relPath.Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
                fullPath = Path.IsPathRooted(normalized) ? normalized : Path.Combine(baseDir, normalized);
                fullPath = Path.GetFullPath(fullPath);
            }
            catch
            {
                skipped++;
                continue;
            }

            if (!File.Exists(fullPath))
            {
                skipped++;
                continue;
            }

            // Читать файл через ReadTextAuto
            string body;

            try
            {
                body = ReadTextAuto(fullPath);
            }
            catch
            {
                skipped++;
                continue;
            }

            if (string.IsNullOrEmpty(body))
            {
                skipped++;
                continue;
            }

            // Нормализовать \r\n → \n
            body = body.Replace("\r\n", "\n").TrimEnd('\r', '\n');

            // Обрезать по лимиту файла
            bool truncated = false;

            if (body.Length > maxFileChars)
            {
                body = body.Substring(0, maxFileChars);
                truncated = true;
            }

            // Проверить общий лимит
            long blockLen = (long)body.Length + relPath.Length + 40;

            if (totalChars + blockLen > maxTotalChars)
            {
                skipped++;
                continue;
            }

            totalChars += blockLen;
            included++;

            // Формат блока
            string displayPath = relPath.Replace('\\', '/');

            sb.Append("\n=== FILE: " + displayPath + " ===\n");
            sb.Append(body);
            sb.Append("\n");

            if (truncated)
                sb.Append("// [truncated to 40000 chars]\n");

            sb.Append("=== END ===\n");
        }

        if (included == 0)
            return null;

        if (skipped > 0)
            sb.Append("\n// [selective context: " + included + " loaded, " + skipped + " skipped]\n");

        return sb.ToString();
    }

    // ══════════════════════════════════════════════════════════
    //  PRE-FETCH CONTEXT (stub for v4.4)
    // ══════════════════════════════════════════════════════════
    // TODO v4.4: предзагрузка релевантных паттернов/документации до начала генерации.
    static string PreFetchContext(string task)
    {
        return null;
    }

    // ══════════════════════════════════════════════════════════
    //  PRIVATE HELPERS
    // ══════════════════════════════════════════════════════════
    static string StripMarkdownFences(string text)
    {
        if (string.IsNullOrEmpty(text))
            return "";

        string t = text.Trim();

        // Убрать открывающий ``` (с возможным языком)
        if (t.StartsWith("```"))
        {
            int firstNl = t.IndexOf('\n');
            if (firstNl >= 0)
                t = t.Substring(firstNl + 1);
            else
                t = t.Substring(3);
        }

        // Убрать закрывающий ```
        if (t.EndsWith("```"))
            t = t.Substring(0, t.Length - 3);

        return t.Trim();
    }
}