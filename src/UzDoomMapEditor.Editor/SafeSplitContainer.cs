namespace UzDoomMapEditor.Editor;

/// <summary>
/// WinForms validates SplitterDistance against the control's current size.
/// MainForm configures its desired distance before the split container has been
/// parented and laid out, so the stock control can throw during startup.
/// This tiny wrapper defers an initially-too-large distance until layout gives
/// the control its real size.
/// </summary>
public sealed class SplitContainer : System.Windows.Forms.SplitContainer
{
    private int? _deferredSplitterDistance;

    public new int SplitterDistance
    {
        get => _deferredSplitterDistance ?? base.SplitterDistance;
        set
        {
            if (CanApply(value))
            {
                base.SplitterDistance = value;
                _deferredSplitterDistance = null;
            }
            else
            {
                _deferredSplitterDistance = value;
            }
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyDeferredDistance();
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        ApplyDeferredDistance();
    }

    private bool CanApply(int value)
    {
        var extent = Orientation == Orientation.Vertical ? ClientSize.Width : ClientSize.Height;
        var maximum = extent - Panel2MinSize - SplitterWidth;
        return value >= Panel1MinSize && value <= maximum;
    }

    private void ApplyDeferredDistance()
    {
        if (_deferredSplitterDistance is not int desired || !CanApply(desired))
            return;

        base.SplitterDistance = desired;
        _deferredSplitterDistance = null;
    }
}
