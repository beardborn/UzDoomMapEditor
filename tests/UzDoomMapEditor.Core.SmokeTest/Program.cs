using System.Text.Json;
using UzDoomMapEditor.Core;

try
{
    var project = new EditorProject { Name = "Ramp Smoke", MapName = "MAP01" };
    var ramp = Sector.Rectangle("Ramp 1", 0, 0, 128, 64);
    ramp.FloorShape = SectorFloorShape.Ramp;
    ramp.FloorHeight = 0;
    ramp.RampEndHeight = 64;
    ramp.RampDirection = RampDirection.East;
    ramp.CeilingHeight = 192;
    project.Sectors.Add(ramp);
    project.Things.Add(new MapThing { Type = 1, X = 16, Y = 16 });

    Require(Math.Abs(ramp.GetFloorHeightAt(0, 32) - 0) < 0.001, "Ramp start height is wrong.");
    Require(Math.Abs(ramp.GetFloorHeightAt(64, 32) - 32) < 0.001, "Ramp midpoint height is wrong.");
    Require(Math.Abs(ramp.GetFloorHeightAt(128, 32) - 64) < 0.001, "Ramp end height is wrong.");

    var udmf = UdmfExporter.BuildText(project);
    Require(udmf.Contains("floorplane_a = -0.5;", StringComparison.Ordinal), "Ramp floorplane A coefficient was not exported.");
    Require(udmf.Contains("floorplane_b = 0;", StringComparison.Ordinal), "Ramp floorplane B coefficient was not exported.");
    Require(udmf.Contains("floorplane_c = 1;", StringComparison.Ordinal), "Ramp floorplane C coefficient was not exported.");
    Require(udmf.Contains("floorplane_d = 0;", StringComparison.Ordinal), "Ramp floorplane D coefficient was not exported.");

    var door = new Door();
    Require(door.Width == 128, "New door width should default to 128 map units.");
    Require(door.Depth == 64, "New door depth should default to 64 map units.");

    var json = JsonSerializer.Serialize(project);
    var reopened = JsonSerializer.Deserialize<EditorProject>(json) ?? throw new InvalidOperationException("Map JSON round trip returned null.");
    reopened.Normalize();
    Require(reopened.Sectors.Count == 1, "Ramp sector was lost during JSON round trip.");
    Require(reopened.Sectors[0].FloorShape == SectorFloorShape.Ramp, "Ramp floor shape was not preserved.");
    Require(reopened.Sectors[0].RampDirection == RampDirection.East, "Ramp direction was not preserved.");
    Require(reopened.Sectors[0].RampEndHeight == 64, "Ramp end height was not preserved.");

    Console.WriteLine("UZDoom map ramp/door smoke tests passed.");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex);
    return 1;
}

static void Require(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
