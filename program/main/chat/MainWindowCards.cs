using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MainApp;

// Карточки действий агента: компактная строка + стрелка ˄, по клику — diff/код/вывод.
public partial class MainWindow
{
    private const double CardLeft = 50;

    internal void RenderCards(List<ActionCardDto>? cards)
    {
        if (cards == null) return;
        foreach (var card in cards)
            AddActionCard(card);
    }

    private void AddActionCard(ActionCardDto card)
    {
        var borderColor = "#123626";
        if (card.Type == "error") borderColor = "#e94560";
        else if (card.Type == "outside") borderColor = "#e94560";
        else if (card.Type == "repair") borderColor = "#ffb14a";
        else if (card.Type == "summary")
            borderColor = card.Status == "failed" ? "#e94560" : "#00ff88";
        else if (card.ExitCode.HasValue && card.ExitCode.Value != 0) borderColor = "#e94560";

        var border = new Border
        {
            Background = B("#0a1a12"), BorderBrush = B(borderColor),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 7, 10, 7),
            Margin = new Thickness(CardLeft, 6, 0, 6)
        };

        var sp = new StackPanel();
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var left = new StackPanel { Orientation = Orientation.Horizontal };
        left.Children.Add(CardText(card.Icon + " ", "#00ff88", 13, true));
        left.Children.Add(CardText(card.Title, "#d9ffe7", 13, true));
        if (!string.IsNullOrWhiteSpace(card.Status))
            left.Children.Add(CardText(" · " + card.Status, "#7dffa8", 12));

        var sub = !string.IsNullOrWhiteSpace(card.Path) ? card.Path
            : !string.IsNullOrWhiteSpace(card.Command) ? card.Command : "";
        if (!string.IsNullOrWhiteSpace(sub))
            left.Children.Add(CardText("  " + sub, "#447a5a", 12));

        Grid.SetColumn(left, 0);
        header.Children.Add(left);

        var details = new StackPanel
        {
            Margin = new Thickness(0, 6, 0, 0),
            Visibility = Visibility.Collapsed
        };
        FillCardDetails(details, card);
        if (card.Backup)
            details.Children.Add(CardText("💾 создан бэкап", "#7dffa8", 12));

        if (details.Children.Count > 0)
        {
            var chevron = new Button
            {
                Content = "˄", Width = 28, Height = 22,
                Padding = new Thickness(0),
                Background = B("#0a1a12"), BorderBrush = B("#0a1a12"),
                Foreground = B("#447a5a"), FontFamily = Theme.Font(),
                FontSize = 13, VerticalAlignment = VerticalAlignment.Center
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

    private void FillCardDetails(StackPanel details, ActionCardDto card)
    {
        switch (card.Type)
        {
            case "patch":
                if (!string.IsNullOrWhiteSpace(card.OldText))
                    details.Children.Add(CodeBlock("− было", card.OldText!, "#e94560"));
                if (!string.IsNullOrWhiteSpace(card.NewText))
                    details.Children.Add(CodeBlock("+ стало", card.NewText!, "#00ff88"));
                break;

            case "write":
            case "create":
                if (!string.IsNullOrWhiteSpace(card.NewText))
                    details.Children.Add(CodeBlock("содержимое", card.NewText!, "#00ff88"));
                else if (!string.IsNullOrWhiteSpace(card.Details))
                    details.Children.Add(CodeBlock("детали", card.Details, "#9fe8bb"));
                break;

            case "command":
            case "repair":
                if (!string.IsNullOrWhiteSpace(card.Command))
                    details.Children.Add(CodeBlock(
                        $"команда · {card.Shell ?? "CMD"}", card.Command!, "#7dffa8"));
                if (!string.IsNullOrWhiteSpace(card.Details))
                    details.Children.Add(CodeBlock(
                        $"вывод · exit {card.ExitCode}", card.Details,
                        card.ExitCode.HasValue && card.ExitCode.Value != 0 ? "#e94560" : "#9fe8bb"));
                break;

            default:
                if (!string.IsNullOrWhiteSpace(card.Details))
                    details.Children.Add(CodeBlock("детали", card.Details, "#9fe8bb"));
                break;
        }
    }

    private static UIElement CodeBlock(string header, string text, string accent)
    {
        var sp = new StackPanel { Margin = new Thickness(0, 3, 0, 3) };
        sp.Children.Add(new TextBlock
        {
            Text = header, Foreground = B(accent),
            FontFamily = Theme.Font(), FontSize = 11,
            Margin = new Thickness(2, 0, 0, 3)
        });
        var code = new TextBlock
        {
            Text = text, Foreground = B("#d9ffe7"),
            FontFamily = new FontFamily("Consolas"), FontSize = 12.5,
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
            Background = B("#04110c"), BorderBrush = B("#123626"),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 6, 8, 6),
            Child = scroll
        };
        sp.Children.Add(box);
        return sp;
    }

    internal static TextBlock CardText(string text, string color, int size = 13, bool bold = false)
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
            Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(0, 0, 6, 0)
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

    private void AddApprovalCard(AgentRunResponse r)
    {
        var border = new Border
        {
            Background = B(r.Dangerous ? "#180a10" : "#0a1a12"),
            BorderBrush = B(r.Dangerous ? "#e94560" : "#1d5c3d"),
            BorderThickness = new Thickness(r.Dangerous ? 2 : 1),
            CornerRadius = new CornerRadius(8), Padding = new Thickness(10),
            Margin = new Thickness(CardLeft, 6, 0, 6)
        };

        var sp = new StackPanel();
        var title = r.Dangerous ? "⚠️ ОПАСНОЕ ДЕЙСТВИЕ" : "🛠 Подтверждение действия";
        sp.Children.Add(CardText(title, r.Dangerous ? "#e94560" : "#00ff88", 14, true));
        var statusTb = CardText("ожидает ответа", "#447a5a", 12);
        sp.Children.Add(statusTb);
        sp.Children.Add(CardText($"Инструмент: {r.Tool}", "#d9ffe7", 13));
        sp.Children.Add(CardText(DescribePending(r), "#9fe8bb", 13));

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0)
        };
        buttons.Children.Add(MakeButton("Разрешить", "#123626", "#00ff88", () =>
            AnswerAndClose(border, new
            {
                sessionId = r.SessionId, approve = true, remember = false,
                steps = 0, inputText = (string?)null
            })));
        buttons.Children.Add(MakeButton("Разрешить и запомнить", "#0d2418", "#7dffa8", () =>
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
            Background = B("#0a1a12"), BorderBrush = B("#1d5c3d"),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10), Margin = new Thickness(CardLeft, 6, 0, 6)
        };

        var sp = new StackPanel();
        sp.Children.Add(CardText("⏱ ИИ просит дополнительные шаги", "#00ff88", 14, true));
        var statusTb = CardText("ожидает ответа", "#447a5a", 12);
        sp.Children.Add(statusTb);
        sp.Children.Add(CardText($"Просит: +{r.RequestedCount}", "#d9ffe7", 13));
        if (!string.IsNullOrWhiteSpace(r.Reason))
            sp.Children.Add(CardText("Причина: " + r.Reason, "#9fe8bb", 13));

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0)
        };
        buttons.Children.Add(MakeButton("+4", "#123626", "#00ff88", () =>
            AnswerAndClose(border, new
            {
                sessionId = r.SessionId, approve = true, remember = false,
                steps = 4, inputText = (string?)null
            })));
        buttons.Children.Add(MakeButton("+8", "#123626", "#00ff88", () =>
            AnswerAndClose(border, new
            {
                sessionId = r.SessionId, approve = true, remember = false,
                steps = 8, inputText = (string?)null
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

    private void AddUserInputCard(AgentRunResponse r)
    {
        var border = new Border
        {
            Background = B("#0a1a12"), BorderBrush = B("#1d5c3d"),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10), Margin = new Thickness(CardLeft, 6, 0, 6)
        };

        var sp = new StackPanel();
        sp.Children.Add(CardText("❓ Вопрос пользователю", "#00ff88", 14, true));
        var statusTb = CardText("ожидает ответа", "#447a5a", 12);
        sp.Children.Add(statusTb);
        sp.Children.Add(CardText(r.Question ?? "Нужна дополнительная информация.", "#d9ffe7", 13));

        var box = new TextBox
        {
            Background = B("#04110c"), Foreground = B("#d9ffe7"),
            BorderBrush = B("#1d5c3d"), BorderThickness = new Thickness(1),
            Padding = new Thickness(8), FontFamily = Theme.Font(), FontSize = 14,
            Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0)
        };
        buttons.Children.Add(MakeButton("Ответить", "#123626", "#00ff88", () =>
            AnswerAndClose(border, new
            {
                sessionId = r.SessionId, approve = true, remember = false,
                steps = 0, inputText = box.Text.Trim()
            })));
        buttons.Children.Add(MakeButton("Пропустить", "#1a0f14", "#e94560", () =>
            AnswerAndClose(border, new
            {
                sessionId = r.SessionId, approve = false, remember = false,
                steps = 0, inputText = (string?)null
            })));

        sp.Children.Add(box);
        sp.Children.Add(buttons);
        border.Child = sp;
        border.Tag = statusTb;
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
            Padding = new Thickness(10), Margin = new Thickness(CardLeft, 6, 0, 6)
        };

        var sp = new StackPanel();
        sp.Children.Add(CardText("🚨 ВНЕ ПРОЕКТА — запрос доступа", "#e94560", 14, true));
        var statusTb = CardText("ожидает ответа", "#447a5a", 12);
        sp.Children.Add(statusTb);
        sp.Children.Add(CardText($"Путь: {r.Path}", "#d9ffe7", 13));
        sp.Children.Add(CardText($"Причина: {r.Reason}", "#9fe8bb", 13));
        sp.Children.Add(CardText($"Действия: {r.RequestedActions}", "#9fe8bb", 13));

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0)
        };
        buttons.Children.Add(MakeButton("Разрешить доступ", "#123626", "#00ff88", () =>
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
                case "list_files": return $"Папка: {Get("path")}";
                case "grep": return $"Паттерн: {Get("pattern")} · Путь: {Get("path")}";
                case "write_file": return $"Путь: {Get("path")}";
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