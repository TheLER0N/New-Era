using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace MainApp;

public static class GatewayHost
{
    public static string FindSendReceivingDir()
    {
        var baseDir = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "sending and receiving")),
            Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "sending and receiving")),
            Path.GetFullPath(Path.Combine(baseDir, "..", "sending and receiving")),
            Path.Combine(baseDir, "sending and receiving")
        };
        foreach (var c in candidates)
        {
            try { if (Directory.Exists(c)) return c; } catch { }
        }
        return candidates[0];
    }

    public static void Start()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://localhost:51234");
        var app = builder.Build();
        var st = new GatewayState();
        app.MapPost("/agent-run", async (HttpContext ctx) =>
        {
            var body = await new StreamReader(ctx.Request.Body).ReadToEndAsync();
            var req = JsonSerializer.Deserialize<AgentRequest>(body, JsonOpts.Ci);
            if (req == null || string.IsNullOrWhiteSpace(req.Text))
                return Results.BadRequest(new { error = "text is required" });
            var mode = string.IsNullOrWhiteSpace(req.Mode) ? "edit" : req.Mode.ToLowerInvariant();
            string? root = null;
            if (!string.IsNullOrWhiteSpace(req.ProjectPath))
            {
                if (!Directory.Exists(req.ProjectPath))
                    return Results.BadRequest(new { error = "Папка проекта не найдена. Выбери проект в хабе заново." });
                root = req.ProjectPath;
            }
            // Раунды/вопросы планирования: потолок 30, значения 11–30 не обрезаются до 10.
            var planRounds = req.PlanRounds >= 0 ? req.PlanRounds : 1;
            var planMin = req.PlanMin > 0 ? Math.Min(req.PlanMin, 30) : 1;
            var planMax = req.PlanMax > 0 ? Math.Min(req.PlanMax, 30) : 3;
            if (planMax < planMin) planMax = planMin;
            var session = new AgentSession
            {
                Role = req.Role,
                Root = root,
                Mode = mode,
                Think = req.Think,
                AutoRepair = req.AutoRepair,
                AllowTools = root != null && mode != "chat",
                PlanRounds = planRounds,
                PlanMin = planMin,
                PlanMax = planMax
            };
            if (root != null)
            {
                var settings = st.GetProjectSettings(root);
                session.StepLimit = settings.MaxSteps ?? 30;
                settings.AutoRepair = req.AutoRepair;
            }
            session.Messages.Add(new JsonObject
            {
                ["role"] = "user",
                ["content"] = req.Text
            });
            var loopCts = st.BeginLoop(req.Role, ctx.RequestAborted);
            st.AgentLog("[BACKEND] браузер");
            var result = await st.RunBrowserAgentLoopAsync(session, loopCts.Token);
            st.EndLoop(req.Role, loopCts);
            return Results.Ok(result);
        });
        app.MapPost("/agent-approve", async (HttpContext ctx) =>
        {
            var body = await new StreamReader(ctx.Request.Body).ReadToEndAsync();
            var req = JsonSerializer.Deserialize<ApproveRequest>(body, JsonOpts.Ci);
            if (req == null || string.IsNullOrWhiteSpace(req.SessionId))
                return Results.BadRequest(new { error = "sessionId is required" });
            if (!st.AgentSessions.TryRemove(req.SessionId, out var s))
                return Results.BadRequest(new { error = "Сессия не найдена или устарела." });
            return Results.Ok(await st.HandleApproveAsync(s, req, ctx.RequestAborted));
        });
        app.MapPost("/cancel", async (HttpContext ctx) =>
        {
            var body = await new StreamReader(ctx.Request.Body).ReadToEndAsync();
            var req = JsonSerializer.Deserialize<CancelRequest>(body, JsonOpts.Ci);
            if (req == null || string.IsNullOrWhiteSpace(req.Role))
                return Results.BadRequest(new { error = "role is required" });
            st.CancelRole(req.Role);
            return Results.Ok(new { ok = true });
        });
        app.MapGet("/diag", async (HttpContext ctx) =>
        {
            foreach (var client in st.Clients)
            {
                try
                {
                    if (client.State == WebSocketState.Open)
                        await client.SendAsync(
                            new ArraySegment<byte>(Encoding.UTF8.GetBytes("DIAG?")),
                            WebSocketMessageType.Text, true, CancellationToken.None);
                }
                catch { }
            }
            await Task.Delay(700);
            return Results.Ok(new
            {
                gateway = "ok",
                ws_clients = st.Clients.Count,
                pending_wait_roles = st.PendingResponses.Keys,
                roles = st.RoleChatMap,
                token_present = false
            });
        });
        app.UseWebSockets();
        app.Use(async (context, next) =>
        {
            if (context.Request.Path == "/ws" && context.WebSockets.IsWebSocketRequest)
            {
                var webSocket = await context.WebSockets.AcceptWebSocketAsync();
                await st.HandleWsClientAsync(webSocket);
            }
            else
            {
                await next();
            }
        });
        app.MapGet("/status", () => Results.Ok(new
        {
            status = "LERON GUI работает",
            rolesWithChats = st.RoleChatMap.Count,
            roles = st.RoleChatMap.Keys
        }));
        app.MapPost("/send-and-wait", async (HttpContext ctx) =>
        {
            var body = await new StreamReader(ctx.Request.Body).ReadToEndAsync();
            var req = JsonSerializer.Deserialize<SendRequest>(body, JsonOpts.Ci);
            if (req == null || string.IsNullOrEmpty(req.Role))
                return Results.BadRequest(new { error = "role is required" });
            var result = await st.SendToBrowserAndWait(req.Role, req.Text, req.Think, Timeout.Infinite, ctx.RequestAborted, st.NextReqId());
            if (!result.ok)
                return Results.BadRequest(new { error = result.text });
            return Results.Ok(new { role = req.Role, response = result.text });
        });
        st.AgentLog("LERON GUI gateway запущен in-process на http://localhost:51234");
        app.Run();
    }
}