using System.Drawing;
using WindowsMcpNet.Native;

namespace WindowsMcpNet.Services;

/// <summary>
/// Production <see cref="IInputDriver"/>: wraps <see cref="User32.SetCursorPos"/> and
/// <see cref="InputFactory"/> exactly as <c>InputTools.Click</c>/<c>InputTools.Type</c> do today, so
/// <see cref="ActionExecutor"/>'s mouse/keyboard fallback sends the identical input sequences.
/// </summary>
public sealed class InputDriver : IInputDriver
{
    /// <summary>A desktop that takes injected input has a foreground window; a disconnected or
    /// non-rendered session has none.</summary>
    public bool HasInteractiveDesktop => User32.GetForegroundWindow() != nint.Zero;

    public void LeftClick(Point p) => InputFactory.LeftClickAt(p.X, p.Y);

    public void TypeText(string text, bool clear, bool pressEnter)
    {
        if (clear)
            InputFactory.SelectAllAndDelete();

        InputFactory.Send(InputFactory.BuildTextInputs(text));

        if (pressEnter)
            InputFactory.PressEnter();
    }

    public void PressEnter() => InputFactory.PressEnter();
}
