using System.Numerics;
using UzDoomMapEditor.Core;

namespace UzDoomMapEditor.Editor;

public sealed class Map3DPreview : Control
{
    private sealed record Face(Vector3[] Points, Color Fill, Color Outline);
    private sealed record ProjectedFace(PointF[] Points, float Depth, Color Fill, Color Outline);

    private EditorProject _project = new();
    private float _yaw = 0.8f;
    private float _pitch = 0.55f;
    private float _distance = 900f;
    private Vector3 _target = new(0, 64, 0);

    private bool _orbiting;
    private bool _panning;
    private Point _mouseStart;
    private float _yawStart;
    private float _pitchStart;
    private Vector3 _targetStart;

    public EditorProject Project
    {
        get => _project;
        set
        {
            _project = value ?? new EditorProject();
            _project.Normalize();
            Invalidate();
        }
    }

    public Map3DPreview()
    {
        DoubleBuffered = true;
        BackColor = Color.FromArgb(25, 27, 31);
        TabStop = true;
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }

    public void ResetView()
    {
        var points = _project.Sectors.SelectMany(s => s.Vertices)
            .Concat(_project.Doors.SelectMany(d => d.GetVertices()))
            .ToList();

        if (points.Count == 0)
        {
            _target = new Vector3(0, 64, 0);
            _distance = 900f;
        }
        else
        {
            var minX = points.Min(v => v.X);
            var maxX = points.Max(v => v.X);
            var minY = points.Min(v => v.Y);
            var maxY = points.Max(v => v.Y);
            var minH = _project.Sectors.Count == 0 ? 0 : _project.Sectors.Min(s => s.FloorHeight);
            var maxH = _project.Sectors.Count == 0 ? 128 : _project.Sectors.Max(s => s.CeilingHeight);

            _target = new Vector3((minX + maxX) / 2f, (minH + maxH) / 2f, (minY + maxY) / 2f);
            var span = Math.Max(128f, Math.Max(maxX - minX, maxY - minY));
            _distance = Math.Clamp(span * 1.75f, 300f, 10000f);
        }

        _yaw = 0.8f;
        _pitch = 0.55f;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        e.Graphics.Clear(BackColor);

        if (ClientSize.Width < 10 || ClientSize.Height < 10) return;

        var faces = BuildFaces();
        var camera = GetCamera();
        var projected = new List<ProjectedFace>();

        foreach (var face in faces)
        {
            if (!TryProjectFace(face, camera, out var result)) continue;
            projected.Add(result);
        }

        foreach (var face in projected.OrderByDescending(f => f.Depth))
        {
            using var fill = new SolidBrush(face.Fill);
            using var outline = new Pen(face.Outline, 1f);
            e.Graphics.FillPolygon(fill, face.Points);
            e.Graphics.DrawPolygon(outline, face.Points);
        }

        DrawThings(e.Graphics, camera);
        DrawOverlay(e.Graphics);
    }

    private List<Face> BuildFaces()
    {
        var faces = new List<Face>();

        foreach (var sector in _project.Sectors)
        {
            if (sector.Vertices.Count < 3) continue;
            var light = Math.Clamp(sector.LightLevel / 255f, 0.2f, 1f);
            var floorColor = Shade(Color.FromArgb(86, 103, 112), light * 0.82f);
            var ceilingColor = Shade(Color.FromArgb(105, 112, 126), light * 0.72f);
            var wallColor = Shade(Color.FromArgb(112, 125, 135), light);

            faces.Add(new Face(
                sector.Vertices.Select(v => new Vector3(v.X, sector.FloorHeight, v.Y)).ToArray(),
                floorColor,
                Color.FromArgb(90, 95, 105)));

            faces.Add(new Face(
                sector.Vertices.AsEnumerable().Reverse().Select(v => new Vector3(v.X, sector.CeilingHeight, v.Y)).ToArray(),
                ceilingColor,
                Color.FromArgb(90, 95, 105)));

            for (var i = 0; i < sector.Vertices.Count; i++)
            {
                var a = sector.Vertices[i];
                var b = sector.Vertices[(i + 1) % sector.Vertices.Count];
                faces.Add(new Face(
                    new[]
                    {
                        new Vector3(a.X, sector.FloorHeight, a.Y),
                        new Vector3(b.X, sector.FloorHeight, b.Y),
                        new Vector3(b.X, sector.CeilingHeight, b.Y),
                        new Vector3(a.X, sector.CeilingHeight, a.Y)
                    },
                    wallColor,
                    Color.FromArgb(72, 77, 86)));
            }
        }

        foreach (var door in _project.Doors)
        {
            var vertices = door.GetVertices();
            var bottom = door.FloorHeight;
            var top = bottom + 112;
            var fill = Shade(Color.FromArgb(176, 108, 42), Math.Clamp(door.LightLevel / 255f, 0.3f, 1f));

            for (var i = 0; i < vertices.Count; i++)
            {
                var a = vertices[i];
                var b = vertices[(i + 1) % vertices.Count];
                faces.Add(new Face(
                    new[]
                    {
                        new Vector3(a.X, bottom, a.Y),
                        new Vector3(b.X, bottom, b.Y),
                        new Vector3(b.X, top, b.Y),
                        new Vector3(a.X, top, a.Y)
                    },
                    fill,
                    Color.FromArgb(120, 70, 25)));
            }

            faces.Add(new Face(
                vertices.Select(v => new Vector3(v.X, top, v.Y)).ToArray(),
                Shade(fill, 1.15f),
                Color.FromArgb(120, 70, 25)));
        }

        return faces;
    }

    private void DrawThings(Graphics g, Camera camera)
    {
        foreach (var thing in _project.Things)
        {
            var floor = FindFloorHeight(thing.X, thing.Y);
            var basePoint = new Vector3(thing.X, floor + thing.Height, thing.Y);
            var topPoint = basePoint + new Vector3(0, 48, 0);
            if (!TryProject(basePoint, camera, out var a, out _) || !TryProject(topPoint, camera, out var b, out _)) continue;

            using var pen = new Pen(thing.Type == 1 ? Color.LimeGreen : Color.Gold, 3f);
            g.DrawLine(pen, a, b);
            g.FillEllipse(new SolidBrush(pen.Color), b.X - 4, b.Y - 4, 8, 8);
        }
    }

    private int FindFloorHeight(int x, int y)
    {
        foreach (var sector in _project.Sectors)
            if (GeometryUtil.PointInPolygon(x, y, sector.Vertices))
                return sector.FloorHeight;
        return 0;
    }

    private void DrawOverlay(Graphics g)
    {
        const string title = "3D PERSPECTIVE";
        const string help = "LMB orbit   MMB/RMB pan   Wheel zoom";
        using var bg = new SolidBrush(Color.FromArgb(180, 12, 13, 15));
        using var fg = new SolidBrush(Color.Gainsboro);
        var titleSize = g.MeasureString(title, Font);
        var helpSize = g.MeasureString(help, Font);
        var width = Math.Max(titleSize.Width, helpSize.Width) + 16;
        g.FillRectangle(bg, 8, 8, width, titleSize.Height + helpSize.Height + 10);
        g.DrawString(title, Font, fg, 14, 11);
        g.DrawString(help, Font, fg, 14, 11 + titleSize.Height + 1);
    }

    private readonly record struct Camera(Vector3 Position, Vector3 Forward, Vector3 Right, Vector3 Up, float Focal);

    private Camera GetCamera()
    {
        var cosPitch = MathF.Cos(_pitch);
        var offset = new Vector3(
            cosPitch * MathF.Cos(_yaw),
            MathF.Sin(_pitch),
            cosPitch * MathF.Sin(_yaw)) * _distance;

        var position = _target + offset;
        var forward = Vector3.Normalize(_target - position);
        var right = Vector3.Cross(forward, Vector3.UnitY);
        if (right.LengthSquared() < 0.0001f) right = Vector3.UnitX;
        right = Vector3.Normalize(right);
        var up = Vector3.Normalize(Vector3.Cross(right, forward));
        var focal = Math.Min(ClientSize.Width, ClientSize.Height) * 0.95f;
        return new Camera(position, forward, right, up, focal);
    }

    private bool TryProjectFace(Face face, Camera camera, out ProjectedFace projected)
    {
        var points = new PointF[face.Points.Length];
        float depthSum = 0;
        for (var i = 0; i < face.Points.Length; i++)
        {
            if (!TryProject(face.Points[i], camera, out points[i], out var depth))
            {
                projected = null!;
                return false;
            }
            depthSum += depth;
        }

        projected = new ProjectedFace(points, depthSum / face.Points.Length, face.Fill, face.Outline);
        return true;
    }

    private bool TryProject(Vector3 point, Camera camera, out PointF screen, out float depth)
    {
        var rel = point - camera.Position;
        depth = Vector3.Dot(rel, camera.Forward);
        if (depth <= 2f)
        {
            screen = PointF.Empty;
            return false;
        }

        var x = Vector3.Dot(rel, camera.Right);
        var y = Vector3.Dot(rel, camera.Up);
        var scale = camera.Focal / depth;
        screen = new PointF(
            ClientSize.Width / 2f + x * scale,
            ClientSize.Height / 2f - y * scale);
        return true;
    }

    private static Color Shade(Color source, float factor)
    {
        return Color.FromArgb(
            source.A,
            Math.Clamp((int)(source.R * factor), 0, 255),
            Math.Clamp((int)(source.G * factor), 0, 255),
            Math.Clamp((int)(source.B * factor), 0, 255));
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        _mouseStart = e.Location;

        if (e.Button == MouseButtons.Left)
        {
            _orbiting = true;
            _yawStart = _yaw;
            _pitchStart = _pitch;
            Capture = true;
        }
        else if (e.Button is MouseButtons.Middle or MouseButtons.Right)
        {
            _panning = true;
            _targetStart = _target;
            Capture = true;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var dx = e.X - _mouseStart.X;
        var dy = e.Y - _mouseStart.Y;

        if (_orbiting)
        {
            _yaw = _yawStart - dx * 0.008f;
            _pitch = Math.Clamp(_pitchStart + dy * 0.006f, -0.15f, 1.35f);
            Invalidate();
        }
        else if (_panning)
        {
            var camera = GetCamera();
            var scale = _distance / Math.Max(200f, Math.Min(ClientSize.Width, ClientSize.Height));
            var horizontal = camera.Right * (-dx * scale);
            var flatForward = new Vector3(camera.Forward.X, 0, camera.Forward.Z);
            if (flatForward.LengthSquared() > 0.001f) flatForward = Vector3.Normalize(flatForward);
            var forward = flatForward * (dy * scale);
            _target = _targetStart + horizontal + forward;
            Invalidate();
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Left) _orbiting = false;
        if (e.Button is MouseButtons.Middle or MouseButtons.Right) _panning = false;
        if (!_orbiting && !_panning) Capture = false;
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        _distance = Math.Clamp(_distance * (e.Delta > 0 ? 0.86f : 1.16f), 50f, 50000f);
        Invalidate();
    }
}
