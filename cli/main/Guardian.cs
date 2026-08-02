// Guardian.cs — SYSTEM_GUARDIAN: промпты, анализ, парсинг, валидация
// New Era CLI v5.2 · partial class MainConsole
using System;
using System.Text;
using System.Text.RegularExpressions;

class GuardianAnalysis { public string EnhancedTask; public string TargetFiles; public string Acceptance; public string Suggestions; public bool IsValid; }

partial class MainConsole
{
    const string GuardianAnalysisPrompt =
        "You are SYSTEM_GUARDIAN in a two-level code editing system. Your role: analyze the task and produce a structured plan for CODE_WRITER.\n" +
        "Output EXACTLY these sections:\nENHANCED_TASK: <clarified task>\nTARGET_FILES: <file1> [READ|MODIFY|CREATE], <file2> [READ|MODIFY|CREATE], ...\nACCEPTANCE: <criteria that must hold after the edit>\nSUGGESTIONS: <optional hints for CODE_WRITER>\nNo other text. No markdown fences.";

    const string GuardianValidationPrompt =
        "You are SYSTEM_GUARDIAN. Validate the proposed code change. Check: syntax, logic, completeness, adherence to the plan. Respond with PASS or FAIL followed by specific errors. No markdown fences, no extra text.";

    static GuardianAnalysis ParseGuardianAnalysis(string raw) {
        var result = new GuardianAnalysis(); if (string.IsNullOrWhiteSpace(raw)) return result;
        result.EnhancedTask = ExtractSection(raw, "ENHANCED_TASK:"); result.TargetFiles = ExtractSection(raw, "TARGET_FILES:");
        result.Acceptance = ExtractSection(raw, "ACCEPTANCE:"); result.Suggestions = ExtractSection(raw, "SUGGESTIONS:");
        result.IsValid = !string.IsNullOrWhiteSpace(result.EnhancedTask) && !string.IsNullOrWhiteSpace(result.TargetFiles) && !string.IsNullOrWhiteSpace(result.Acceptance);
        return result;
    }
    static string ExtractSection(string text, string marker) {
        if (string.IsNullOrEmpty(text)) return null; int idx = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase); if (idx < 0) return null;
        int from = idx + marker.Length; string[] nextMarkers = { "ENHANCED_TASK:", "TARGET_FILES:", "ACCEPTANCE:", "SUGGESTIONS:" }; int end = text.Length;
        foreach (string nm in nextMarkers) { if (nm == marker.TrimEnd(':')) continue; int mi = text.IndexOf(nm, from, StringComparison.OrdinalIgnoreCase); if (mi >= 0 && mi < end) end = mi; }
        string val = text.Substring(from, end - from).Trim(); return string.IsNullOrWhiteSpace(val) ? null : val;
    }
    static bool IsGuardianPass(string raw) { if (string.IsNullOrWhiteSpace(raw)) return false; string upper = raw.ToUpperInvariant(); if (upper.Contains("FAIL")) return false; return upper.Contains("PASS"); }
    static string ExtractGuardianErrors(string raw) {
        if (string.IsNullOrWhiteSpace(raw)) return "unknown"; int failIdx = raw.ToUpperInvariant().IndexOf("FAIL");
        if (failIdx >= 0) { string after = raw.Substring(failIdx + 4).Trim(); if (after.Length > 500) after = after.Substring(0, 500) + "..."; return after.Length > 0 ? after : "validation failed"; }
        string trimmed = raw.Trim(); return trimmed.Length > 500 ? trimmed.Substring(0, 500) + "..." : trimmed;
    }
    static string ExtractGuardianCoordinates(string raw) { if (string.IsNullOrWhiteSpace(raw)) return null; Match m = Regex.Match(raw, @"COORDINATES:\s*(.+)"); return m.Success ? m.Groups[1].Value.Trim() : null; }
}