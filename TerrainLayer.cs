using Godot;

[GlobalClass]
public partial class TerrainLayer : TileMapLayer
{
	[Export]
	public double GroundFriction { get; set; } = 25;
}
