# About Multiplayer Play Mode

Use Multiplayer Play Mode to test multiplayer functionality across multiple Players without leaving your development environment. This package enables quick creation of complex Play mode scenarios by simulating multiple Editor instances and adding additional local instances simultaneously, accelerating iteration speed for faster development and testing of multiplayer features.

## Compatibility

Multiplayer Play Mode version 2.0 is compatible with the following:
* Unity Editor versions 6000.3.0b4 or later.
* Windows and MacOS platforms.

# Technical details

## Requirements

This version of Multiplayer Play Mode is compatible with the following Unity versions and platforms:

* Unity 6 and later
* Windows, Mac platforms

## Limitations

Multiplayer Play Mode has some inherent technical limitations, specifically around [scale](#scale) and [authoring](#authoring).

### Scale

Multiplayer Play Mode is designed for small-scale, local testing environments that can only support up to four total editor Players (the Editor Player and up to three additional editor instances). Up to four locally built instances can also be added.

### Authoring
Editor instances are essentially behaving as ligthweight editor instances. To maximise their performances, they are kept in synch with the main editor and have a lesser amount of abilities. Specifically:
- You can't create or change the properties of GameObjects in an Editor Instance. Instead, use the main Editor Player to make changes and an Editor instance to test multiplayer functionality. Any changes you make in Play Mode in the main Editor Player reset when you exit Play Mode.
- You can't access any main Editor Player functionality from Virtual Player.
- The package manager is non functional in Editor Instances.
