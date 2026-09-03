using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MainApp;

public partial class MainWindow
{
    private const double CardLeft = 54;

    internal void RenderCards(List<ActionCardDto>? cards)
    {
        if (cards == null) return;
        foreach (var card in cards)
            AddActionCard(card);
    }

    private void AddActionCard(ActionCardDto card)
    {
        var borderColor = "#12404f";
        if (card.Type == "read") { borderColor = "#1f6f86"; } else if (card.Type == "error") borderColor = "#e94560";
        else if (card.Type == "outside") borderColor = "#e94560";
        else if (card.Type == "repair") borderColor = "#ffb14a";
        else if (card.Type == "summary")
            borderColor = card.Status == "failed" ? "#e94560" : "#00d9ff";
        else if (card.ExitCode.HasValue && card.ExitCode.Value != 0) borderColor = "#e94560";
        if (card.Type == "patch" && card.OldText != null && card.NewText != null) { var diffBorder = new Border { Background = B("#07141d"), BorderBrush = B("#00d9ff"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(12), Margin = new Thickness(CardLeft, 6, 0, 6) }; var diffSp = new StackPanel(); diffSp.Children.Add(CardText("📝 " + (card.Path ?? "patch"), "#00d9ff", 15, true)); var oldLines = card.OldText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None); var newLines = card.NewText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None); for (int i = 0; i < Math.Max(oldLines.Length, newLines.Length); i++) { if (i < oldLines.Length && (i >= newLines.Length || oldLines[i] != newLines[i])) diffSp.Children.Add(CardText("- " + oldLines[i], "#e94560", 13)); if (i < newLines.Length && (i >= oldLines.Length || oldLines[i] != newLines[i])) diffSp.Children.Add(CardText("+ " + newLines[i], "#00d9ff", 13)); } diffBorder.Child = diffSp; ChatScroll.Content = ChatScroll.Content as StackPanel; ((StackPanel)ChatScroll.Content).Children.Add(diffBorder); return; }
var border = new Border
        {
            Background = B("#07141d"), BorderBrush = B(borderColor),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(CardLeft, 6, 0, 6)
        };
        var sp = new StackPanel();
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel { Orientation = Orientation.Horizontal };
        left.Children.Add(CardText(card.Icon + " ", "#00d9ff", 15, true));
        left.Children.Add(CardText(card.Title, "#eaf6ff", 15, true));
        if (!string.IsNullOrWhiteSpace(card.Status))
            left.Children.Add(CardText(" · " + card.Status, "#8fe6ff", 14));
        var sub = !string.IsNullOrWhiteSpace(card.Path) ? card.Path
            : !string.IsNullOrWhiteSpace(card.Command) ? card.Command : "";
        if (!string.IsNullOrWhiteSpace(sub))
            left.Children.Add(CardText("  " + sub, "#6f96a8", 14));
        if (card.Type == "outside" || IsOutsidePath(card.Path))
            left.Children.Add(CardText("  вне проекта", "#e94560", 14));
        Grid.SetColumn(left, 0);
        header.Children.Add(left);
        var details = new StackPanel
        {
            Margin = new Thickness(0, 6, 0, 0),
            Visibility = Visibility.Collapsed
        };
        FillCardDetails(details, card);
        if (card.Backup)
            details.Children.Add(CardText("💾 создан бэкап", "#8fe6ff", 14));
        if (details.Children.Count > 0)
        {
            var chevron = new Button
            {
                Content = "˄", Width = 32, Height = 26,
                Padding = new Thickness(0),
                Background = B("#07141d"), BorderBrush = B("#07141d"),
                Foreground = B("#6f96a8"), FontFamily = Theme.Font(),
                FontSize = 15, VerticalAlignment = VerticalAlignment.Center
            };
            chevron.Click += (_, _) =>
            {
                bool show = details.Visibility != Visibility.Visible;
                details.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                chevron.Content = show ? "˅" : "˄";
                ChatScroll.ScrollToEnd();
            };
            Grid.SetColumn(chevron, 1);
            header.Children.Add(chevron);
        }
        sp.Children.Add(header);
        sp.Children.Add(details);
        border.Child = sp;
        ChatMessages.Children.Add(border);
        ChatScroll.ScrollToEnd();
    }

    private static bool IsOutsidePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        try { return System.IO.Path.IsPathRooted(path); }
        catch { return false; }
    }

    private void FillCardDetails(StackPanel details, ActionCardDto card)
    {
        switch (card.Type)
        {
            case "patch":
                if (!string.IsNullOrWhiteSpace(card.OldText))
                    details.Children.Add(CodeBlock("− было", card.OldText!, "#e94560"));
                if (!string.IsNullOrWhiteSpace(card.NewText))
                    details.Children.Add(CodeBlock("+ стало", card.NewText!, "#00d9ff"));
                break;
            case "write":
            case "create":
                if (!string.IsNullOrWhiteSpace(card.NewText))
                    details.Children.Add(CodeBlock("содержимое", card.NewText!, "#00d9ff"));
                else if (!string.IsNullOrWhiteSpace(card.Details))
                    details.Children.Add(CodeBlock("детали", card.Details, "#8fe6ff"));
                break;
            case "command":
            case "repair":
                if (!string.IsNullOrWhiteSpace(card.Command))
                    details.Children.Add(CodeBlock(
                        $"команда · {card.Shell ?? "CMD"}", card.Command!, "#8fe6ff"));
                if (!string.IsNullOrWhiteSpace(card.Details))
                    details.Children.Add(CodeBlock(
                        $"вывод · exit {card.ExitCode}", card.Details,
                        card.ExitCode.HasValue && card.ExitCode.Value != 0 ? "#e94560" : "#8fe6ff"));
                break;
            case "outside":
                if (!string.IsNullOrWhiteSpace(card.Details))
                    details.Children.Add(CodeBlock("вне проекта", card.Details, "#e94560"));
                break;
            default:
                if (!string.IsNullOrWhiteSpace(card.Details))
                    details.Children.Add(CodeBlock("детали", card.Details, "#8fe6ff"));
                break;
        }
    }

    private static UIElement CodeBlock(string header, string text, string accent)
    {
        var sp = new StackPanel { Margin = new Thickness(0, 3, 0, 3) };
        sp.Children.Add(new TextBlock
        {
            Text = header, Foreground = B(accent),
            FontFamily = Theme.Font(), FontSize = 13,
            Margin = new Thickness(2, 0, 0, 3)
        });
        var code = new TextBlock
        {
            Text = text, Foreground = B("#eaf6ff"),
            FontFamily = new FontFamily("Consolas"), FontSize = 14,
            TextWrapping = TextWrapping.Wrap
        };
        var scroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = 320,
            Content = code
        };
        var box = new Border
        {
            Background = B("#050b12"), BorderBrush = B("#12404f"),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 6, 8, 6),
            Child = scroll
        };
        sp.Children.Add(box);
        return sp;
    }

    internal static TextBlock CardText(string text, string color, int size = 15, bool bold = false)
    {
        return new TextBlock
        {
            Text = text, Foreground = B(color), FontSize = size,
            FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
            FontFamily = Theme.Font(), TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    private Button MakeButton(string text, string background, string foreground, Action onClick)
    {
        var btn = new Button
        {
            Content = text, Background = B(background), Foreground = B(foreground),
            Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(0, 0, 6, 0),
            FontSize = 15
        };
        btn.Click += (_, _) =>
        {
            btn.IsEnabled = false;
            onClick();
        };
        return btn;
    }

    private async void AnswerAndClose(Border card, object payload)
    {
        _interactiveCards.Remove(card);
        ChatMessages.Children.Remove(card);
        ChatScroll.ScrollToEnd();
        await PostApprovalAsync(payload);
    }

    private void CloseCard(Border card)
    {
        _interactiveCards.Remove(card);
        ChatMessages.Children.Remove(card);
        ChatScroll.ScrollToEnd();
    }

    // Карточка настроек режима «планирование»: выбираются раунды и число
    // вопросов в раунде (5/10/15/своё, дефолт 10), кнопка «старт» отправляет
    // /agent-run с ОБОИМИ параметрами: SendAgentRunAsync(role, text, rounds, count, count).
    private void AddPlanSettingsCard(string role, string text)
    {
        int selRounds = 1;
        int selCount = 10;

        // ОБЪЯВЛЯЕМ ДО ВСЕХ ЛЯМБД — иначе лямбды не увидят переменные (ошибка компиляции).
        var customBtn = new Button
        {
            Content = "своё",
            Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(0, 0, 6, 0),
            FontSize = 15,
            ToolTip = "своё число вопросов (1–30)"
        };
        var customBox = new TextBox
        {
            Width = 70,
            Background = B("#050b12"), Foreground = B("#eaf6ff"),
            BorderBrush = B("#12404f"), BorderThickness = new Thickness(1),
            Padding = new Thickness(6, 4, 6, 4), FontSize = 15,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "своё число вопросов (1–30)"
        };

var border = new Border
        {
            Background = B("#07141d"), BorderBrush = B("#00d9ff"),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12), Margin = new Thickness(CardLeft, 6, 0, 6)
        };
        var sp = new StackPanel();
        sp.Children.Add(CardText("📋 Настройки планирования", "#00d9ff", 16, true));
        var statusTb = CardText("раунды: 1 · вопросы в раунде: 10", "#6f96a8", 14);
        sp.Children.Add(statusTb);

        var roundsButtons = new List<Button>();
        var countButtons = new List<Button>();

        void SetSel(Button b, bool on)
        {
            b.Background = B(on ? "#12404f" : "#0a1c28");
            b.BorderBrush = B(on ? "#00d9ff" : "#12404f");
            b.Foreground = B(on ? "#00d9ff" : "#eaf6ff");
        }
        void Refresh()
        {
            for (int i = 0; i < roundsButtons.Count; i++)
                SetSel(roundsButtons[i], i == selRounds);
            int[] presets = { 5, 10, 15 };
            bool customOn = !string.IsNullOrEmpty(customBox.Text.Trim());
            for (int i = 0; i < 3; i++)
                SetSel(countButtons[i], !customOn && selCount == presets[i]);
            SetSel(countButtons[3], customOn);
            statusTb.Text = $"раунды: {selRounds} · вопросы в раунде: {selCount} · старт отправит оба параметра";
        }

        var roundsRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 8, 0, 0)
        };
        roundsRow.Children.Add(CardText("раунды вопросов: ", "#8fe6ff", 15));
        foreach (var rounds in new[] { 0, 1, 2, 3 })
        {
            int r = rounds;
            var b = new Button
            {
                Content = r.ToString(),
                Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(0, 0, 6, 0),
                FontSize = 15
            };
            b.Click += (_, _) => { selRounds = r; Refresh(); };
            roundsButtons.Add(b);
            roundsRow.Children.Add(b);
        }
        sp.Children.Add(roundsRow);

        var perRoundRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 8, 0, 0)
        };
        perRoundRow.Children.Add(CardText("вопросов в раунде: ", "#8fe6ff", 15));
        foreach (var n in new[] { 5, 10, 15 })
        {
            int c = n;
            var b = new Button
            {
                Content = n.ToString(),
                Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(0, 0, 6, 0),
                FontSize = 15
            };
            b.Click += (_, _) => { selCount = c; customBox.Text = ""; Refresh(); };
            countButtons.Add(b);
            perRoundRow.Children.Add(b);
        }
        customBtn.Click += (_, _) => customBox.Focus();
        customBox.TextChanged += (_, _) =>
        {
            if (int.TryParse(customBox.Text.Trim(), out var v) && v >= 1 && v <= 30)
            {
                selCount = v;
                Refresh();
            }
        };
        countButtons.Add(customBtn);
        perRoundRow.Children.Add(customBtn);
        perRoundRow.Children.Add(customBox);
        sp.Children.Add(perRoundRow);

        var startRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 10, 0, 0)
        };
        startRow.Children.Add(MakeButton("▶ старт", "#12404f", "#00d9ff", () =>
        {
            int rounds = selRounds;
            int count = selCount;
            var t = customBox.Text.Trim();
            if (t.Length > 0 && int.TryParse(t, out var cv))
                count = Math.Clamp(cv, 1, 30);
            CloseCard(border);
            _ = SendAgentRunAsync(role, text, rounds, count, count);
        }));
        sp.Children.Add(startRow);

        Refresh();
        border.Child = sp;
        border.Tag = statusTb;
        _interactiveCards.Add(border);
        ChatMessages.Children.Add(border);
        ChatScroll.ScrollToEnd();
    }

    // Финал планирования: «Реализовать план» переключает режим на авто
    // и запускает новую сессию с планом, «Готово» просто закрывает карточку.
    private void AddPlanDoneCard(string role, string plan)
    {
var border = new Border
        {
            Background = B("#07141d"), BorderBrush = B("#00d9ff"),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12), Margin = new Thickness(CardLeft, 6, 0, 6)
        };
        var sp = new StackPanel();
        sp.Children.Add(CardText("✅ План готов", "#00d9ff", 16, true));
        var statusTb = CardText("реализовать в авто-режиме или закрыть", "#6f96a8", 14);
        sp.Children.Add(statusTb);
        var planTb = CardText(plan, "#eaf6ff", 15);
        planTb.Margin = new Thickness(0, 8, 0, 0);
        sp.Children.Add(planTb);
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0)
        };
        buttons.Children.Add(MakeButton("Реализовать план", "#12404f", "#00d9ff", () =>
        {
            CloseCard(border);
            SetMode("auto");
            _ = SendAgentRunAsync(role, "Реализуй план:\n" + plan, null, null, null);
        }));
        buttons.Children.Add(MakeButton("Готово", "#1a0f14", "#e94560", () => CloseCard(border)));
        sp.Children.Add(buttons);
        border.Child = sp;
        border.Tag = statusTb;
        _interactiveCards.Add(border);
        ChatMessages.Children.Add(border);
        ChatScroll.ScrollToEnd();
    }

    private void AddApprovalCard(AgentRunResponse r)
    {
var border = new Border
        {
            Background = B(r.Dangerous ? "#180a10" : "#07141d"),
            BorderBrush = B(r.Dangerous ? "#e94560" : "#12404f"),
            BorderThickness = new Thickness(r.Dangerous ? 2 : 1),
            CornerRadius = new CornerRadius(8), Padding = new Thickness(12),
            Margin = new Thickness(CardLeft, 6, 0, 6)
        };
        var sp = new StackPanel();
        var title = r.Dangerous ? "⚠️ ОПАСНОЕ ДЕЙСТВИЕ" : "🛠 Подтверждение действия";
        sp.Children.Add(CardText(title, r.Dangerous ? "#e94560" : "#00d9ff", 16, true));
        var statusTb = CardText("ожидает ответа", "#6f96a8", 14);
        sp.Children.Add(statusTb);
        sp.Children.Add(CardText($"Инструмент: {r.Tool}", "#eaf6ff", 15));
        sp.Children.Add(CardText(DescribePending(r), "#8fe6ff", 15));
        if (r.StepsUsed.HasValue && r.StepLimit.HasValue)
            sp.Children.Add(CardText($"запросы: {r.StepsUsed}/{r.StepLimit}", "#8fe6ff", 13));
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0)
        };
        buttons.Children.Add(MakeButton("Разрешить", "#12404f", "#00d9ff", () =>
            AnswerAndClose(border, new
            {
                sessionId = r.SessionId, approve = true, remember = false,
                steps = 0, inputText = (string?)null
            })));
        buttons.Children.Add(MakeButton("Разрешить и запомнить", "#0a1c28", "#8fe6ff", () =>
            AnswerAndClose(border, new
            {
                sessionId = r.SessionId, approve = true, remember = true,
                steps = 0, inputText = (string?)null
            })));
        buttons.Children.Add(MakeButton("Запретить", "#1a0f14", "#e94560", () =>
            AnswerAndClose(border, new
            {
                sessionId = r.SessionId, approve = false, remember = false,
                steps = 0, inputText = (string?)null
            })));
        sp.Children.Add(buttons);
        border.Child = sp;
        border.Tag = statusTb;
        _interactiveCards.Add(border);
        ChatMessages.Children.Add(border);
        ChatScroll.ScrollToEnd();
    }

    private void AddMoreStepsCard(AgentRunResponse r)
    {
var border = new Border
        {
            Background = B("#07141d"), BorderBrush = B("#ffb14a"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8), Padding = new Thickness(12),
            Margin = new Thickness(CardLeft, 6, 0, 6)
        };
        var sp = new StackPanel();
        sp.Children.Add(CardText("⏱ Лимит запросов достигнут", "#ffb14a", 16, true));
        var statusTb = CardText("ожидает ответа", "#6f96a8", 14);
        sp.Children.Add(statusTb);
        sp.Children.Add(CardText($"Использовано: {r.StepsUsed ?? 0}/{r.StepLimit ?? 30}", "#eaf6ff", 15));
        if (!string.IsNullOrWhiteSpace(r.Reason))
            sp.Children.Add(CardText("Причина: " + r.Reason, "#8fe6ff", 15));
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0)
        };
        buttons.Children.Add(MakeButton("+10 запросов", "#12404f", "#00d9ff", () =>
            AnswerAndClose(border, new
            {
                sessionId = r.SessionId, approve = true, remember = false,
                steps = 10, inputText = (string?)null
            })));
        buttons.Children.Add(MakeButton("+20 запросов", "#12404f", "#00d9ff", () =>
            AnswerAndClose(border, new
            {
                sessionId = r.SessionId, approve = true, remember = false,
                steps = 20, inputText = (string?)null
            })));
        buttons.Children.Add(MakeButton("Стоп", "#1a0f14", "#e94560", () =>
            AnswerAndClose(border, new
            {
                sessionId = r.SessionId, approve = false, remember = false,
                steps = 0, inputText = (string?)null
            })));
        sp.Children.Add(buttons);
        border.Child = sp;
        border.Tag = statusTb;
        _interactiveCards.Add(border);
        ChatMessages.Children.Add(border);
        ChatScroll.ScrollToEnd();
    }

    // [LERON UPDATE] AddUserInputCard: all questions shown at once
private void AddUserInputCard(AgentRunResponse r)
{
    var border = new Border
    {
        Background = B("#07141d"),
        BorderBrush = B("#12404f"),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(10),
        Padding = new Thickness(14),
        Margin = new Thickness(CardLeft, 8, 0, 8)
    };

    var sp = new StackPanel();
    border.Child = sp;

    bool hasMany = r.Questions != null && r.Questions.Count > 1;
    sp.Children.Add(CardText(hasMany ? "❓ Вопросы пользователю" : "❓ Вопрос пользователю", "#00d9ff", 16, true));
    sp.Children.Add(CardText("Ответь на все пункты и нажми «Ответить». Карточка закроется только после отправки.", "#6f96a8", 12));

    if (r.StepsUsed.HasValue && r.StepLimit.HasValue)
        sp.Children.Add(CardText($"Использовано: {r.StepsUsed.Value}/{r.StepLimit.Value}", "#eaf6ff", 13));

    if (!string.IsNullOrWhiteSpace(r.Question))
        sp.Children.Add(CardText(r.Question, "#eaf6ff", 14));

    var inputs = new System.Collections.Generic.List<System.Tuple<string, string, TextBox>>();

    if (r.Questions != null && r.Questions.Count > 0)
    {
        int qi = 1;

        foreach (var q in r.Questions)
        {
            if (q == null) continue;

            var qId = string.IsNullOrWhiteSpace(q.Id) ? $"q{qi}" : q.Id;
            var qText = q.Text ?? "";

            sp.Children.Add(new TextBlock
            {
                Text = $"[{qId}] {qText}",
                Foreground = B("#eaf6ff"),
                FontSize = 14,
                FontWeight = System.Windows.FontWeights.SemiBold,
                TextWrapping = System.Windows.TextWrapping.Wrap,
                Margin = new Thickness(0, 10, 0, 4)
            });

            var tb = new TextBox
            {
                MinHeight = 32,
                Background = B("#0a1620"),
                Foreground = B("#eaf6ff"),
                BorderBrush = B("#12404f"),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(8, 6, 8, 6),
                TextWrapping = System.Windows.TextWrapping.Wrap,
                AcceptsReturn = true,
                Tag = qId
            };

            if (q.Options != null && q.Options.Count > 0)
            {
                var optionsPanel = new System.Windows.Controls.WrapPanel
                {
                    Orientation = System.Windows.Controls.Orientation.Horizontal,
                    Margin = new Thickness(0, 4, 0, 4)
                };

                foreach (var opt in q.Options)
                {
                    if (string.IsNullOrWhiteSpace(opt)) continue;

                    var optBtn = MakeButton(opt, "#12404f", "#8fe6ff", () => { tb.Text = opt; });
                    optBtn.Margin = new Thickness(0, 0, 6, 6);
                    optionsPanel.Children.Add(optBtn);
                }

                sp.Children.Add(optionsPanel);
            }

            sp.Children.Add(tb);
            inputs.Add(System.Tuple.Create(qId, qText, tb));
            qi++;
        }
    }
    else
    {
        var tb = new TextBox
        {
            MinHeight = 34,
            Background = B("#0a1620"),
            Foreground = B("#eaf6ff"),
            BorderBrush = B("#12404f"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 6, 8, 6),
            TextWrapping = System.Windows.TextWrapping.Wrap,
            AcceptsReturn = true
        };

        sp.Children.Add(tb);
        inputs.Add(System.Tuple.Create("answer", r.Question ?? "", tb));
    }

    var buttons = new StackPanel
    {
        Orientation = System.Windows.Controls.Orientation.Horizontal,
        Margin = new Thickness(0, 12, 0, 0)
    };

    var sendBtn = MakeButton("✅ Ответить на все", "#12404f", "#00d9ff", () =>
    {
        var sbAns = new System.Text.StringBuilder();
        var answers = new System.Collections.Generic.Dictionary<string, string>();

        sbAns.AppendLine("Ответы пользователя:");

        foreach (var inp in inputs)
        {
            var val = inp.Item3 != null ? (inp.Item3.Text ?? "").Trim() : "";
            if (string.IsNullOrWhiteSpace(val)) val = "без ответа";

            answers[inp.Item1] = val;
            sbAns.AppendLine($"{inp.Item1}: {val}");
        }

        AnswerAndClose(border, new
        {
            sessionId = r.SessionId,
            approve = true,
            remember = false,
            inputText = sbAns.ToString(),
            answers = answers,
            source = "user_input_card"
        });
    });

    buttons.Children.Add(sendBtn);
    sp.Children.Add(buttons);

    _interactiveCards.Add(border);
    ChatMessages.Children.Add(border);
    ChatScroll.ScrollToEnd();
}


    private void AddOutsideCard(AgentRunResponse r)
    {
var border = new Border
        {
            Background = B("#180a10"), BorderBrush = B("#e94560"),
            BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12), Margin = new Thickness(CardLeft, 6, 0, 6)
        };
        var sp = new StackPanel();
        sp.Children.Add(CardText("🚨 ВНЕ ПРОЕКТА — запрос доступа", "#e94560", 16, true));
        var statusTb = CardText("ожидает ответа", "#6f96a8", 14);
        sp.Children.Add(statusTb);
        sp.Children.Add(CardText($"Путь: {r.Path}", "#eaf6ff", 15));
        sp.Children.Add(CardText($"Причина: {r.Reason}", "#8fe6ff", 15));
        sp.Children.Add(CardText($"Действия: {r.RequestedActions}", "#8fe6ff", 15));
        if (r.StepsUsed.HasValue && r.StepLimit.HasValue)
            sp.Children.Add(CardText($"запросы: {r.StepsUsed}/{r.StepLimit}", "#8fe6ff", 13));
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0)
        };
        buttons.Children.Add(MakeButton("Разрешить доступ", "#12404f", "#00d9ff", () =>
            AnswerAndClose(border, new
            {
                sessionId = r.SessionId, approve = true, remember = false,
                steps = 0, inputText = (string?)null
            })));
        buttons.Children.Add(MakeButton("Запретить", "#1a0f14", "#e94560", () =>
            AnswerAndClose(border, new
            {
                sessionId = r.SessionId, approve = false, remember = false,
                steps = 0, inputText = (string?)null
            })));
        sp.Children.Add(buttons);
        border.Child = sp;
        border.Tag = statusTb;
        _interactiveCards.Add(border);
        ChatMessages.Children.Add(border);
        ChatScroll.ScrollToEnd();
    }

    private static string DescribePending(AgentRunResponse r)
    {
        try
        {
            using var doc = JsonDocument.Parse(r.Arguments ?? "{}");
            var root = doc.RootElement;
            string Get(string name) =>
                root.TryGetProperty(name, out var v) ? v.ToString() : "";
            switch (r.Tool)
            {
                case "read_file": return $"Путь: {Get("path")}";
                case "read_files": return $"Пути: {Get("paths")}";
                case "list_files": return $"Папка: {Get("path")}";
                case "grep": return $"Паттерны: {Get("patterns")} · Путь: {Get("path")}";
                case "write_file": return $"Путь: {Get("path")}";
                case "write_files": return $"Пакетная запись нескольких файлов";
                case "patch_file":
                case "edit_file": return $"Путь: {Get("path")}";
                case "rename_file": return $"{Get("path")} → {Get("new_path")}";
                case "delete_file": return $"Путь: {Get("path")}";
                case "create_directory": return $"Папка: {Get("path")}";
                case "run_command": return $"Команда: {Get("command")}";
                default: return r.Arguments ?? "";
            }
        }
        catch { return r.Arguments ?? ""; }
    }
}
