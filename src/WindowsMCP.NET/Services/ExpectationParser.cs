using System.Text.Json;
using WindowsMcpNet.Models;
using WindowsMcpNet.Tools;

namespace WindowsMcpNet.Services;

/// <summary>
/// Turns the raw <c>conditions</c> JSON of the Expect tool into typed conditions. Pure. Everything it
/// does not understand is rejected with an <see cref="ArgumentException"/> naming the 1-based index
/// of the condition — a condition is never guessed at or silently dropped, because the answer to a
/// misread condition would look like an answer to the one the caller meant.
/// </summary>
public static class ExpectationParser
{
    public const int MaxConditions = 20;

    private static readonly string[] WindowKeys = ["window", "state", "modal", "match"];
    private static readonly string[] ElementKeys = ["element", "type", "name", "panel", "in_window", "state", "value", "value_contains", "match"];
    private static readonly string[] TextKeys = ["text"];
    private static readonly string[] SelectorKeys = ["type", "name", "panel", "in_window", "match"];

    public static IReadOnlyList<Expectation> Parse(JsonElement conditions)
    {
        if (conditions.ValueKind != JsonValueKind.Array)
            throw new ArgumentException("conditions: must be an array of condition objects");

        var count = conditions.GetArrayLength();
        if (count == 0)
            throw new ArgumentException("conditions: at least one condition is required");
        if (count > MaxConditions)
            throw new ArgumentException($"conditions: at most {MaxConditions} conditions per call (got {count})");

        var parsed = new List<Expectation>(count);
        var index = 0;
        foreach (var condition in conditions.EnumerateArray())
        {
            index++;
            try
            {
                parsed.Add(ParseCondition(condition, index));
            }
            catch (FormatException ex)
            {
                throw new ArgumentException($"condition {index}: {ex.Message}");
            }
        }

        return parsed;
    }

    // Problems inside one condition are raised as FormatException and get their "condition N: "
    // prefix in one place (Parse).
    private static Expectation ParseCondition(JsonElement c, int index)
    {
        if (c.ValueKind != JsonValueKind.Object)
            throw new FormatException("must be an object");

        var isWindow = c.TryGetProperty("window", out _);
        var isText = c.TryGetProperty("text", out _);
        var isElement = c.TryGetProperty("element", out _) || c.TryGetProperty("type", out _) || c.TryGetProperty("name", out _)
            || c.TryGetProperty("panel", out _);

        var kinds = (isWindow ? 1 : 0) + (isText ? 1 : 0) + (isElement ? 1 : 0);
        if (kinds == 0)
            throw new FormatException("needs one of window, element, type, name, panel, text");
        if (kinds > 1)
            throw new FormatException("only one of window, element (or type/name/panel), text per condition");

        if (isWindow)
            return ParseWindow(c, index);
        if (isText)
            return ParseText(c, index);
        return ParseElement(c, index);
    }

    private static WindowExpectation ParseWindow(JsonElement c, int index)
    {
        RejectUnknownKeys(c, WindowKeys);

        var title = RequiredText(c, "window");
        var state = EnumOr(c, "state", ExpectWindowState.Open);
        var modal = Bool(c, "modal");
        if (modal is not null && state != ExpectWindowState.Open)
            throw new FormatException("modal needs state=open");

        return new WindowExpectation(index, title, state, modal, EnumOr(c, "match", ExpectMatch.Exact));
    }

    private static TextExpectation ParseText(JsonElement c, int index)
    {
        RejectUnknownKeys(c, TextKeys);
        return new TextExpectation(index, RequiredText(c, "text"));
    }

    private static ElementExpectation ParseElement(JsonElement c, int index)
    {
        RejectUnknownKeys(c, ElementKeys);

        var id = OptionalText(c, "element");
        if (id is not null)
        {
            foreach (var selectorKey in SelectorKeys)
            {
                if (c.TryGetProperty(selectorKey, out _))
                    throw new FormatException($"element cannot be combined with {selectorKey}");
            }
        }

        var state = EnumOr(c, "state", ExpectState.Exists);
        var value = OptionalText(c, "value", allowEmpty: true);
        var valueContains = OptionalText(c, "value_contains");
        if (value is not null && valueContains is not null)
            throw new FormatException("value and value_contains cannot be combined");
        if (state == ExpectState.Absent && (value ?? valueContains) is not null)
            throw new FormatException($"{(value is not null ? "value" : "value_contains")} cannot be combined with state=absent");

        return new ElementExpectation(
            index, id, ElementType(c), OptionalText(c, "name"), OptionalText(c, "panel"), OptionalText(c, "in_window"),
            state, value, valueContains, EnumOr(c, "match", ExpectMatch.Exact));
    }

    private static void RejectUnknownKeys(JsonElement c, string[] allowed)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in c.EnumerateObject())
        {
            if (!allowed.Contains(property.Name, StringComparer.Ordinal))
                throw new FormatException($"unknown key '{property.Name}' (allowed: {string.Join(", ", allowed)})");
            // JSON allows a key twice and the reader would silently take the last one.
            if (!seen.Add(property.Name))
                throw new FormatException($"duplicate key '{property.Name}'");
        }
    }

    /// <summary>An observation only lists the actionable control types: a condition on any other type
    /// could never match, so "absent" would always pass and "exists" always fail. Returns the
    /// canonical spelling.</summary>
    private static string? ElementType(JsonElement c)
    {
        if (OptionalText(c, "type") is not { } given)
            return null;

        foreach (var known in ObservationBuilder.ActionableTypes)
        {
            if (string.Equals(known, given, StringComparison.OrdinalIgnoreCase))
                return known;
        }

        var hint = string.Equals(given, "Text", StringComparison.OrdinalIgnoreCase)
            ? """ — for static text use {"text":"..."}"""
            : "";
        throw new FormatException(
            $"unknown type '{given}' (allowed: {string.Join(", ", ObservationBuilder.ActionableTypes.Order(StringComparer.Ordinal))}){hint}");
    }

    private static string RequiredText(JsonElement c, string key) =>
        OptionalText(c, key) ?? throw new FormatException($"{key} is required");

    private static string? OptionalText(JsonElement c, string key, bool allowEmpty = false)
    {
        if (!c.TryGetProperty(key, out var v))
            return null;
        if (v.ValueKind != JsonValueKind.String)
            throw new FormatException($"{key} must be a string");

        var text = v.GetString()!;
        // Blank counts as empty: a blank contains-match would fit nearly everything.
        if (!allowEmpty && string.IsNullOrWhiteSpace(text))
            throw new FormatException($"{key} must not be empty");
        return text;
    }

    private static bool? Bool(JsonElement c, string key)
    {
        if (!c.TryGetProperty(key, out var v))
            return null;
        return v.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new FormatException($"{key} must be true or false"),
        };
    }

    /// <summary>Reads an enum by its snake_case name, case-insensitively; an unknown value's message
    /// lists the allowed ones, so the caller does not have to look them up.</summary>
    private static TEnum EnumOr<TEnum>(JsonElement c, string key, TEnum fallback) where TEnum : struct, Enum
    {
        if (!c.TryGetProperty(key, out var v))
            return fallback;
        if (v.ValueKind != JsonValueKind.String)
            throw new FormatException($"{key} must be a string");

        var text = v.GetString()!;
        var values = Enum.GetValues<TEnum>();
        foreach (var value in values)
        {
            if (string.Equals(SnakeName(value), text, StringComparison.OrdinalIgnoreCase))
                return value;
        }

        throw new FormatException($"unknown {key} '{text}' (allowed: {string.Join(", ", values.Select(SnakeName))})");
    }

    internal static string SnakeName<TEnum>(TEnum value) where TEnum : struct, Enum =>
        JsonNamingPolicy.SnakeCaseLower.ConvertName(value.ToString());
}
