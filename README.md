# Godot 2D Movement Controller (C#)

## Overview

This project provides a highly customizable 2D character movement controller for Godot, written in C#. It's designed to serve as a flexible template for game developers, offering a wide array of common platformer mechanics out-of-the-box.

My focus for ths project was to implement a movement controller using programmer-friendly variables like MaxJumpHeight and TimeToMaxJumpHeight that make it intuitive to change values and expect certain results.

Additionally, I added some more features that make it easier to get started on creating environmental movment interactions.

## Core Features

* **The Player as a State Machine:** An internal state machine handles transitions between being grounded, airborne, and brief "coyote time" moments, as well as managing different phases of a jump (ascent, hang time).
* **Responsive Running & Ground Movement:**
    * Define maximum speed and the time it takes to reach it for precise acceleration control.
    * Adjustable ground friction allows for snappy stops or more slippery movement.
* **Nuanced Jumping System:**
    * **Variable Jump Height:** Players can control jump height by how long they hold the jump button.
    * **Coyote Time:** A small window after walking off a ledge where a jump can still be initiated, improving game feel.
    * **Jump Buffering:** Inputs made slightly before landing are remembered, making jumps feel more responsive.
    * **Hang Time:** A configurable period at the jump's apex where gravity's effect is lessened, allowing for more air control and a smoother jump arc.
    * **B-Hop (Bunny Hop) Capability:** Option to enable a speed boost when jumping immediately upon landing.
    * **Multiple Air Jumps (Double Jumps):** Easily set the number of jumps allowed while airborne.
* **Dynamic Dashing:**
    * Configure the number of dashes available before needing to touch the ground.
    * Set dash speed, duration, and an optional change in velocity when the dash concludes.
* **Precise Air Control:**
    * Independent settings for maximum air speed and air acceleration.
    * Configurable air friction for when no movement input is given.
* **Environmental Interactions:**
    * Support for different ground materials with different friction values.
    * Collision detection that allow objects like launchpads to interact with player movement
    * Moving platforms that move between two predetermined locations.
* **Prebuilt Gym**
    * Includes a set of preconfigured values.
    * Includes a simple gym that showcases all of the movement capabilities.

## Getting Started

Download the repository and run the playground.tscn scene.