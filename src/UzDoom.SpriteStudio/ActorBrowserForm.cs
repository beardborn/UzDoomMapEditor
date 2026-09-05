using UzDoom.Core;

namespace UzDoom.SpriteStudio;

internal sealed record ActorPreviewFrame(string LumpName, DoomPatchImage Image, bool Mirrored);

internal sealed class ActorBrowserForm : Form
{
    private readonly ActorDefinitionCatalog _catalog;
    private readonly DoomPalette _palette;
    private readonly Func<string, char, int, ActorPreviewFrame?> _resolveFrame;
    private readonly Action<string, char> _navigateToFrame;

    private readonly TextBox _search = new();
    private readonly TreeView _actorTree = new();
    private readonly ListBox _stateList = new();
    private readonly ListView _frameList = new();
    private readonly SpritePreviewControl _preview = new();
    private readonly ComboBox _rotation = new();
    private readonly Button _play = new();
    private readonly Label _actorInfo = new();
    private readonly Label _stateInfo = new();
    private readonly ToolStripStatusLabel _status = new();
    private readonly System.Windows.Forms.Timer _timer = new();
    private readonly List<ActorSpriteFrame> _sequence = new();

    private int _sequenceIndex;

    public ActorBrowserForm(
        ActorDefinitionCatalog catalog,
        DoomPalette palette,
        Func<string, char, int, ActorPreviewFrame?> resolveFrame,
        Action<string, char> navigateToFrame)
    {
        _catalog = catalog;
        _palette = palette;
        _resolveFrame = resolveFrame;
        _navigateToFrame = navigateToFrame;

        Text = "UzDoom Sprite Studio v0.4 - Actor States";
        StartPosition = FormStartPosition.CenterParent;
        Width = 1280;
        Height = 780;
        MinimumSize = new Size(920, 600);
        BackColor = Color.FromArgb(28, 30, 34);
        ForeColor = Color.Gainsboro;

        var header = BuildHeader();
        var body = BuildBody();
        var statusStrip = new StatusStrip
        {
            BackColor = Color.FromArgb(37, 39, 44),
            ForeColor = Color.Gainsboro,
            SizingGrip = false
        };
        statusStrip.Items.Add(_status);

        Controls.Add(body);
        Controls.Add(statusStrip);
        Controls.Add(header);

        _timer.Tick += (_, _) => AdvanceAnimation();
        FormClosing += (_, _) => StopAnimation();

        RebuildActorTree();
        _status.Text = catalog.UsedClassicFallback
            ? "No DECORATE/ZSCRIPT actor definitions were found. Using built-in classic Doom/Freedoom state profiles."
            : $"Loaded {catalog.Actors.Count:N0} actor definitions from {string.Join(", ", catalog.Sources)}.";
    }

    private Control BuildHeader()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 64,
            Padding = new Padding(12, 8, 12, 8),
            BackColor = Color.FromArgb(35, 37, 42)
        };

        var title = new Label
        {
            Text = "ACTOR / STATE BROWSER",
            Dock = DockStyle.Left,
            Width = 210,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font(Font.FontFamily, 10f, FontStyle.Bold),
            ForeColor = Color.White
        };

        _search.Dock = DockStyle.Fill;
        _search.PlaceholderText = "Filter actors, state names or sprite families...";
        _search.BackColor = Color.FromArgb(49, 51, 57);
        _search.ForeColor = Color.White;
        _search.BorderStyle = BorderStyle.FixedSingle;
        _search.TextChanged += (_, _) => RebuildActorTree();

        var source = new Label
        {
            Dock = DockStyle.Right,
            Width = 330,
            TextAlign = ContentAlignment.MiddleRight,
            ForeColor = _catalog.UsedClassicFallback ? Color.Khaki : Color.Silver,
            Text = _catalog.UsedClassicFallback
                ? "Source: Classic Doom fallback"
                : $"Source: {string.Join(", ", _catalog.Sources)}"
        };

        panel.Controls.Add(_search);
        panel.Controls.Add(source);
        panel.Controls.Add(title);
        return panel;
    }

    private Control BuildBody()
    {
        var outer = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            Size = new Size(1200, 650),
            SplitterDistance = 300,
            BackColor = Color.FromArgb(21, 23, 26)
        };
        outer.Panel1MinSize = 230;
        outer.Panel2MinSize = 620;

        ConfigureActorTree();
        outer.Panel1.Controls.Add(_actorTree);
        outer.Panel1.Controls.Add(BuildActorInfo());

        var detail = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            Size = new Size(880, 650),
            SplitterDistance = 300,
            BackColor = Color.FromArgb(21, 23, 26)
        };
        detail.Panel1MinSize = 240;
        detail.Panel2MinSize = 430;
        detail.Panel1.Controls.Add(BuildStatePanel());
        detail.Panel2.Controls.Add(BuildPreviewAndFrames());
        outer.Panel2.Controls.Add(detail);

        return outer;
    }

    private void ConfigureActorTree()
    {
        _actorTree.Dock = DockStyle.Fill;
        _actorTree.BackColor = Color.FromArgb(31, 33, 38);
        _actorTree.ForeColor = Color.Gainsboro;
        _actorTree.BorderStyle = BorderStyle.None;
        _actorTree.HideSelection = false;
        _actorTree.FullRowSelect = true;
        _actorTree.AfterSelect += (_, _) => PopulateStates();
    }

    private Control BuildActorInfo()
    {
        _actorInfo.Dock = DockStyle.Bottom;
        _actorInfo.Height = 80;
        _actorInfo.Padding = new Padding(10);
        _actorInfo.BackColor = Color.FromArgb(36, 38, 43);
        _actorInfo.ForeColor = Color.Silver;
        return _actorInfo;
    }

    private Control BuildStatePanel()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(32, 34, 39),
            Padding = new Padding(10)
        };

        var title = new Label
        {
            Text = "STATES",
            Dock = DockStyle.Top,
            Height = 28,
            Font = new Font(Font.FontFamily, 10f, FontStyle.Bold),
            ForeColor = Color.White
        };

        _stateList.Dock = DockStyle.Fill;
        _stateList.BackColor = Color.FromArgb(31, 33, 38);
        _stateList.ForeColor = Color.Gainsboro;
        _stateList.BorderStyle = BorderStyle.FixedSingle;
        _stateList.IntegralHeight = false;
        _stateList.FormattingEnabled = true;
        _stateList.SelectedIndexChanged += (_, _) => PopulateFrames();
        _stateList.Format += (_, e) =>
        {
            if (e.ListItem is ActorStateDefinition state)
                e.Value = state.FriendlyName == state.Label ? state.Label : $"{state.FriendlyName}   [{state.Label}]";
        };

        _stateInfo.Dock = DockStyle.Bottom;
        _stateInfo.Height = 70;
        _stateInfo.Padding = new Padding(4, 8, 4, 4);
        _stateInfo.ForeColor = Color.Silver;

        panel.Controls.Add(_stateList);
        panel.Controls.Add(_stateInfo);
        panel.Controls.Add(title);
        return panel;
    }

    private Control BuildPreviewAndFrames()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(24, 26, 30)
        };

        var framePanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 245,
            Padding = new Padding(10),
            BackColor = Color.FromArgb(34, 36, 41)
        };

        var frameTitle = new Label
        {
            Text = "STATE FRAMES   (double-click to jump to the sprite)",
            Dock = DockStyle.Top,
            Height = 26,
            ForeColor = Color.White,
            Font = new Font(Font.FontFamily, 9f, FontStyle.Bold)
        };

        ConfigureFrameList();
        framePanel.Controls.Add(_frameList);
        framePanel.Controls.Add(frameTitle);

        var controls = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 48,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(8, 8, 8, 5),
            BackColor = Color.FromArgb(38, 40, 45)
        };

        _play.Text = "▶ Play State";
        _play.Width = 105;
        _play.Height = 28;
        _play.FlatStyle = FlatStyle.Flat;
        _play.BackColor = Color.FromArgb(66, 83, 116);
        _play.ForeColor = Color.White;
        _play.Click += (_, _) => ToggleAnimation();

        _rotation.DropDownStyle = ComboBoxStyle.DropDownList;
        _rotation.Width = 110;
        for (var i = 1; i <= 8; i++)
            _rotation.Items.Add($"Rotation {i}");
        _rotation.SelectedIndex = 0;
        _rotation.SelectedIndexChanged += (_, _) =>
        {
            StopAnimation();
            PopulateFrames();
        };

        controls.Controls.Add(_play);
        controls.Controls.Add(new Label
        {
            Text = "View",
            AutoSize = true,
            Margin = new Padding(12, 6, 4, 0),
            ForeColor = Color.Silver
        });
        controls.Controls.Add(_rotation);

        _preview.Dock = DockStyle.Fill;
        panel.Controls.Add(_preview);
        panel.Controls.Add(controls);
        panel.Controls.Add(framePanel);
        return panel;
    }

    private void ConfigureFrameList()
    {
        _frameList.Dock = DockStyle.Fill;
        _frameList.View = View.Details;
        _frameList.FullRowSelect = true;
        _frameList.HideSelection = false;
        _frameList.MultiSelect = false;
        _frameList.BackColor = Color.FromArgb(29, 31, 36);
        _frameList.ForeColor = Color.Gainsboro;
        _frameList.BorderStyle = BorderStyle.FixedSingle;
        _frameList.Columns.Add("#", 38);
        _frameList.Columns.Add("Sprite", 95);
        _frameList.Columns.Add("Tics", 55);
        _frameList.Columns.Add("Flags", 70);
        _frameList.Columns.Add("Resolved lump", 150);
        _frameList.DoubleClick += (_, _) => NavigateSelectedFrame();
        _frameList.SelectedIndexChanged += (_, _) => PreviewSelectedFrame();
    }

    private void RebuildActorTree()
    {
        var previous = CurrentActor?.Name;
        var query = _search.Text.Trim();

        var actors = _catalog.Actors
            .Where(actor => MatchesFilter(actor, query))
            .OrderBy(actor => actor.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _actorTree.BeginUpdate();
        try
        {
            _actorTree.Nodes.Clear();
            foreach (var actor in actors)
            {
                var parent = string.IsNullOrWhiteSpace(actor.Parent) ? string.Empty : $" : {actor.Parent}";
                _actorTree.Nodes.Add(new TreeNode(actor.Name + parent) { Tag = actor });
            }

            var select = _actorTree.Nodes.Cast<TreeNode>()
                .FirstOrDefault(node => node.Tag is ActorDefinition actor && string.Equals(actor.Name, previous, StringComparison.OrdinalIgnoreCase))
                ?? (_actorTree.Nodes.Count > 0 ? _actorTree.Nodes[0] : null);
            if (select is not null)
                _actorTree.SelectedNode = select;
        }
        finally
        {
            _actorTree.EndUpdate();
        }
    }

    private static bool MatchesFilter(ActorDefinition actor, string query)
    {
        if (query.Length == 0)
            return true;
        if (actor.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            return true;
        if (actor.Parent?.Contains(query, StringComparison.OrdinalIgnoreCase) == true)
            return true;
        if (actor.SpriteFamilies.Any(family => family.Contains(query, StringComparison.OrdinalIgnoreCase)))
            return true;
        return actor.States.Any(state => state.Label.Contains(query, StringComparison.OrdinalIgnoreCase)
                                         || state.FriendlyName.Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    private void PopulateStates()
    {
        StopAnimation();
        _stateList.BeginUpdate();
        try
        {
            _stateList.Items.Clear();
            _frameList.Items.Clear();
            _preview.SetSprite(null);

            var actor = CurrentActor;
            if (actor is null)
            {
                _actorInfo.Text = string.Empty;
                return;
            }

            foreach (var state in actor.States)
                _stateList.Items.Add(state);

            _actorInfo.Text = $"{actor.Name}\r\nParent: {actor.Parent ?? "(none)"}\r\nSprites: {string.Join(", ", actor.SpriteFamilies)}";
            if (_stateList.Items.Count > 0)
                _stateList.SelectedIndex = 0;
        }
        finally
        {
            _stateList.EndUpdate();
        }
    }

    private void PopulateFrames()
    {
        StopAnimation();
        _frameList.BeginUpdate();
        try
        {
            _frameList.Items.Clear();
            _preview.SetSprite(null);
            var state = CurrentState;
            if (state is null)
            {
                _stateInfo.Text = string.Empty;
                return;
            }

            var missing = 0;
            var rotation = _rotation.SelectedIndex + 1;
            for (var i = 0; i < state.Frames.Count; i++)
            {
                var frame = state.Frames[i];
                var resolved = _resolveFrame(frame.Family, frame.Frame, rotation);
                if (resolved is null)
                    missing++;

                var item = new ListViewItem((i + 1).ToString())
                {
                    Tag = frame
                };
                item.SubItems.Add($"{frame.Family} {frame.Frame}");
                item.SubItems.Add(frame.Tics.ToString());
                item.SubItems.Add(frame.Bright ? "Bright" : string.Empty);
                item.SubItems.Add(resolved?.LumpName ?? "MISSING");
                if (resolved is null)
                    item.ForeColor = Color.Salmon;
                _frameList.Items.Add(item);
            }

            _stateInfo.Text = $"{state.FriendlyName}\r\n{state.Frames.Count:N0} frame steps" +
                              (string.IsNullOrWhiteSpace(state.FlowControl) ? string.Empty : $"   •   {state.FlowControl}") +
                              (missing == 0 ? string.Empty : $"\r\n{missing:N0} step(s) could not be matched to loaded sprites.");

            _sequence.Clear();
            _sequence.AddRange(state.Frames);
            if (_frameList.Items.Count > 0)
                _frameList.Items[0].Selected = true;
        }
        finally
        {
            _frameList.EndUpdate();
        }
    }

    private void PreviewSelectedFrame()
    {
        if (_frameList.SelectedItems.Count == 0 || _frameList.SelectedItems[0].Tag is not ActorSpriteFrame frame)
            return;
        ShowFrame(frame);
    }

    private void NavigateSelectedFrame()
    {
        if (_frameList.SelectedItems.Count == 0 || _frameList.SelectedItems[0].Tag is not ActorSpriteFrame frame)
            return;
        _navigateToFrame(frame.Family, frame.Frame);
    }

    private void ToggleAnimation()
    {
        if (_timer.Enabled)
        {
            StopAnimation();
            PreviewSelectedFrame();
            return;
        }

        if (_sequence.Count == 0)
            return;

        _sequenceIndex = 0;
        _play.Text = "■ Stop";
        ShowFrame(_sequence[0]);
        ScheduleNext(_sequence[0]);
        _timer.Start();
    }

    private void AdvanceAnimation()
    {
        if (_sequence.Count == 0)
            return;

        _sequenceIndex = (_sequenceIndex + 1) % _sequence.Count;
        var frame = _sequence[_sequenceIndex];
        ShowFrame(frame);
        ScheduleNext(frame);
    }

    private void ScheduleNext(ActorSpriteFrame frame)
    {
        var tics = frame.Tics <= 0 ? 4 : frame.Tics;
        _timer.Interval = Math.Clamp((int)Math.Round(tics * 1000d / 35d), 35, 2000);
    }

    private void ShowFrame(ActorSpriteFrame frame)
    {
        var rotation = _rotation.SelectedIndex + 1;
        var resolved = _resolveFrame(frame.Family, frame.Frame, rotation);
        if (resolved is null)
        {
            _preview.SetSprite(null);
            _status.Text = $"Missing {frame.Family} frame {frame.Frame} for rotation {rotation}.";
            return;
        }

        var bitmap = SpriteBitmapFactory.ToBitmap(resolved.Image, _palette);
        if (resolved.Mirrored)
            bitmap.RotateFlip(RotateFlipType.RotateNoneFlipX);
        _preview.SetSprite(bitmap, resolved.Image.LeftOffset, resolved.Image.TopOffset);
        _status.Text = $"{CurrentActor?.Name} / {CurrentState?.Label}: {resolved.LumpName}   •   {frame.Tics} tics" +
                       (resolved.Mirrored ? "   •   mirrored" : string.Empty) +
                       (frame.Bright ? "   •   bright" : string.Empty);
    }

    private void StopAnimation()
    {
        if (_timer.Enabled)
            _timer.Stop();
        _play.Text = "▶ Play State";
    }

    private ActorDefinition? CurrentActor => _actorTree.SelectedNode?.Tag as ActorDefinition;
    private ActorStateDefinition? CurrentState => _stateList.SelectedItem as ActorStateDefinition;
}
