#ifndef COMPOSITEBODY_OATRAIL_NOISE_INCLUDED
#define COMPOSITEBODY_OATRAIL_NOISE_INCLUDED

// Value noise for the ObjectAssembly trail. Arithmetic hash ("hash without sine"), so it gives
// the same pattern on every GPU and needs no noise texture in the build.

float OATrailHash12(float2 p)
{
    float3 p3 = frac(float3(p.xyx) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}

/// Smooth value noise in [0,1] with a quintic fade, so the scroll has no visible lattice steps.
float OATrailValueNoise(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    float2 u = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);

    float a = OATrailHash12(i);
    float b = OATrailHash12(i + float2(1.0, 0.0));
    float c = OATrailHash12(i + float2(0.0, 1.0));
    float d = OATrailHash12(i + float2(1.0, 1.0));

    return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
}

/// Two octaves: a broad current and a finer ripple riding on it.
float OATrailFbm(float2 p)
{
    return OATrailValueNoise(p) * 0.65 + OATrailValueNoise(p * 2.13 + float2(7.7, 3.1)) * 0.35;
}

#endif // COMPOSITEBODY_OATRAIL_NOISE_INCLUDED
