# AERO volumetric fog — Single Pass Instanced fix

AERO (`Assets/Mirza/AERO - Volumetric Fog and Mist`) is **gitignored**: it is a 58 MB paid Asset
Store package. It therefore has to be re-imported by hand on a fresh clone — and when it is, it
arrives **broken in VR** until the two edits below are reapplied.

This file exists because the fix lives inside the package's own files. Re-importing or updating
AERO overwrites them silently.

## The symptom

In the headset the whole frame is a flat ~50% grey. Non-XR captures of the same scene look
correct, which is what makes it confusing. Turning fog density almost to zero does **not** clear
the grey — that is the tell, and it is how this was diagnosed: if density near zero still shows
grey, the fog is not what you are looking at.

## The cause

AERO's shader graph declares a hidden texture property `_BlitTexture`, which is where the fog
reads the camera image from. It is declared as a plain 2D texture.

Under Single Pass Instanced stereo, URP binds a **two-slice texture array** there instead, one
slice per eye. The binding fails silently and the shader is handed Unity's default grey texture.

This project enforces Single Pass Instanced (`VerifyStereoShaders`), so the fix is required, not
optional.

## The fix — two files

### 1. `Shaders/Volumetric Fog.shadergraph`

Remove the `_BlitTexture` property so the graph stops declaring it:

- delete the `Texture2DShaderProperty` object with guid **`b67fa33b96bc4f30a0274ca9be4a508b`**
- delete its **two** references from the graph's property lists

Verify with `grep -c b67fa33b96bc4f30a0274ca9be4a508b "Volumetric Fog.shadergraph"` → must be `0`.
The file is JSON-per-line; after editing, confirm every block still parses.

### 2. `Shaders/Volumetric Fog.hlsl`

Declare the texture stereo-aware instead, just after the `ShaderGraphFunctions.hlsl` include
(around line 34):

```hlsl
TEXTURE2D_X(_BlitTexture);
```

And change the line that reads it (around line 1069) to the matching stereo-aware sampler:

```hlsl
float4 sceneColour = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uvSS);
```

`TEXTURE2D_X` and `SAMPLE_TEXTURE2D_X` resolve to a plain 2D texture when XR is off, so the
non-VR path is unchanged.

Keep the explanatory banner above the declaration — it is the only warning anyone gets before
updating the package wipes this.

## Verifying

Density near zero should give a **clear** frame in the headset, not a grey one. If it is still
grey, the shadergraph property is back.

## Current state in this project

AERO is **switched off**: the `AERO Volumetric Fog` renderer feature on
`Assets/VRMPAssets/Settings/URP-Performant-Renderer.asset` has `m_Active: 0`, and the chain is
verified by `Tools → Verify → Render Pipeline`.

Because AERO is gitignored, on a fresh clone that renderer feature will show as a **missing
script** until the package is re-imported. It is inactive, so nothing renders differently — but
do not "clean up" the missing entry, or re-enabling AERO later means rebuilding the feature and
re-pointing it at `Assets/Materials/OnboardingFog.mat` (which *is* tracked, with its tuned
values: ambient 0.25, density 0.008, max distance 5).

Turn it back on with **Tools → Onboarding → AERO Fog → Enable**.

One thing that was never finished: a headset frame-time measurement. AERO added the project's
first renderer feature, which forces an intermediate colour target and an MSAA 4 resolve per eye
at 90 Hz. That cost was never measured, and it is the reason to be careful about switching it
back on.
