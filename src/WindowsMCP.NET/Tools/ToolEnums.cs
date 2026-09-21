using System.Text.Json;
using System.Text.Json.Serialization;

namespace WindowsMcpNet.Tools;

/// <summary>
/// Serialises enum members as lower snake_case strings ("read_base64", "ui_tree") and reads them
/// case-insensitively. Because the converter is attached to the enum type, the MCP input schema
/// lists exactly these values, so clients validate up front instead of the tool rejecting a typo.
/// </summary>
public sealed class SnakeCaseEnumConverter<TEnum> : JsonStringEnumConverter<TEnum> where TEnum : struct, Enum
{
    public SnakeCaseEnumConverter() : base(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false) { }
}

[JsonConverter(typeof(SnakeCaseEnumConverter<OutputFormat>))]
public enum OutputFormat { Markdown, Json }

[JsonConverter(typeof(SnakeCaseEnumConverter<MouseButton>))]
public enum MouseButton { Left, Right, Middle }

[JsonConverter(typeof(SnakeCaseEnumConverter<ScrollDirection>))]
public enum ScrollDirection { Up, Down, Left, Right }

[JsonConverter(typeof(SnakeCaseEnumConverter<ScrollAxis>))]
public enum ScrollAxis { Vertical, Horizontal }

[JsonConverter(typeof(SnakeCaseEnumConverter<ClipboardMode>))]
public enum ClipboardMode { Get, Set }

[JsonConverter(typeof(SnakeCaseEnumConverter<FileSystemMode>))]
public enum FileSystemMode { Read, Write, ReadBase64, WriteBase64, Copy, Move, Delete, List, Search, Info }

[JsonConverter(typeof(SnakeCaseEnumConverter<ProcessMode>))]
public enum ProcessMode { List, Kill }

[JsonConverter(typeof(SnakeCaseEnumConverter<ProcessSort>))]
public enum ProcessSort { Memory, Cpu, Name, Pid }

[JsonConverter(typeof(SnakeCaseEnumConverter<RegistryMode>))]
public enum RegistryMode { Get, Set, Delete, List }

/// <summary>Registry value kinds keep their Win32 spelling (DWord, QWord, ExpandString); reading is case-insensitive.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<RegistryValueType>))]
public enum RegistryValueType { String, DWord, QWord, Binary, ExpandString }

[JsonConverter(typeof(SnakeCaseEnumConverter<AppMode>))]
public enum AppMode { Launch, Ensure, Status, Switch, Resize }

[JsonConverter(typeof(SnakeCaseEnumConverter<AmbiguousPolicy>))]
public enum AmbiguousPolicy { First, Error }

[JsonConverter(typeof(SnakeCaseEnumConverter<ContextModule>))]
public enum ContextModule { Window, Screen, UiTree, Clipboard, Processes }
