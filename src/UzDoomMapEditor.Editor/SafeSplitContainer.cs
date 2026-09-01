namespace UzDoomMapEditor.Editor;

/// <summary>
/// WinForms validates SplitterDistance and panel minimum sizes against the
/// control's current size. MainForm configures its desired layout before the
/// split containers have been parented and laid out, so the stock control can
/// throw during startup. This wrapper defers values that are not valid yet and
/// applies them once layout gives the control its real size.
/// </summary>
public sealed class SplitContainer : System.Windows.Forms.SplitContainer
{
    private int? _deferredSplitterDistance;
    private int? _deferredPanel2MinSize;

    public new int SplitterDistance
    {
        get => _deferredSplitterDistance ?? base.SplitterDistance;
        set
        {
            if (CanApplySplitterDistance(value))
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

    public new int Panel2MinSize
    {
        get => _deferredPanel2MinSize ?? base.Panel2MinSize;
        set
        {
            if (CanApplyPanel2MinSize(value))
            {
                base.Panel2MinSize = value;
                _deferredPanel2MinSize = null;
            }
            else
            {
                _deferredPanel2MinSize = value;
            }
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyDeferredLayout();
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        ApplyDeferredLayout();
    }

    private int Extent => Orientation == Orientation.Vertical ? ClientSize.Width : ClientSize.Height;

    private bool CanApplySplitterDistance(int value)
    {
        var maximum = Extent - Panel2MinSize - SplitterWidth;
        return value >= Panel1MinSize && value <= maximum;
    }

    private bool CanApplyPanel2MinSize(int value)
    {
        if (value < 0) return false;

        var maximumSplitterDistance = Extent - value - SplitterWidth;
        return base.SplitterDistance >= Panel1MinSize && base.SplitterDistance <= maximumSplitterDistance;
    }

    private void ApplyDeferredLayout()
    {
        // Apply the panel minimum first. The requested splitter distance uses it
        // when calculating its legal maximum.
        if (_deferredPanel2MinSize is int minSize && CanApplyPanel2MinSize(minSize))
        {
            base.Panel2MinSize = minSize;
            _deferredPanel2MinSize = null;
        }

        if (_deferredSplitterDistance is int desired && CanApplySplitterDistance(desired))
        {
            base.SplitterDistance = desired;
            _deferredSplitterDistance = null;
        }
    }
}
