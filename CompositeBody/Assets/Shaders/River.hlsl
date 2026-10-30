#ifndef COMPOSITEBODY_RIVER_INCLUDED
#define COMPOSITEBODY_RIVER_INCLUDED

// Shared by every pass of CompositeBody/River.
//
// THE DISPLACEMENT LIVES HERE FOR ONE REASON: the forward pass, the depth pass and the depth-
// normals pass must move a vertex to exactly the same place. If the colour pass raises a wave and
// the depth pass does not, the river writes depth for a surface that is not where it is drawn --
// everything that samples the depth texture afterwards (the 光圈 cones, the orbit glows, the
// membrane's soft intersection) then reads the wrong distance, and the water cuts a halo through
// them. One function, included three times, cannot drift.
//
// The properties are declared in one UnityPerMaterial block here for the same reason: a cbuffer
// whose layout differs between passes is undefined behaviour, and it fails quietly.

CBUFFER_START(UnityPerMaterial)
    float4 _DeepColor;
    float4 _ShallowColor;
    float4 _FoamColor;

    float _Smoothness;
    float _SpecIntensity;
    float _DiffuseWrap;
    float _DiffuseIntensity;
    float _Fresnel0;
    float _FresnelStrength;

    float _NormalFadeStart;
    float _NormalFadeRange;
    float _NormalFadeFloor;

    float _DepthFalloff;
    float _ShallowScatter;
    float _WaveDepthScale;

    float _FlowSpeed;

    float _WaveAmplitude;
    float _WaveFreqA;
    float _WaveFreqB;
    float _WaveFreqC;
    float _WaveFadeStart;
    float _WaveFadeRange;

    float4 _NormalA_ST;
    float4 _NormalB_ST;
    float _NormalSpeedA;
    float _NormalSpeedB;
    float _NormalStrength;

    float _FoamScale;
    float _FoamSpeed;
    float _FoamCoverage;
    float _FoamSoftness;
    float _EdgeFoam;
    float _EdgeFoamSoftness;

    float _Reveal;
CBUFFER_END

// NO DEPTH-BASED SHORELINE FADE, deliberately. The transparent volumetrics in this project soften
// their intersections by comparing against _CameraDepthTexture, but that works because they are
// drawn after the opaques. The river is opaque, and URP-Performant requires the depth texture, so
// a depth prepass has already written the river's OWN depth by the time the forward pass runs:
// the comparison would come out zero everywhere and fade the water away. Softening an opaque
// shoreline needs either the transparent queue or a fade baked against known bank geometry.
// Here the edge foam does that job instead.

TEXTURE2D(_NormalA);    SAMPLER(sampler_NormalA);
TEXTURE2D(_NormalB);    SAMPLER(sampler_NormalB);
TEXTURE2D(_FoamTex);    SAMPLER(sampler_FoamTex);

// Set from RiverSpline off ExperienceClock, NOT taken from _Time. Two headsets in one room would
// otherwise each animate the water from their own session start, and two people looking at the
// same stretch would see different waves. Declared outside UnityPerMaterial because it is a
// global, not a material property.
float _RiverTime;

// uv.x runs across the river 0..1; uv.y is DISTANCE ALONG THE SPLINE IN METRES, baked by
// BuildRiver, which is what lets everything here scroll down the channel and follow its bends.
// prof is the baked profile: x = flow multiplier, y = foam amount, z = DEPTH IN METRES at this
// vertex, w = width in metres.
//
// The depth is authored, not measured. There is no riverbed in this scene, so the depth texture
// has nothing behind the surface to subtract against -- and the river is opaque, so by the time
// it shades, it is itself the nearest thing in that texture. Baking the depth is not a shortcut
// around a technique that would otherwise work here; it is the only thing that can work, and it
// has the advantage of being art-directable.

/// How far the surface rises at this vertex, in metres along its normal.
float RiverWaveHeight(float2 uv, float4 prof, float3 positionWS)
{
    float flow = max(prof.x, 0.0);
    float t = _RiverTime * _FlowSpeed * flow;

    // Three travelling components rather than one, so the surface is not a corrugation. The
    // first two run downstream at different wavelengths; the third leans across the channel so
    // the crests are not parallel bars.
    float a = sin(uv.y * _WaveFreqA - t * 2.0);
    float b = sin(uv.y * _WaveFreqB - t * 3.1 + uv.x * 6.0);
    float c = sin(uv.x * _WaveFreqC + uv.y * 0.35 - t * 0.7);

    float height = a * 0.55 + b * 0.30 + c * 0.15;

    // Faster water is choppier. Keeps a pool reading as a pool when the flow curve dips.
    height *= 0.5 + 0.5 * flow;

    // SHOALING. Waves shrink as the bed rises under them, which is why real water goes glassy at
    // the margins. Without this the surface heaves identically right up to the bank and the
    // depth in the colour is contradicted by the motion -- and motion is the cue people believe.
    height *= saturate(prof.z / max(_WaveDepthScale, 0.01));

    // Faded out with distance, because the far LOD levels have too few vertices to carry a wave
    // and would show it as faceting. The fade is a function of position only, so every pass
    // agrees on it.
    float distanceToEye = distance(positionWS, _WorldSpaceCameraPos);
    float fade = 1.0 - saturate((distanceToEye - _WaveFadeStart) / max(_WaveFadeRange, 0.01));

    return height * _WaveAmplitude * fade;
}

/// The displaced object-space position. Called identically by all three passes.
float3 RiverDisplace(float3 positionOS, float3 normalOS, float2 uv, float4 prof)
{
    // The undisplaced world position feeds the distance fade. Using the displaced one would make
    // the fade depend on its own result.
    float3 positionWS = TransformObjectToWorld(positionOS);
    float height = RiverWaveHeight(uv, prof, positionWS);
    return positionOS + normalOS * height;
}

#endif
