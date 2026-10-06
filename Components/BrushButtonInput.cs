namespace RouteFilter.Components;

internal readonly struct BrushButtonInput
{
    internal readonly bool ApplyPressed, ClearPressed;
    private readonly bool m_ApplyReleased, m_ClearReleased;
    internal BrushButtonInput(bool applyPressed, bool clearPressed, bool applyReleased, bool clearReleased)
    {
        ApplyPressed = applyPressed;
        ClearPressed = clearPressed;
        m_ApplyReleased = applyReleased;
        m_ClearReleased = clearReleased;
    }
    internal bool Pressed => ApplyPressed || ClearPressed;
    internal bool Released(bool clear) => clear ? m_ClearReleased : m_ApplyReleased;
}
