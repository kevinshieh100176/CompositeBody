# Unity SDK WebGL support

The Vivox Unity WebGL SDK is available starting at version 16.5.0.

This SDK is available at a Limited level of support. For more information on this support level, refer to the [Supported platforms and versions](supported-platforms.md) page.

## Known Vivox WebGL limitations

* Players can only join a single channel per instance.
* There is no support for display names.
* Only 2D (non-positional) channels are supported. No 3D (positional) voice channel support.
* There are no participant-updated events; this covers events for audio energy, `is_talking`, or mute states.
* There is limited audio device functionality due to web browser limitations.


## Build a Unity project for WebGL

Review the [prerequisites](https://docs.unity3d.com/Manual/webgl-gettingstarted.html) for building a Unity project for WebGL in the Unity Editor documentation on [building for WebGL](https://docs.unity3d.com/Manual/webgl-intro.html).

When ready to build your application, set the build target to WebGL: **File > Build Settings > WebGL > Build** (or Build and Run).

## Best practices

* If your Vivox application is using a feature that’s not implemented in the WebGL SDK, a NotImplementedException is thrown to ensure compiler compatibility while helping to identify limited functionality.
* For local testing purposes, turn off compression in the WebGL build: **Player Settings > Publish Settings > Compression > Disabled**.
* CORS (Cross-Origin Resource Sharing) messages are expected in the local browser. Connect by using HTTPS instead of HTTP.
* Firefox is the recommended browser for local testing.
