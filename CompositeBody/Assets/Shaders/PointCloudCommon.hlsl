#ifndef COMPOSITEBODY_POINTCLOUD_COMMON_INCLUDED
#define COMPOSITEBODY_POINTCLOUD_COMMON_INCLUDED

// Shared point maths for CompositeBody/PointCloud.
//
// Lives in an include because the colour pass and the depth pass have to displace points
// identically. If they drift, the depth pass writes the cloud where the colour pass is not,
// and the figure self-occludes along the difference -- which reads as holes torn in the body
// rather than as a depth bug.
//
// The stereo macros deliberately stay in the .shader passes rather than moving here, because
// VerifyStereoShaders checks for them per HLSLPROGRAM block and would not see them through an
// include.

// --------------------------------------------------------------------------------------
// Per-point randomness
//
// Derived from the point's own object-space position, so it is stable: the same point gets the
// same number every frame, on both headsets, with nothing stored per point and nothing sent
// over the network. A per-frame random would make the cloud boil, and a per-draw random would
// make the two players watch different dissolves.
// --------------------------------------------------------------------------------------
float PointHash(float3 p)
{
    float3 q = frac(p * float3(0.1031, 0.1030, 0.0973));
    q += dot(q, q.yzx + 33.33);
    return frac((q.x + q.y) * q.z);
}

// --------------------------------------------------------------------------------------
// Idle turbulence
//
// Three sines, one per axis, each reading a different component of the position so the motion
// does not collapse onto a diagonal. The per-point hash is added to the phase, which is what
// stops neighbours moving in step -- without it the body wobbles as one solid object instead of
// shimmering.
//
// Driven by an explicit phase rather than by _Time. _Time starts when the scene loads, so two
// headsets that joined seconds apart would animate out of step, and the figure is something
// both players look at together. PointCloudFigure feeds this from ExperienceClock, the same
// shared clock DistantSoundBed uses to keep its cues identical on both machines.
// --------------------------------------------------------------------------------------
float3 PointTurbulence(float3 positionOS, float hash, float phase, float amplitude,
                       float spatialScale, float speed)
{
    if (amplitude <= 0.00001) return float3(0, 0, 0);

    float3 q = positionOS * spatialScale;
    float t = phase * speed;
    float jitter = hash * 6.2831853;

    float3 offset;
    offset.x = sin(q.y + t * 1.00 + jitter);
    offset.y = sin(q.z + t * 0.83 + jitter * 1.3);
    offset.z = sin(q.x + t * 1.17 + jitter * 0.7);
    return offset * amplitude;
}

// --------------------------------------------------------------------------------------
// Spatial ordering, shared by reveal and dissolve
//
// Returns 0 at the origin point and 1 at the radius, so a sweep of the control from 0 to 1
// crosses the cloud outward from wherever the origin is. Radius 0 means no spatial order at
// all, and the whole cloud acts together.
// --------------------------------------------------------------------------------------
float PointSpatialOrder(float3 positionOS, float3 origin, float radius)
{
    if (radius <= 0.0001) return 0.0;
    return saturate(length(positionOS - origin) / radius);
}

// --------------------------------------------------------------------------------------
// How far gone a point is: 0 still present, 1 fully departed.
//
// Spatial order and the per-point hash are mixed rather than used alone. Pure spatial order
// gives a hard shell creeping across the body; pure hash gives an even static fizz with no
// direction. The blend is what reads as the body coming apart from somewhere.
// --------------------------------------------------------------------------------------
float PointDeparture(float order, float hash, float amount, float scatter)
{
    if (amount <= 0.0001) return 0.0;

    float threshold = lerp(order, hash, saturate(scatter));
    // The 0.25 window is how long one point takes to leave once its turn comes, as a fraction
    // of the whole control. Narrower reads as a hard edge, wider as fog.
    return saturate((amount * 1.25 - threshold) / 0.25);
}

// --------------------------------------------------------------------------------------
// Where a departing point has drifted to.
//
// Three parts, and all three are needed: a push along a chosen direction, so the dissolve can
// travel up an arm or across to another figure rather than just expanding; a push outward from
// the origin, so the body opens up instead of sliding sideways; and turbulence that grows with
// departure, so points lose their place in the surface as they go instead of marching in
// formation.
// --------------------------------------------------------------------------------------
float3 PointDrift(float3 positionOS, float3 origin, float hash, float phase,
                  float gone, float distance, float3 direction, float spread)
{
    if (gone <= 0.0001 || distance <= 0.00001) return float3(0, 0, 0);

    float3 outward = positionOS - origin;
    float len = length(outward);
    outward = len > 0.0001 ? outward / len : float3(0, 1, 0);

    float3 along = direction;
    float dirLen = length(along);
    along = dirLen > 0.0001 ? along / dirLen : float3(0, 0, 0);

    // Eased so a point accelerates away rather than starting at full speed.
    float travel = gone * gone * distance;

    float3 wander = PointTurbulence(positionOS * 1.7, hash, phase, spread, 2.3, 0.6) * gone;
    return (along + outward * 0.6) * travel + wander;
}

// --------------------------------------------------------------------------------------
// Glitch
//
// The script asks for the 真人 to return 「有膜／Glitch的感覺」, so this is content, not an
// effect pass bolted on: it is how the figures read as memory rather than as people.
//
// Three things make displaced points read as a *digital* fault rather than as wind:
//
//   Bands, not points. The body is cut into horizontal slabs and a whole slab moves together,
//   which is what a corrupted scanline looks like. Displacing points individually gives noise;
//   displacing slabs gives damage.
//
//   Steps, not slides. Time is quantised to _GlitchRate, so offsets jump between held frames.
//   Anything continuous reads as motion, and motion is the one thing a glitch is not. At rate
//   0 the whole effect freezes on _GlitchSeed, which is the still "misplaced" version.
//
//   Intermittence. Only slabs whose own hash falls under the amount move at all, so a low
//   amount corrupts a few bands rather than shaking everything slightly. That is the
//   difference between a broken image and a blurred one.
//
// Chromatic separation is faked. A point carries one colour, so real RGB splitting would mean
// drawing the cloud three times; instead a displaced slab is tinted by the direction it moved,
// which gives the same red/cyan fringing along the tear for one multiply.
// --------------------------------------------------------------------------------------
float GlitchHash(float a, float b, float seed)
{
    float3 p = float3(a, b, seed) * float3(0.1031, 0.1030, 0.0973);
    p = frac(p);
    p += dot(p, p.yzx + 19.19);
    return frac((p.x + p.y) * p.z);
}

void PointGlitch(float3 basePosition, float hash, float phase,
                 float amount, float slabCount, float shift, float dropout,
                 float scatter, float rate, float seed,
                 out float3 offset, out float dropped, out float chroma)
{
    offset = float3(0, 0, 0);
    dropped = 0.0;
    chroma = 0.0;

    if (amount <= 0.0001) return;

    // Held frames. floor() of a scaled phase is what makes the offsets jump rather than
    // travel; rate 0 holds frame 0 forever, which is the static version.
    float frame = rate > 0.0001 ? floor(phase * rate) : 0.0;
    float slab = floor(basePosition.y * slabCount);

    float pick = GlitchHash(slab, frame, seed);
    if (pick > amount) return;                 // this band is intact this frame

    // Remapped so a band that only just qualifies still moves a useful distance, instead of
    // the whole effect fading in from nothing as the amount rises.
    float energy = saturate(pick / max(amount, 0.0001));

    float dx = GlitchHash(slab, frame + 17.0, seed) * 2.0 - 1.0;
    float dz = GlitchHash(slab, frame + 41.0, seed) * 2.0 - 1.0;

    // Lateral only. Vertical displacement slides bands into each other and reads as melting;
    // keeping the tear in the horizontal plane keeps the figure's silhouette legible, which
    // matters because the audience has to still recognise the person.
    offset.x = dx * shift;
    offset.z = dz * shift * 0.6;

    // A few points inside a corrupted band fly much further, which stops a displaced slab
    // reading as a solid object that merely moved.
    float spark = GlitchHash(hash * 977.0, frame, seed + 3.7);
    if (spark > 0.93)
        offset += (float3(GlitchHash(hash, frame, seed + 11.0),
                          GlitchHash(hash, frame, seed + 23.0),
                          GlitchHash(hash, frame, seed + 37.0)) - 0.5) * scatter;

    float drop = GlitchHash(slab, frame + 73.0, seed);
    dropped = step(drop, dropout * amount);

    chroma = dx * (1.0 - energy * 0.5);
}

// Red/cyan fringing from the direction a band tore.
//
// The channels are *scaled*, not offset. Adding a flat amount paints the fringe on at full
// strength regardless of what is underneath, so a figure in dark trousers comes back with
// bands of pure red across the legs -- which reads as colour bars, not as a torn image.
// Scaling keeps dark material dark and lets the fringe show where there is already light to
// shift, which is how channel separation actually behaves.
float3 GlitchTint(float3 rgb, float chroma, float strength)
{
    if (strength <= 0.0001) return rgb;
    float c = clamp(chroma, -1.0, 1.0) * strength;
    float3 gain = float3(1.0 + max(c, 0.0),
                         1.0 - abs(c) * 0.25,
                         1.0 + max(-c, 0.0));
    return rgb * gain;
}

#endif // COMPOSITEBODY_POINTCLOUD_COMMON_INCLUDED
