using Godot;

public partial class MovingPlatform : AnimatableBody2D
{
	[Export]
	public Vector2 Direction { get; set; } = Vector2.Right; // Example: (1, 0) for horizontal

	[Export]
	public float PlatformSpeed { get; set; } = 100.0f; // Speed of the platform in pixels/sec

	[Export]
	public float MoveDistance { get; set; } = 200.0f; // Distance to travel in one direction before reversing

	[Export]
	public float GroundFriction = 25;

	private bool _movingForward = true; // true = moving in 'Direction', false = moving in '-Direction'
	private double _distanceTravelledThisSegment = 0; // Distance travelled since the last direction change

	public override void _Ready()
	{
		// This is crucial for AnimatableBody2D to correctly interact with other physics bodies
		// when its position is changed directly.
		SyncToPhysics = true;
	}

	public override void _PhysicsProcess(double delta)
	{
		// Calculate the movement for this frame
		float currentFrameMovementAmount = PlatformSpeed * (float)delta;
		_distanceTravelledThisSegment += currentFrameMovementAmount;

		// Check if the platform has completed its current segment
		if(_distanceTravelledThisSegment >= MoveDistance)
		{
			// Calculate any overshoot
			float overshoot = (float)_distanceTravelledThisSegment - MoveDistance;
			
			// Correct the movement for this frame to land exactly at the end of the segment
			currentFrameMovementAmount -= overshoot; 
			
			// Reverse direction
			_movingForward = !_movingForward;
			// Reset distance for the new segment, accounting for the start of the next movement
			_distanceTravelledThisSegment = overshoot; 
		}

		// Determine the actual direction vector for this frame
		Vector2 currentDirectionNormalized = Direction.Normalized();
		if(!_movingForward)
		{
			currentDirectionNormalized *= -1;
		}

		// Calculate the displacement vector
		Vector2 displacement = currentDirectionNormalized * currentFrameMovementAmount;

		// Move the platform by directly changing its global position
		// This movement will be uninterruptible by other physics bodies.
		// The AnimatableBody2D will push other CharacterBody2D or RigidBody2D nodes.
		GlobalPosition += displacement;
	}
}
