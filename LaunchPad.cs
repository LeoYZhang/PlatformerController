using Godot;

[GlobalClass]
public partial class LaunchPad : CharacterBody2D
{
	[Export]
	public double LaunchVelocity = 1000;

	[Export]
	public Vector2 Direction;
}
