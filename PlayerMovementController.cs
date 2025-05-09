// PlayerMovementController.cs
// Handles all aspects of player movement, including running, jumping, dashing,
// and interactions with various environmental elements.

using System;
using Godot;

public partial class PlayerMovementController : CharacterBody2D
{
	// --- Constants and Units ---
	// time: Measured in physics frames (default 60 fps). Timers are often converted from frames to seconds.
	// distance: Measured in pixels (Godot's default 2D unit).
	// velocity: Measured in pixels per second.
	// acceleration: Measured in pixels per second squared.

	// --- Exported Variables: General Physics ---
	[ExportGroup("General")]
	[Export] public double Gravity = 3000; // Downward acceleration applied when airborne (not used for the initial jump impulse calculation, but for falling).

	// --- Exported Variables: Running ---
	[ExportGroup("Running")]
	[Export] public double MaxRunSpeed = 200;           // Maximum horizontal speed achievable while running on the ground.
	[Export] public double TimeToMaxRunSpeed = 3;       // Time in physics frames (at 60fps) to reach MaxRunSpeed from a standstill.
	[Export] public double CoyoteTime = 3;              // Duration in physics frames (at 60fps) allowing a jump after leaving a platform.
	[Export] public double GroundFriction = 20;         // Coefficient determining how quickly the player decelerates on the ground when no movement input is given.

	// --- Exported Variables: Mid-Air Movement ---
	[ExportGroup("Midair")]
	[Export] public double MaxAirRunSpeed = 200;        // Maximum horizontal speed achievable while moving in the air.
	[Export] public double TimeToMaxAirRunSpeed = 6;    // Time in physics frames (at 60fps) to reach MaxAirRunSpeed while airborne.
	[Export] public double AirFriction = 1;             // Coefficient determining how quickly the player decelerates in the air when no movement input is given.
	[Export] public int DoubleJumps = 1;                // Number of additional jumps allowed while airborne.
	[Export] public int Dashes = 1;                     // Number of dashes available before needing to touch the ground to reset.
	[Export] public int DashVelocity = 750;             // Magnitude of the velocity vector applied during a dash.
	[Export] public double DashDuration = 6;            // Duration of the dash in physics frames (at 60fps).
	[Export] public double DashEndRatio = 0.5;          // Multiplier applied to velocity when the dash ends, to reduce speed abruptly.

	// --- Exported Variables: Jumping ---
	[ExportGroup("Jumping")]
	[Export] public double MaxJumpHeight = 75;          // The maximum vertical distance the player can reach from a jump's start.
	[Export] public double HangTime = 6;                // Duration in physics frames (at 60fps) the player can "hang" at the peak of a jump with reduced gravity.
	[Export] public double HangTimeSmooth = 1000;       // Value counteracting gravity during hang time. Higher values make the hang floatier. A value equal to Gravity would result in no vertical movement during hang.
	[Export] public double MaxJumpHeldTime = 12;        // Maximum duration in physics frames (at 60fps) the jump button can be held to influence jump height.
	[Export] public double JumpBuffer = 10;             // Duration in physics frames (at 60fps) a jump input is remembered if pressed slightly before landing.
	[Export] public double BHopBoostFactor = 1.3;       // Multiplier applied to horizontal speed if jumping immediately upon landing (bunny hop).

	// --- Private Variables: System and Constants ---
	private double _fps = Engine.GetPhysicsTicksPerSecond(); // Fetches the physics FPS (typically 60) for frame-based timer conversions.
	private const double _epsilonSpeed = 1e-2; // A very small speed value, used to consider velocity as effectively zero.

	// --- Player State Management ---
	private enum State {
		Grounded, // Player is on the floor.
		Airborne, // Player is in the air (falling, jumping, or dashing).
		Coyote    // Player has just walked off a ledge, allowing a brief window for a jump.
	}
	private State _currState; // Current primary state of the player.

	private enum JumpState {
		NotJumping, // Player is not currently in an active jump sequence (on ground or falling).
		Ascent,     // Player is moving upwards as part of a jump.
		Hanging     // Player is at the peak of the jump, experiencing reduced gravity (hang time).
	}
	private JumpState _currJumpState; // Current state of the jump mechanics.

	// --- Velocity and Collision Tracking ---
	private Vector2 _prevVelocity; // Stores the velocity from the previous physics frame, used for detecting external velocity changes (e.g., collisions).

	// --- Timers for Mechanics ---
	private Timer _jumpBufferTimer; // Timer for the jump input buffer.
	private bool _jumpBuffered;     // Flag indicating if a jump input is currently buffered.

	private Timer _coyoteTimer;     // Timer for coyote time.

	private Timer _jumpHangTimer;   // Timer for jump hang time.

	private Timer _jumpHeightTimer; // Timer tracking how long the jump button has been held to determine variable jump height.

	private Timer _dashTimer;       // Timer for dash duration.

	// --- Resource Tracking ---
	private int _airJumpsLeft; // Remaining air jumps.
	private int _dashesLeft;   // Remaining dashes.

	// --- Godot Engine Functions ---

	/// <summary>
	/// Called when the node enters the scene tree for the first time.
	/// Initializes states, timers, and resources.
	/// </summary>
	public override void _Ready()
	{
		base._Ready();

		// Initialize player states
		_currState = State.Airborne;     // Start airborne to allow initial ground check to correctly set state.
		_currJumpState = JumpState.NotJumping;

		// Setup Jump Buffer Timer
		_jumpBufferTimer = new Timer();
		_jumpBufferTimer.Connect(Timer.SignalName.Timeout, Callable.From(ResetJumpBufferTimer)); // Connect timeout signal
		AddChild(_jumpBufferTimer);      // Add timer to the scene tree to process
		_jumpBuffered = false;

		// Setup Coyote Timer
		_coyoteTimer = new Timer();
		_coyoteTimer.Connect(Timer.SignalName.Timeout, Callable.From(ResetCoyoteTimer));
		AddChild(_coyoteTimer);

		// Setup Jump Hang Timer
		_jumpHangTimer = new Timer();
		_jumpHangTimer.Connect(Timer.SignalName.Timeout, Callable.From(ResetJumpHangTimer));
		AddChild(_jumpHangTimer);

		// Setup Jump Height (Hold) Timer
		_jumpHeightTimer = new Timer();
		_jumpHeightTimer.Connect(Timer.SignalName.Timeout, Callable.From(ResetJumpHeightTimer));
		AddChild(_jumpHeightTimer);

		// Setup Dash Timer
		_dashTimer = new Timer();
		_dashTimer.Connect(Timer.SignalName.Timeout, Callable.From(ResetDashTimer));
		AddChild(_dashTimer);

		// Initialize resources
		_airJumpsLeft = DoubleJumps;
		_dashesLeft = Dashes;
	}

	/// <summary>
	/// Called every physics frame. Handles input, state updates, and movement.
	/// </summary>
	/// <param name="delta">The time elapsed since the last physics frame, in seconds.</param>
	public override void _PhysicsProcess(double delta)
	{
		base._PhysicsProcess(delta); // Standard Godot CharacterBody2D processing.
		HandleEnviornment(delta);    // Check for interactions with terrain, launchpads, etc.

		// --- Input Gathering ---
		bool jumpJustPressed = Input.IsActionJustPressed("jump"); // Jump button pressed this frame.
		bool jumpReleased = Input.IsActionJustReleased("jump");   // Jump button released this frame.
		bool dashJustPressed = Input.IsActionJustPressed("dash"); // Dash button pressed this frame.
		bool leftPressed = Input.IsActionPressed("left_arrow");   // Left movement button held.
		bool rightPressed = Input.IsActionPressed("right_arrow"); // Right movement button held.
		bool upPressed = Input.IsActionPressed("up_arrow");       // Up movement button held (for dashing).
		bool downPressed = Input.IsActionPressed("down_arrow");   // Down movement button held (for dashing).
		bool onFloor = IsOnFloor();                               // Is the player currently touching the floor?

		// --- State Machine Logic ---
		bool jumped = false; // Flag to indicate if a jump was initiated this frame.

		// --- Player State (Grounded, Airborne, Coyote) ---
		switch (_currState)
		{
			case State.Grounded:
				if (jumpJustPressed || _jumpBuffered) // Jump input received
				{
					_currState = State.Airborne;
					jumped = true;
					StartJump(false); // Standard jump
				}
				else if (!onFloor) // Player walked off a ledge
				{
					_currState = State.Coyote;
					_coyoteTimer.Start(CoyoteTime / _fps); // Start coyote time window
				}
				else // Still on ground, no jump
				{
					_dashesLeft = Dashes; // Reset dashes when grounded
				}
				break;

			case State.Airborne:
				if (onFloor) // Player landed
				{
					_airJumpsLeft = DoubleJumps; // Reset air jumps
					_dashesLeft = Dashes;       // Reset dashes
					if (jumpJustPressed || _jumpBuffered) // Jump input upon landing (bhop or buffered)
					{
						jumped = true;
						StartJump(true); // Bhop jump
					}
					else
					{
						_currState = State.Grounded; // Transition to grounded state
					}
				}
				else if (_airJumpsLeft > 0 && (jumpJustPressed || _jumpBuffered)) // Air jump
				{
					jumped = true;
					StartJump(false); // Standard jump (uses air jump resource)
					_airJumpsLeft--;
				}
				break;

			case State.Coyote:
				if (onFloor) // Player landed back during coyote time
				{
					_airJumpsLeft = DoubleJumps;
					_dashesLeft = Dashes;
					_currState = State.Grounded;
				}
				else if (_coyoteTimer.TimeLeft == 0) // Coyote time expired
				{
					_currState = State.Airborne;
				}
				else if (jumpJustPressed || _jumpBuffered) // Jumped during coyote time
				{
					jumped = true;
					_currState = State.Airborne;
					if (onFloor) // Should not happen if coyote time is active, but for safety
					{
						StartJump(true);
					}
					else
					{
						StartJump(false); // Coyote jump (uses ground jump logic)
					}
				}
				break;
		}

		// --- Jump State (NotJumping, Ascent, Hanging) ---
		switch (_currJumpState)
		{
			case JumpState.NotJumping:
				if (jumped) // A jump was initiated this frame
				{
					_currJumpState = JumpState.Ascent;
				}
				break;

			case JumpState.Ascent:
				// Conditions to exit ascent phase:
				// 1. Landed on the floor.
				// 2. Vertical velocity was externally disrupted (e.g., hit a ceiling).
				if (onFloor || (Velocity.Y != _prevVelocity.Y && Velocity.Y > _prevVelocity.Y)) // Second condition checks for unexpected halt/downward push
				{
					ResetJumpHeightTimer(); // Stop tracking jump hold time
					_currJumpState = JumpState.NotJumping;
				}
				else if (Velocity.Y >= 0) // Reached the peak of the jump (or started falling)
				{
					_currJumpState = JumpState.Hanging;
					Velocity = new Vector2(Velocity.X, 0); // Nullify vertical speed for hang
					_jumpHangTimer.Start(HangTime / _fps); // Start hang time
				}
				else if (jumpReleased && _jumpHeightTimer.TimeLeft > 0) // Jump button released early for variable height
				{
					// Calculate current height achieved during the jump
					double initialSpeed = Math.Sqrt(2 * Gravity * MaxJumpHeight); // Potential initial speed for max height
					double timeSinceJumpStart = (MaxJumpHeldTime / _fps) - _jumpHeightTimer.TimeLeft; // Time button was held
					double distanceTraveled = (initialSpeed * timeSinceJumpStart) - (0.5 * Gravity * Math.Pow(timeSinceJumpStart, 2)); // s = v0*t + 0.5*a*t^2
					
					// Calculate the new target velocity based on how long the jump was held
					// (timeSinceJumpStart / (MaxJumpHeight / _fps)) is a typo in original, should be timeSinceJumpStart / (MaxJumpHeldTime / _fps) for proportion
					double proportionOfMaxHold = timeSinceJumpStart / (MaxJumpHeldTime / _fps);
					double newJumpVelocityY = GetJumpNewVelocity(distanceTraveled, proportionOfMaxHold);

					// Apply the new velocity
					if (newJumpVelocityY >= 0) // If calculated new velocity is non-negative (i.e., should stop or fall)
					{
						Velocity = new Vector2(Velocity.X, 0); // Go into hang or fall
						_currJumpState = JumpState.Hanging; // Transition to hang if at peak
					}
					else
					{
						Velocity = new Vector2(Velocity.X, (float)newJumpVelocityY); // Apply new upward velocity
					}
					ResetJumpHeightTimer(); // Stop tracking jump hold as it's been released
				}
				break;

			case JumpState.Hanging:
				// Conditions to exit hang phase:
				// 1. Landed on the floor.
				// 2. Hang time expired.
				// 3. Vertical velocity was externally disrupted.
				if (onFloor || _jumpHangTimer.TimeLeft == 0 || (Velocity.Y != _prevVelocity.Y && Velocity.Y > 0) ) // Added check for downward disruption
				{
					ResetJumpHangTimer();   // Stop hang timer
					ResetJumpHeightTimer(); // Ensure jump height timer is also reset
					_currJumpState = JumpState.NotJumping;
				}
				break;
		}

		// --- Jump Buffer Logic ---
		if (jumpJustPressed && !jumped) // Jump pressed but no jump occurred (e.g., airborne with no air jumps)
		{
			_jumpBuffered = true; // Buffer the jump input
			_jumpBufferTimer.Start(JumpBuffer / _fps); // Start buffer timer
		}

		// --- Velocity Calculations ---
		double vx = Velocity.X; // Current horizontal velocity
		double vy = Velocity.Y; // Current vertical velocity

		// --- Apply Gravity ---
		// Apply if not grounded and not currently dashing (dash overrides gravity)
		if (_currState != State.Grounded && _dashTimer.TimeLeft == 0)
		{
			vy += Gravity * delta;
		}

		// --- Apply Jump Hang Anti-Gravity ---
		// If in hang state, apply a force counteracting gravity to make the player float
		if (_currJumpState == JumpState.Hanging)
		{
			vy -= HangTimeSmooth * delta; // HangTimeSmooth reduces downward acceleration
		}

		// --- Ground Movement ---
		if (_currState == State.Grounded)
		{
			if (leftPressed && !rightPressed) // Moving left
			{
				vx -= GetRunAcceleration(-Velocity.X) * delta; // Accelerate left
				if (Velocity.X >= -MaxRunSpeed) // Clamp to max speed if moving left or decelerating from right
					vx = Math.Max(-MaxRunSpeed, vx);
			}
			else if (rightPressed && !leftPressed) // Moving right
			{
				vx += GetRunAcceleration(Velocity.X) * delta;  // Accelerate right
				if (Velocity.X <= MaxRunSpeed) // Clamp to max speed if moving right or decelerating from left
					vx = Math.Min(MaxRunSpeed, vx);
			}
			else // No horizontal input, apply friction
			{
				vx += GetGroundFriction(Velocity.X) * delta; // Apply ground friction
				if (Math.Abs(vx) < _epsilonSpeed) // If speed is negligible, stop completely
					vx = 0;
			}
		}

		// --- Air Movement ---
		if (_currState == State.Airborne) // Includes Coyote state for movement purposes
		{
			if (leftPressed && !rightPressed) // Moving left in air
			{
				vx -= GetAirRunAcceleration(-Velocity.X) * delta; // Accelerate left
				if (Velocity.X >= -MaxAirRunSpeed) // Clamp to max air speed
					vx = Math.Max(-MaxAirRunSpeed, vx);
			}
			else if (rightPressed && !leftPressed) // Moving right in air
			{
				vx += GetAirRunAcceleration(Velocity.X) * delta;  // Accelerate right
				if (Velocity.X <= MaxAirRunSpeed) // Clamp to max air speed
					vx = Math.Min(MaxAirRunSpeed, vx);
			}
			else // No horizontal input in air, apply air friction
			{
				vx += GetAirFriction(Velocity.X) * delta;    // Apply air friction
				if (Math.Abs(vx) < _epsilonSpeed) // If speed is negligible, stop completely
					vx = 0;
			}
		}

		// --- Dashing ---
		// Dashing overrides other movement calculations for its duration.
		if (dashJustPressed && _dashesLeft > 0)
		{
			int dirX = 0; // Horizontal dash direction component
			int dirY = 0; // Vertical dash direction component
			if (leftPressed) dirX--;
			if (rightPressed) dirX++;
			if (upPressed) dirY--;
			if (downPressed) dirY++;

			// Dash only if a direction key was pressed
			if (!(dirX == 0 && dirY == 0))
			{
				_dashTimer.Start(DashDuration / _fps); // Start dash timer
				_currJumpState = JumpState.NotJumping; // Cancel any current jump state
				_dashesLeft--;                         // Consume a dash charge
				Vector2 dashDirection = new Vector2(dirX, dirY).Normalized(); // Get normalized direction vector
				vx = dashDirection.X * DashVelocity;   // Set horizontal dash velocity
				vy = dashDirection.Y * DashVelocity;   // Set vertical dash velocity
			}
		}

		// --- Update Velocity and Move ---
		Velocity = new Vector2((float)vx, (float)vy); // Apply calculated velocities
		_prevVelocity = Velocity;                     // Store current velocity for next frame's comparison
		MoveAndSlide();                               // Godot's built-in movement and collision handling
	}

	/// <summary>
	/// Handles interactions with environmental elements based on collisions.
	/// Called every physics frame.
	/// </summary>
	private void HandleEnviornment(double delta) 
	{
		// Iterate through all collisions that occurred in the last MoveAndSlide() call
		for (int i = 0; i < GetSlideCollisionCount(); i++) 
		{
			KinematicCollision2D collision = GetSlideCollision(i); // Get collision data
			Node collider = collision.GetCollider() as Node; // Get the colliding node

			if (collider == null) continue; // Skip if collider is not a valid Node

			// Check if the collided object is a TerrainLayer to apply its friction
			if (collider is TerrainLayer terrainLayer) 
			{ // Assumes TerrainLayer is a custom C# class
				// TODO: Potentially check if this is a floor collision (e.g., collision.GetNormal().Dot(Vector2.Up) > 0.7f)
				GroundFriction = terrainLayer.GroundFriction; // Update player's ground friction
			}
			// Check if the collided object is a LaunchPad to apply its launch effect
			else if(collider is LaunchPad launchPad) 
			{ // Assumes LaunchPad is a custom C# class
				Vector2 normDir = launchPad.Direction.Normalized(); // Get launchpad's normalized direction
				// Directly set velocity based on launchpad properties
				Velocity = new Vector2((float)(normDir.X*launchPad.LaunchVelocity), (float)(normDir.Y*launchPad.LaunchVelocity));
			}
			// Check if the collided object is a MovingPlatform to apply its friction
			else if(collider is MovingPlatform movingPlatform) 
			{ // Assumes MovingPlatform is a custom C# class
				// TODO: Potentially check if this is a floor collision
				GroundFriction = movingPlatform.GroundFriction; // Update player's ground friction based on platform
			}
		}
	}


	// --- Control Functions (Jump, Timers) ---
	
	/// <summary>
	/// Initiates a jump.
	/// </summary>
	/// <param name="bhop">True if this is a bunny hop (jump immediately on landing), false otherwise.</param>
	private void StartJump(bool bhop) 
	{
		ResetJumpBufferTimer(); // Consume any buffered jump
		_jumpHeightTimer.Start(MaxJumpHeldTime / _fps); // Start timer for variable jump height based on hold duration

		float newX = bhop ? Velocity.X * (float)BHopBoostFactor : Velocity.X; // Apply bhop speed boost if applicable
		// Set initial upward velocity for the jump based on MaxJumpHeight and Gravity
		// v = sqrt(2 * g * h)
		Velocity = new Vector2(newX, (float)-Math.Sqrt(2 * Gravity * MaxJumpHeight));
	}

	/// <summary>
	/// Resets the jump buffer flag and stops its timer.
	/// </summary>
	private void ResetJumpBufferTimer() 
	{
		_jumpBuffered = false;
		_jumpBufferTimer.Stop();
	}

	/// <summary>
	/// Stops the coyote timer.
	/// </summary>
	private void ResetCoyoteTimer() 
	{
		_coyoteTimer.Stop();
	}

	/// <summary>
	/// Stops the jump hang timer.
	/// </summary>
	private void ResetJumpHangTimer() 
	{
		_jumpHangTimer.Stop();
	}

	/// <summary>
	/// Stops the jump height (hold) timer.
	/// </summary>
	private void ResetJumpHeightTimer() 
	{
		_jumpHeightTimer.Stop();
	}

	/// <summary>
	/// Stops the dash timer and applies a velocity reduction.
	/// </summary>
	private void ResetDashTimer() 
	{
		_dashTimer.Stop();
		// Reduce velocity when dash ends to provide a distinct end feel
		Velocity = new Vector2(Velocity.X * (float)DashEndRatio, Velocity.Y * (float)DashEndRatio);
	}


	// --- Movement Calculation Helper Functions ---

	/// <summary>
	/// Calculates running acceleration based on current velocity.
	/// Assumes attempted movement is in the positive direction relative to the player's facing.
	/// </summary>
	/// <param name="velocity">Current horizontal velocity in the direction of attempted movement.</param>
	/// <returns>The calculated acceleration value.</returns>
	private double GetRunAcceleration(double velocity) 
	{
		// If already at or exceeding max run speed in the direction of input, no more acceleration.
		if(velocity >= MaxRunSpeed) 
			return 0;

		// Acceleration required to reach MaxRunSpeed in TimeToMaxRunSpeed frames.
		// a = v_max / t_frames_to_v_max * physics_fps (to convert frame time to seconds for acceleration unit)
		return MaxRunSpeed / TimeToMaxRunSpeed * _fps;
	}

	/// <summary>
	/// Calculates ground friction force (as deceleration).
	/// </summary>
	/// <param name="velocity">Current horizontal velocity.</param>
	/// <returns>The friction value (negative acceleration).</returns>
	private double GetGroundFriction(double velocity) 
	{
		// Friction is proportional to velocity and opposes motion.
		return -GroundFriction * velocity;
	}

	/// <summary>
	/// Calculates air running acceleration based on current velocity.
	/// Assumes attempted movement is in the positive direction relative to the player's facing.
	/// </summary>
	/// <param name="velocity">Current horizontal velocity in the direction of attempted movement.</param>
	/// <returns>The calculated acceleration value.</returns>
	private double GetAirRunAcceleration(double velocity) 
	{
		// If already at or exceeding max air run speed in the direction of input, no more acceleration.
		if(velocity >= MaxAirRunSpeed) // Note: Original code had MaxRunSpeed here, corrected to MaxAirRunSpeed
			return 0;

		// Acceleration required to reach MaxAirRunSpeed in TimeToMaxAirRunSpeed frames.
		return MaxAirRunSpeed / TimeToMaxAirRunSpeed * _fps;
	}

	/// <summary>
	/// Calculates air friction force (as deceleration) when no movement input is given.
	/// </summary>
	/// <param name="velocity">Current horizontal velocity.</param>
	/// <returns>The friction value (negative acceleration).</returns>
	private double GetAirFriction(double velocity) 
	{
		// Air friction is proportional to velocity and opposes motion.
		return -AirFriction * velocity;
	}

	/// <summary>
	/// Calculates the new upward velocity for a variable height jump that was cut short.
	/// </summary>
	/// <param name="dist">The vertical distance already traveled since the jump started.</param>
	/// <param name="mult">The proportion of MaxJumpHeldTime that the jump button was actually held (0.0 to 1.0).</param>
	/// <returns>The new target upward velocity (will be negative or zero).</returns>
	private double GetJumpNewVelocity(double dist, double mult) 
	{
		// Calculate the target height based on how long the jump button was held.
		double targetHeightBasedOnHold = MaxJumpHeight * mult;
		// Determine the remaining height to reach this new target height.
		double remainingHeight = Math.Max(0, targetHeightBasedOnHold - dist);
		// Calculate the velocity needed to reach this remaining height: v = sqrt(2 * g * h_remaining)
		// Result is negative because upward velocity is negative in this coordinate system.
		return -Math.Sqrt(2 * Gravity * remainingHeight);
	}
}
