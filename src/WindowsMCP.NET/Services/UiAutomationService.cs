using FlaUI.Core.AutomationElements;
using FlaUI.Core.Conditions;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using FuzzySharp;
using WindowsMcpNet.Models;

namespace WindowsMcpNet.Services;

public sealed class UiAutomationService : IDisposable
{
    private readonly ILogger<UiAutomationService> _logger;
    private readonly UIA3Automation _automation;

    public UiAutomationService(ILogger<UiAutomationService> logger)
    {
        _logger = logger;
        _automation = new UIA3Automation();
    }

    public List<UiElementNode> GetDesktopTree(int maxDepth = 5)
    {
        var desktop = _automation.GetDesktop();
        var children = desktop.FindAllChildren();
        return children.Select(c => BuildNode(c, 0, maxDepth)).ToList();
    }

    public AutomationElement? FindElementByName(string name, int minScore = 60)
    {
        var desktop = _automation.GetDesktop();
        var allElements = desktop.FindAllDescendants();

        AutomationElement? best = null;
        int bestScore = 0;

        foreach (var el in allElements)
        {
            try
            {
                var elName = el.Name;
                if (string.IsNullOrEmpty(elName)) continue;

                var score = Fuzz.PartialRatio(name.ToLowerInvariant(), elName.ToLowerInvariant());
                if (score > bestScore)
                {
                    bestScore = score;
                    best = el;
                }
            }
            catch
            {
                // Some elements throw when accessing properties — skip them.
            }
        }

        return bestScore >= minScore ? best : null;
    }

    public List<AutomationElement> FindElements(
        string? automationId = null,
        string? name = null,
        ControlType? controlType = null)
    {
        var desktop = _automation.GetDesktop();
        var conditions = new List<ConditionBase>();
        var cf = _automation.ConditionFactory;

        if (automationId is not null) conditions.Add(cf.ByAutomationId(automationId));
        if (name is not null) conditions.Add(cf.ByName(name));
        if (controlType.HasValue) conditions.Add(cf.ByControlType(controlType.Value));

        var combined = conditions.Count switch
        {
            0 => TrueCondition.Default,
            1 => conditions[0],
            _ => new AndCondition(conditions.ToArray())
        };

        return desktop.FindAllDescendants(combined).ToList();
    }

    public (int X, int Y)? GetClickablePoint(AutomationElement element)
    {
        try
        {
            if (element.TryGetClickablePoint(out var point))
                return ((int)point.X, (int)point.Y);

            var rect = element.BoundingRectangle;
            if (!rect.IsEmpty)
                return ((int)(rect.X + rect.Width / 2), (int)(rect.Y + rect.Height / 2));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get clickable point");
        }
        return null;
    }

    private static UiElementNode BuildNode(AutomationElement element, int depth, int maxDepth)
    {
        // Property access can throw PropertyNotSupportedException for some system elements.
        // Wrap each access individually so one bad property doesn't skip the whole node.
        string name = "";
        string controlType = "Unknown";
        string? automationId = null;
        int x = 0, y = 0, width = 0, height = 0;
        bool isInteractive = false;

        try { name = element.Name ?? ""; } catch { }
        try { controlType = element.ControlType.ToString(); } catch { }
        try { automationId = element.AutomationId; } catch { }
        try
        {
            var rect = element.BoundingRectangle;
            x = (int)rect.X;
            y = (int)rect.Y;
            width = (int)rect.Width;
            height = (int)rect.Height;
        }
        catch { }
        try { isInteractive = IsInteractiveType(element.ControlType); } catch { }

        var node = new UiElementNode
        {
            Name = name,
            ControlType = controlType,
            AutomationId = automationId,
            X = x,
            Y = y,
            Width = width,
            Height = height,
            IsInteractive = isInteractive,
        };

        if (depth < maxDepth)
        {
            try
            {
                var children = element.FindAllChildren();
                node.Children.AddRange(children.Select(c => BuildNode(c, depth + 1, maxDepth)));
            }
            catch { }
        }

        return node;
    }

    private static bool IsInteractiveType(ControlType type) =>
        type == ControlType.Button ||
        type == ControlType.CheckBox ||
        type == ControlType.ComboBox ||
        type == ControlType.Edit ||
        type == ControlType.Hyperlink ||
        type == ControlType.ListItem ||
        type == ControlType.MenuItem ||
        type == ControlType.RadioButton ||
        type == ControlType.Slider ||
        type == ControlType.Tab ||
        type == ControlType.TabItem ||
        type == ControlType.TreeItem;

    public void Dispose() => _automation.Dispose();
}
