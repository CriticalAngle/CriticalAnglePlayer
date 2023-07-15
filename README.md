# CriticalAnglePlayer

## About
After many iterations of player controllers using Rigidbodies and CharacterControllers, this solution has been by-far my most elegant and simplistic. It mostly revolves around basic physics equations to be as realistic as possible.

#### _NOTE: THIS PACKAGE USES THE NEW INPUT SYSTEM_

## Installation
1. Download the latest unitypackage from the releases tab
2. Open your Unity project
3. Install the new Input System package from the Package Manager window
4. Double-click on the unitypackage to import the package
5. Import all

## Usage
There is a Player prefab in `CriticalAngle\Prefabs` that has everything set up already. I would recommend using that as a base and expanding from there.

The object's needed components are a Rigidbody, a Capsule Collider, and the CriticalAnglePlayer script.
The script itself requires a reference to the camera, which is a child of the GameObject, and a ground mask.
Any object that you want the player to walk on MUST have the same layer as the layer selected in the ground mask.

## How it works
It is recommended to read the following sections to gain an understanding of how the player interacts with the world.

### Movement

### Friction

### Collisions

### Crouching

### Jumping

### Air strafing