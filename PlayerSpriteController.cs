using Godot;

public partial class PlayerSpriteController : Sprite2D
{
	public override void _Ready()
	{
		base._Ready();
	}

	public void ChangeColor(Color c) {
		Modulate = c;
	}
}
