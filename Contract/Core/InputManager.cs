using Microsoft.Xna.Framework.Input;
namespace Contract.Core;

public static class InputManager
{
    private static KeyboardState _prev;
    private static KeyboardState _cur;

    public static void Update()
    {
        _prev = _cur;
        _cur = Keyboard.GetState();
    }

    public static bool IsDown(Keys k) => _cur.IsKeyDown(k);
    public static bool IsPressed(Keys k) => _cur.IsKeyDown(k) && !_prev.IsKeyDown(k);
    public static bool IsReleased(Keys k) => !_cur.IsKeyDown(k) && _prev.IsKeyDown(k);

    // Jump mapping: C is a primary jump key but still can be used as Confirm elsewhere.
    public static bool IsJumpDown() => IsDown(Keys.C);
    public static bool IsJumpPressed() => IsPressed(Keys.C);
    public static bool IsJumpReleased() => IsReleased(Keys.C);

    // Confirm mapping (C)
    public static bool IsConfirmPressed() => IsPressed(Keys.C);
    public static bool IsConfirmDown() => IsDown(Keys.C);

    // Cancel
    public static bool IsCancelPressed() => IsPressed(Keys.Enter);
    public static bool IsCancelDown() => IsDown(Keys.Enter);
}
