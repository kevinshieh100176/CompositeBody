#ifndef COMPOSITEBODY_FOG_INCLUDED
#define COMPOSITEBODY_FOG_INCLUDED

// Height fog for the whole show, and the air the stage lights hang in.
//
// WHY THIS IS NOT A FULLSCREEN PASS
//
// Kronnect's fog guide reaches the same conclusion from the other direction: for VR it
// recommends cheap depth-based height fog plus a few volumetric lights, rather than a
// volumetric fog field. Every fullscreen route it describes -- and every one the asset store
// sells -- needs the depth texture, and this project has a specific reason to refuse that: the
// 真人 are point clouds of several hundred thousand vertices, so a depth prepass pays their
// whole vertex cost a second time, every frame, in both eyes. URP-Performant also runs
// m_MSAA 4, which a fullscreen effect forces a resolve on.
//
// So the fog is computed per fragment, in the material, exactly where Unity's own MixFog was
// already being called. That costs nothing extra to composite, needs no depth buffer, and --
// the reason it matters here -- it is CORRECT ON TRANSPARENTS. A fullscreen fog applied after
// the transparent queue paints over the membranes, the additive point clouds and the ghost
// halves instead of mixing into them, which is the limitation that ruled out the fullscreen
// options for this project. Each surface fogs itself at its own depth, so a figure seen
// through a membrane is fogged once at its own distance and once at the membrane's.
//
// THE INTEGRAL. Density falls off exponentially with height:
//
//     rho(y) = rho0 * exp(-(y - y0) / H)
//
// and the optical depth along a view ray has a closed form:
//
//     tau = rho0 * exp(-(eye.y - y0)/H) * L * (1 - exp(-k)) / k,   k = (p.y - eye.y) / H
//
// Two exponentials and a divide, no march. The alternative -- sampling the density at the
// fragment and multiplying by distance -- gives a fog layer with a visibly flat lid when you
// look along it, because it has no idea what the ray passed through on the way.
//
// THE LIGHTS. Stock fog does not respond to light at all; that is the gap this fills. Up to
// four fixtures are registered as globals, and the fog colour picks up each one near its
// CLOSEST APPROACH to the view ray, not at the fragment and not at the ray's midpoint. That
// distinction is the whole effect: it means looking PAST a lamp through haze picks up its
// colour, which is what gives each 真人 a 光圈 in the air beyond the cone itself.
//
// Globals, never material properties: these are the state of the room, shared by every shader
// in it, and must not become something a material can disagree about. Set by CompositeFogZone.
// Unset, they are all zero, _CompositeFogColor.a is zero, and every function below falls
// straight through to Unity's stock fog -- so a scene with no zone in it behaves exactly as it
// did before this file existed.

float4 _CompositeFogColor;          // rgb = the colour of the air, a = master strength (0 = off)
float4 _CompositeFogParams;         // x = density at base, y = 1/falloff height, z = base height y0, w = max opacity
float4 _CompositeFogLightPos[4];    // xyz = world position, w = radius in metres
float4 _CompositeFogLightColor[4];  // rgb = colour, a = strength
float  _CompositeFogLightCount;

/// Optical depth between the eye and a world-space point. Not clamped to an opacity here,
/// because the attenuating and mixing forms below want it raw.
float CompositeFogOpticalDepth(float3 positionWS)
{
    float3 eye = _WorldSpaceCameraPos;
    float3 toPoint = positionWS - eye;
    float L = length(toPoint);
    if (L < 1e-5) return 0.0;

    float invH = max(_CompositeFogParams.y, 1e-4);
    float rho0 = _CompositeFogParams.x;
    float y0 = _CompositeFogParams.z;

    // Density at the eye's own height. Below the base this grows without bound, which is right
    // for an exponential profile and wrong for a renderer, so it is capped -- a camera that
    // drops a long way under the floor should not take the whole frame to white.
    float atEye = rho0 * exp(-clamp((eye.y - y0) * invH, -8.0, 32.0));

    // (1 - exp(-k)) / k, the shape term, with its removable singularity at k = 0 filled in by
    // hand: a ray travelling level through the layer samples one constant density, and the
    // limit of the quotient is exactly 1.
    float k = clamp((positionWS.y - eye.y) * invH, -32.0, 32.0);
    float shape = abs(k) < 1e-4 ? 1.0 : (1.0 - exp(-k)) / k;

    return clamp(atEye * L * shape, 0.0, 32.0);
}

/// The colour of the air along this ray, including whatever the fixtures put into it.
half3 CompositeFogColorAlong(float3 positionWS)
{
    half3 tint = _CompositeFogColor.rgb;

    int count = (int)_CompositeFogLightCount;
    if (count <= 0) return tint;

    float3 ro = _WorldSpaceCameraPos;
    float3 rd = positionWS - ro;
    float L = length(rd);
    if (L < 1e-5) return tint;
    rd /= L;

    [loop]
    for (int i = 0; i < count; i++)
    {
        float4 lamp = _CompositeFogLightPos[i];
        float4 col = _CompositeFogLightColor[i];

        // Closest approach of the ray to the lamp, clamped to the segment the eye can see.
        // Sampling at the fragment would only light the air touching a surface; sampling at
        // the ray's midpoint would make the glow slide around as the far geometry changes.
        float t = clamp(dot(lamp.xyz - ro, rd), 0.0, L);
        float3 closest = ro + rd * t;
        float3 offset = lamp.xyz - closest;

        float r2 = max(lamp.w * lamp.w, 1e-6);
        float w = saturate(1.0 - dot(offset, offset) / r2);
        tint += col.rgb * (w * w * col.a);
    }

    return tint;
}

/// Opacity of the air between the eye and a point, 0 clear and 1 opaque.
float CompositeFogOpacity(float3 positionWS)
{
    float tau = CompositeFogOpticalDepth(positionWS);
    return saturate((1.0 - exp(-tau)) * _CompositeFogParams.w) * _CompositeFogColor.a;
}

/// Drop-in replacement for MixFog on an ordinary surface. Falls through to Unity's fog when no
/// zone is driving the globals, so the stock URP shaders in a scene stay in agreement.
half3 CompositeFogMix(half3 color, float3 positionWS, half unityFogCoord)
{
    if (_CompositeFogColor.a <= 0.0) return MixFog(color, unityFogCoord);
    return lerp(color, CompositeFogColorAlong(positionWS), CompositeFogOpacity(positionWS));
}

/// The same, for a pass that blends additively.
///
/// Additive light can only be ATTENUATED by distance, never mixed toward the fog colour: a lerp
/// would add the colour of the air on top of the scene wherever the beam was faintest, which is
/// the exact opposite of what distance should do to it. Lerping toward black is the same
/// operation as multiplying by transmittance, and going through MixFogColor in the fallback
/// keeps the keyword guard URP puts around its fog -- the bare intensity helper returns zero
/// when fog is off, which would delete the beam outright.
half3 CompositeFogAttenuate(half3 color, float3 positionWS, half unityFogCoord)
{
    if (_CompositeFogColor.a <= 0.0) return MixFogColor(color, half3(0.0, 0.0, 0.0), unityFogCoord);
    return color * (1.0 - CompositeFogOpacity(positionWS));
}

#endif // COMPOSITEBODY_FOG_INCLUDED
