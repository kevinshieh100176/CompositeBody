using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// Paints a 4x4 atlas of love-themed spray-paint graffiti (letters, hearts, arrows ...) with soft
// spray edges, speckled overspray and running drips. Used by the CompositeBody/GraffitiWall shader.
public static class GraffitiAtlasGenerator
{
    const int Tile = 512;
    const int Grid = 4;
    const bool EnableDrips = false;   // running paint under the strokes; off for now
    const string OutPath = "Assets/Textures/Graffiti/LoveGraffitiAtlas.png";

    delegate float Sdf(Vector2 p);

    class Layer
    {
        public Sdf Shape;
        public float HalfWidth;   // stroke half width in tile units; 0 for solid shapes (signed distance)
        public Color Fill;
        public float Outline;     // extra outline width, 0 for none
        public Color OutlineColor;
    }

    class Piece
    {
        public List<Layer> Layers = new List<Layer>();
        public bool Drips;
        public float Seed;
    }

    // ---------------------------------------------------------------- geometry helpers

    static float Seg(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 pa = p - a, ba = b - a;
        float h = Mathf.Clamp01(Vector2.Dot(pa, ba) / Vector2.Dot(ba, ba));
        return (pa - ba * h).magnitude;
    }

    // Distance to the arc of radius r around c, running counter-clockwise from a0 to a1 (radians).
    static float Arc(Vector2 p, Vector2 c, float r, float a0, float a1)
    {
        Vector2 d = p - c;
        float ang = Mathf.Atan2(d.y, d.x);
        float span = Mathf.Repeat(a1 - a0, Mathf.PI * 2f);
        float rel = Mathf.Repeat(ang - a0, Mathf.PI * 2f);
        if (rel <= span)
            return Mathf.Abs(d.magnitude - r);
        Vector2 e0 = c + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * r;
        Vector2 e1 = c + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * r;
        return Mathf.Min((p - e0).magnitude, (p - e1).magnitude);
    }

    static float Circle(Vector2 p, Vector2 c, float r)
    {
        return Mathf.Abs((p - c).magnitude - r);
    }

    static float Dot2(Vector2 v) { return Vector2.Dot(v, v); }

    // Signed distance to a heart (negative inside), tip at the origin. iq's formulation.
    static float HeartRaw(Vector2 p)
    {
        p.x = Mathf.Abs(p.x);
        if (p.y + p.x > 1f)
            return Mathf.Sqrt(Dot2(p - new Vector2(0.25f, 0.75f))) - 0.35355339f;
        float m = 0.5f * Mathf.Max(p.x + p.y, 0f);
        float d = Mathf.Sqrt(Mathf.Min(Dot2(p - new Vector2(0f, 1f)), Dot2(p - new Vector2(m, m))));
        return d * Mathf.Sign(p.x - p.y);
    }

    // Heart centred around c, roughly 2s wide and 1.8s tall. Signed distance.
    static float Heart(Vector2 p, Vector2 c, float s)
    {
        Vector2 h = (p - c) / s * 0.6f + new Vector2(0f, 0.5f);
        return HeartRaw(h) / 0.6f * s;
    }

    static Vector2 Rot(Vector2 v, float a)
    {
        float c = Mathf.Cos(a), s = Mathf.Sin(a);
        return new Vector2(c * v.x - s * v.y, s * v.x + c * v.y);
    }

    static float Hash(float x, float y, float seed)
    {
        float h = Mathf.Sin(x * 127.1f + y * 311.7f + seed * 74.7f) * 43758.5453f;
        return h - Mathf.Floor(h);
    }

    static float ValueNoise(float x, float y, float seed)
    {
        float ix = Mathf.Floor(x), iy = Mathf.Floor(y);
        float fx = x - ix, fy = y - iy;
        fx = fx * fx * (3f - 2f * fx);
        fy = fy * fy * (3f - 2f * fy);
        float a = Hash(ix, iy, seed), b = Hash(ix + 1, iy, seed);
        float c = Hash(ix, iy + 1, seed), d = Hash(ix + 1, iy + 1, seed);
        return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
    }

    // ---------------------------------------------------------------- stroke font
    // Glyphs live in a box 0.8 wide and 1 tall. '_' is an empty slot.

    static float Glyph(Vector2 g, char ch)
    {
        const float pi = Mathf.PI;
        switch (ch)
        {
            case 'L':
                return Mathf.Min(Seg(g, V(0, 1), V(0, 0)), Seg(g, V(0, 0), V(0.75f, 0)));
            case 'O':
                return Mathf.Min(Mathf.Min(Arc(g, V(0.4f, 0.6f), 0.4f, 0, pi), Arc(g, V(0.4f, 0.4f), 0.4f, pi, 2 * pi)),
                                 Mathf.Min(Seg(g, V(0, 0.4f), V(0, 0.6f)), Seg(g, V(0.8f, 0.4f), V(0.8f, 0.6f))));
            case 'V':
                return Mathf.Min(Seg(g, V(0, 1), V(0.4f, 0)), Seg(g, V(0.4f, 0), V(0.8f, 1)));
            case 'E':
                return Mathf.Min(Mathf.Min(Seg(g, V(0, 0), V(0, 1)), Seg(g, V(0, 1), V(0.75f, 1))),
                                 Mathf.Min(Seg(g, V(0, 0.5f), V(0.6f, 0.5f)), Seg(g, V(0, 0), V(0.75f, 0))));
            case 'I':
                return Mathf.Min(Seg(g, V(0.4f, 0), V(0.4f, 1)),
                                 Mathf.Min(Seg(g, V(0.15f, 1), V(0.65f, 1)), Seg(g, V(0.15f, 0), V(0.65f, 0))));
            case 'U':
                return Mathf.Min(Mathf.Min(Seg(g, V(0, 1), V(0, 0.4f)), Seg(g, V(0.8f, 1), V(0.8f, 0.4f))),
                                 Arc(g, V(0.4f, 0.4f), 0.4f, pi, 2 * pi));
            case 'F':
                return Mathf.Min(Seg(g, V(0, 0), V(0, 1)),
                                 Mathf.Min(Seg(g, V(0, 1), V(0.75f, 1)), Seg(g, V(0, 0.5f), V(0.6f, 0.5f))));
            case 'R':
                return Mathf.Min(Mathf.Min(Seg(g, V(0, 0), V(0, 1)), Seg(g, V(0, 1), V(0.4f, 1))),
                                 Mathf.Min(Mathf.Min(Arc(g, V(0.4f, 0.74f), 0.26f, -pi / 2, pi / 2), Seg(g, V(0.4f, 0.48f), V(0, 0.48f))),
                                           Seg(g, V(0.35f, 0.48f), V(0.8f, 0))));
            case 'X':
                return Mathf.Min(Seg(g, V(0, 0), V(0.8f, 1)), Seg(g, V(0, 1), V(0.8f, 0)));
            case '+':
                return Mathf.Min(Seg(g, V(0.4f, 0.25f), V(0.4f, 0.75f)), Seg(g, V(0.1f, 0.5f), V(0.7f, 0.5f)));
            case '<':
                return Mathf.Min(Seg(g, V(0.8f, 0.9f), V(0.1f, 0.5f)), Seg(g, V(0.1f, 0.5f), V(0.8f, 0.1f)));
            case '3':
                return Mathf.Min(Mathf.Min(Arc(g, V(0.35f, 0.74f), 0.26f, -pi / 2, pi / 2), Seg(g, V(0.35f, 1f), V(0.05f, 1f))),
                                 Mathf.Min(Arc(g, V(0.35f, 0.26f), 0.26f, -pi / 2, pi / 2), Seg(g, V(0.35f, 0f), V(0.05f, 0f))));
            default:
                return 10f;
        }
    }

    static Vector2 V(float x, float y) { return new Vector2(x, y); }

    // Hand-lettered word: each glyph gets its own small wobble and tilt.
    static float Word(Vector2 p, string s, Vector2 centre, float h, float seed)
    {
        float adv = 1.1f * h;
        float total = (s.Length - 1) * adv + 0.8f * h;
        float x0 = centre.x - total * 0.5f + 0.4f * h;
        float best = 10f;
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '_') continue;
            float dx = (Hash(i, 1, seed) - 0.5f) * 0.06f * h;
            float dy = (Hash(i, 2, seed) - 0.5f) * 0.09f * h;
            float rot = (Hash(i, 3, seed) - 0.5f) * 0.14f;
            Vector2 gc = new Vector2(x0 + i * adv + dx, centre.y + dy);
            Vector2 g = Rot(p - gc, -rot) / h + new Vector2(0.4f, 0.5f);
            best = Mathf.Min(best, Glyph(g, s[i]) * h);
        }
        return best;
    }

    static float Star(Vector2 p, Vector2 c, float r)
    {
        float d = 10f;
        for (int k = 0; k < 5; k++)
        {
            float a0 = Mathf.PI / 2 + k * 1.2566371f;
            float a1 = Mathf.PI / 2 + (k + 2) * 1.2566371f;
            d = Mathf.Min(d, Seg(p, c + r * V(Mathf.Cos(a0), Mathf.Sin(a0)), c + r * V(Mathf.Cos(a1), Mathf.Sin(a1))));
        }
        return d;
    }

    static float Arrow(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 dir = (b - a).normalized;
        Vector2 n = new Vector2(-dir.y, dir.x);
        float d = Seg(p, a, b);
        d = Mathf.Min(d, Seg(p, b, b - dir * 0.09f + n * 0.06f));
        d = Mathf.Min(d, Seg(p, b, b - dir * 0.09f - n * 0.06f));
        for (int k = 0; k < 2; k++)
        {
            Vector2 f = a + dir * (0.015f + k * 0.07f);
            d = Mathf.Min(d, Seg(p, f, f + dir * 0.07f + n * 0.055f));
            d = Mathf.Min(d, Seg(p, f, f + dir * 0.07f - n * 0.055f));
        }
        return d;
    }

    // ---------------------------------------------------------------- piece definitions

    static Layer L(Sdf shape, float hw, Color fill, float outline, Color outlineColor)
    {
        // Fat, single-colour airbrush strokes with no hard outline: the outline arguments are kept so the piece
        // definitions stay readable, but they are ignored.
        return new Layer { Shape = shape, HalfWidth = hw * 0.9f, Fill = fill, Outline = 0f, OutlineColor = outlineColor };
    }

    static Color C(float r, float g, float b) { return new Color(r, g, b, 1f); }

    static readonly Color Pink = new Color(1f, 0.30f, 0.58f, 1f);
    static readonly Color HotRed = new Color(0.86f, 0.07f, 0.16f, 1f);
    static readonly Color White = new Color(0.97f, 0.96f, 0.93f, 1f);
    static readonly Color Black = new Color(0.04f, 0.04f, 0.06f, 1f);
    static readonly Color Cyan = new Color(0.12f, 0.80f, 0.88f, 1f);
    static readonly Color Yellow = new Color(1f, 0.82f, 0.12f, 1f);
    static readonly Color Violet = new Color(0.55f, 0.35f, 0.95f, 1f);

    static List<Piece> BuildPieces()
    {
        var pieces = new List<Piece>();
        Vector2 mid = V(0.5f, 0.5f);

        // 0: LOVE
        var p = new Piece { Drips = true, Seed = 1 };
        p.Layers.Add(L(q => Word(q, "LOVE", V(0.5f, 0.5f), 0.2f, 1f), 0.032f, Pink, 0.014f, White));
        p.Layers.Add(L(q => Heart(q, V(0.83f, 0.8f), 0.06f), 0f, HotRed, 0.01f, White));
        pieces.Add(p);

        // 1: I (heart) U
        p = new Piece { Drips = true, Seed = 2 };
        p.Layers.Add(L(q => Word(q, "I_U", V(0.5f, 0.5f), 0.26f, 2f), 0.034f, White, 0.014f, Black));
        p.Layers.Add(L(q => Heart(q, V(0.5f, 0.52f), 0.12f), 0f, HotRed, 0.014f, Black));
        pieces.Add(p);

        // 2: XOXO
        p = new Piece { Drips = true, Seed = 3 };
        p.Layers.Add(L(q => Word(q, "XOXO", V(0.5f, 0.5f), 0.2f, 3f), 0.034f, HotRed, 0.014f, White));
        pieces.Add(p);

        // 3: FOR / EVER
        p = new Piece { Drips = true, Seed = 4 };
        p.Layers.Add(L(q => Mathf.Min(Word(q, "FOR", V(0.5f, 0.68f), 0.2f, 4f), Word(q, "EVER", V(0.5f, 0.32f), 0.2f, 5f)),
                       0.03f, Cyan, 0.013f, Black));
        pieces.Add(p);

        // 4: heart pierced by an arrow
        p = new Piece { Drips = true, Seed = 5 };
        p.Layers.Add(L(q => Mathf.Abs(Heart(q, V(0.5f, 0.52f), 0.3f)), 0.03f, Pink, 0.012f, White));
        p.Layers.Add(L(q => Arrow(q, V(0.08f, 0.2f), V(0.92f, 0.8f)), 0.02f, White, 0.012f, Black));
        pieces.Add(p);

        // 5: solid stencil heart
        p = new Piece { Drips = true, Seed = 6 };
        p.Layers.Add(L(q => Heart(q, mid, 0.34f), 0f, HotRed, 0f, HotRed));
        pieces.Add(p);

        // 6: <3
        p = new Piece { Drips = true, Seed = 7 };
        p.Layers.Add(L(q => Word(q, "<3", V(0.5f, 0.5f), 0.42f, 7f), 0.04f, Pink, 0.015f, White));
        pieces.Add(p);

        // 7: two hearts and a speck
        p = new Piece { Drips = false, Seed = 8 };
        p.Layers.Add(L(q => Mathf.Abs(Heart(q, V(0.4f, 0.45f), 0.22f)), 0.03f, HotRed, 0.012f, White));
        p.Layers.Add(L(q => Mathf.Abs(Heart(q, V(0.7f, 0.72f), 0.13f)), 0.024f, Pink, 0.01f, White));
        p.Layers.Add(L(q => Heart(q, V(0.64f, 0.22f), 0.07f), 0f, Pink, 0.008f, White));
        pieces.Add(p);

        // 8: infinity
        p = new Piece { Drips = true, Seed = 9 };
        p.Layers.Add(L(q => Mathf.Min(Circle(q, V(0.32f, 0.5f), 0.18f), Circle(q, V(0.68f, 0.5f), 0.18f)),
                       0.03f, Violet, 0.012f, White));
        p.Layers.Add(L(q => Heart(q, V(0.5f, 0.84f), 0.06f), 0f, Pink, 0.01f, White));
        pieces.Add(p);

        // 9: heart with rays
        p = new Piece { Drips = false, Seed = 10 };
        p.Layers.Add(L(q => Mathf.Abs(Heart(q, V(0.5f, 0.4f), 0.26f)), 0.03f, HotRed, 0.012f, White));
        p.Layers.Add(L(q => Mathf.Min(Seg(q, V(0.5f, 0.78f), V(0.5f, 0.92f)),
                              Mathf.Min(Seg(q, V(0.28f, 0.76f), V(0.2f, 0.88f)), Seg(q, V(0.72f, 0.76f), V(0.8f, 0.88f)))),
                       0.02f, Yellow, 0.01f, Black));
        pieces.Add(p);

        // 10: I + U inside a heart
        p = new Piece { Drips = true, Seed = 11 };
        p.Layers.Add(L(q => Mathf.Abs(Heart(q, V(0.5f, 0.5f), 0.4f)), 0.03f, Pink, 0.012f, White));
        p.Layers.Add(L(q => Word(q, "I+U", V(0.5f, 0.56f), 0.22f, 12f), 0.026f, White, 0.011f, Black));
        pieces.Add(p);

        // 11: LOVE, black stencil
        p = new Piece { Drips = true, Seed = 12 };
        p.Layers.Add(L(q => Word(q, "LOVE", V(0.5f, 0.5f), 0.2f, 13f), 0.036f, Black, 0f, Black));
        pieces.Add(p);

        // 12: smiley with heart eyes
        p = new Piece { Drips = false, Seed = 13 };
        p.Layers.Add(L(q => Mathf.Min(Circle(q, mid, 0.34f), Arc(q, mid, 0.19f, 3.49f, 5.93f)), 0.028f, Yellow, 0.012f, Black));
        p.Layers.Add(L(q => Mathf.Min(Heart(q, V(0.39f, 0.58f), 0.05f), Heart(q, V(0.61f, 0.58f), 0.05f)), 0f, HotRed, 0.008f, Black));
        pieces.Add(p);

        // 13: star with a heart in the middle
        p = new Piece { Drips = false, Seed = 14 };
        p.Layers.Add(L(q => Star(q, V(0.5f, 0.54f), 0.34f), 0.026f, Yellow, 0.012f, Black));
        p.Layers.Add(L(q => Heart(q, V(0.5f, 0.52f), 0.09f), 0f, HotRed, 0.01f, White));
        pieces.Add(p);

        // 14: big XO
        p = new Piece { Drips = true, Seed = 15 };
        p.Layers.Add(L(q => Word(q, "XO", V(0.5f, 0.5f), 0.4f, 16f), 0.05f, White, 0.018f, HotRed));
        pieces.Add(p);

        // 15: scattered little hearts
        p = new Piece { Drips = false, Seed = 16 };
        p.Layers.Add(L(q => Heart(q, V(0.3f, 0.7f), 0.08f), 0f, Pink, 0.008f, White));
        p.Layers.Add(L(q => Heart(q, V(0.62f, 0.62f), 0.12f), 0f, HotRed, 0.01f, White));
        p.Layers.Add(L(q => Heart(q, V(0.78f, 0.3f), 0.06f), 0f, White, 0.008f, HotRed));
        p.Layers.Add(L(q => Heart(q, V(0.25f, 0.3f), 0.07f), 0f, HotRed, 0.008f, White));
        p.Layers.Add(L(q => Heart(q, V(0.5f, 0.26f), 0.09f), 0f, Pink, 0.01f, White));
        pieces.Add(p);

        return pieces;
    }

    // ---------------------------------------------------------------- rasteriser

    static float Cover(float d, float hw, float soft)
    {
        float t = Mathf.Clamp01((hw + soft - d) / (2f * soft));
        return t * t * (3f - 2f * t);
    }

    static void Over(float[] acc, int i, Color c, float a)
    {
        if (a <= 0f) return;
        float k = 1f - a;
        acc[i * 4 + 0] = c.r * a + acc[i * 4 + 0] * k;
        acc[i * 4 + 1] = c.g * a + acc[i * 4 + 1] * k;
        acc[i * 4 + 2] = c.b * a + acc[i * 4 + 2] * k;
        acc[i * 4 + 3] = a + acc[i * 4 + 3] * k;
    }

    // Premultiplied RGBA floats for one piece, bottom-up rows.
    static float[] Render(Piece piece)
    {
        var acc = new float[Tile * Tile * 4];
        for (int y = 0; y < Tile; y++)
        {
            for (int x = 0; x < Tile; x++)
            {
                int i = y * Tile + x;
                var p = new Vector2((x + 0.5f) / Tile, (y + 0.5f) / Tile);
                // The hand drifts: slow wobble of the whole line.
                float wob = (ValueNoise(p.x * 14f, p.y * 14f, piece.Seed) - 0.5f) * 0.018f;
                // Spray pressure varies along the stroke: some stretches are thin, some fully loaded.
                float pressure = Mathf.Lerp(0.72f, 1f, ValueNoise(p.x * 9f, p.y * 9f, piece.Seed + 2f))
                               * Mathf.Lerp(0.9f, 1f, ValueNoise(p.x * 45f, p.y * 45f, piece.Seed + 4f));
                float grain = Hash(x, y, piece.Seed + 11f);
                foreach (var layer in piece.Layers)
                {
                    float d = layer.Shape(p) + wob;
                    float dd = d - layer.HalfWidth;      // < 0 inside the stroke

                    // Airbrush density: a solid core that falls off smoothly over a wide feather,
                    // so a stroke reads as a round, soft tube rather than a flat shape.
                    const float feather = 0.018f;
                    float t = Mathf.Clamp01((dd + feather * 0.35f) / (feather * 2.2f));
                    float rho = (1f - t * t * (3f - 2f * t)) * pressure;

                    // Stochastic stipple: the thinner the paint, the sparser the dots, which gives the
                    // grainy, sandpapery spray edge instead of a smooth gradient.
                    float a = Mathf.Clamp01((rho - grain * 0.8f) / 0.2f);
                    Over(acc, i, layer.Fill, a);

                    // Overspray: a faint wide mist with a few stray droplets.
                    float haze = Mathf.Exp(-Mathf.Max(dd, 0f) / 0.07f);
                    if (dd < 0.22f)
                    {
                        Over(acc, i, layer.Fill, 0.05f * haze);
                        if (Hash(x * 1.7f, y * 1.3f, piece.Seed + 7f) < 0.35f * haze * haze)
                            Over(acc, i, layer.Fill, Mathf.Lerp(0.2f, 0.7f, Hash(x * 0.9f, y * 2.1f, piece.Seed + 9f)));
                    }
                }
            }
        }

        if (piece.Drips && EnableDrips)
            AddDrips(acc, piece.Seed);
        return acc;
    }

    // Paint running downwards from the lower edge of strokes.
    static void AddDrips(float[] acc, float seed)
    {
        var rng = new System.Random((int)(seed * 977));
        var drips = new List<Vector3>();
        for (int y = 3; y < Tile - 4; y++)
        {
            for (int x = 3; x < Tile - 3; x++)
            {
                float a = acc[(y * Tile + x) * 4 + 3];
                float below = acc[((y - 1) * Tile + x) * 4 + 3];
                if (a > 0.9f && below < 0.5f && rng.NextDouble() < 0.006 && acc[((y + 3) * Tile + x) * 4 + 3] > 0.95f)
                    drips.Add(new Vector3(x, y, 0));
            }
        }

        foreach (var d in drips)
        {
            int x = (int)d.x, y = (int)d.y;
            // take the colour from a little way up inside the stroke, not from its outline
            int src = (Mathf.Min(y + 10, Tile - 1) * Tile + x) * 4;
            float a0 = acc[src + 3];
            if (a0 <= 0f) continue;
            Color col = new Color(acc[src] / a0, acc[src + 1] / a0, acc[src + 2] / a0, 1f);
            float len = Mathf.Lerp(30f, 230f, Mathf.Pow((float)rng.NextDouble(), 1.6f));
            float w = Mathf.Lerp(2.5f, 5f, (float)rng.NextDouble());
            float sway = (float)(rng.NextDouble() - 0.5) * 0.02f;
            for (int k = 0; k <= len + w * 1.5f; k++)
            {
                int yy = y - k;
                if (yy < 0) break;
                float cx = x + sway * k;
                // thin run with a fat bead at the bottom
                float run = k <= len ? Mathf.Lerp(w, w * 0.65f, k / len) : 0f;
                float bead = Mathf.Sqrt(Mathf.Max(0f, (w * 1.5f) * (w * 1.5f) - (k - len) * (k - len)));
                float r = Mathf.Max(run, bead);
                for (int xx = Mathf.FloorToInt(cx - r - 1); xx <= Mathf.CeilToInt(cx + r + 1); xx++)
                {
                    if (xx < 0 || xx >= Tile) continue;
                    float cov = Mathf.Clamp01(r - Mathf.Abs(xx - cx) + 0.5f);
                    Over(acc, yy * Tile + xx, col, cov);
                }
            }
        }
    }

    // ---------------------------------------------------------------- entry point

    [MenuItem("Tools/Graffiti/Generate Love Graffiti Atlas")]
    public static void Generate()
    {
        var pieces = BuildPieces();
        int size = Tile * Grid;
        var pixels = new Color32[size * size];

        for (int n = 0; n < pieces.Count && n < Grid * Grid; n++)
        {
            EditorUtility.DisplayProgressBar("Love graffiti", "Painting piece " + (n + 1) + "/" + pieces.Count, n / (float)pieces.Count);
            float[] acc = Render(pieces[n]);
            Color fallback = pieces[n].Layers[0].Fill;
            int ox = (n % Grid) * Tile, oy = (Grid - 1 - n / Grid) * Tile; // tile 0 sits top-left
            for (int y = 0; y < Tile; y++)
            {
                for (int x = 0; x < Tile; x++)
                {
                    int i = (y * Tile + x) * 4;
                    float a = acc[i + 3];
                    Color c = a > 0.002f ? new Color(acc[i] / a, acc[i + 1] / a, acc[i + 2] / a) : fallback;
                    // The atlas is sRGB, so store gamma-space colours.
                    pixels[(oy + y) * size + ox + x] = new Color32(
                        (byte)Mathf.RoundToInt(Mathf.Clamp01(c.r) * 255f),
                        (byte)Mathf.RoundToInt(Mathf.Clamp01(c.g) * 255f),
                        (byte)Mathf.RoundToInt(Mathf.Clamp01(c.b) * 255f),
                        (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f));
                }
            }
        }
        EditorUtility.ClearProgressBar();

        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.SetPixels32(pixels);
        Directory.CreateDirectory(Path.GetDirectoryName(OutPath));
        File.WriteAllBytes(OutPath, tex.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(tex);

        AssetDatabase.ImportAsset(OutPath, ImportAssetOptions.ForceUpdate);
        var importer = (TextureImporter)AssetImporter.GetAtPath(OutPath);
        importer.textureType = TextureImporterType.Default;
        importer.sRGBTexture = true;
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = true;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.filterMode = FilterMode.Trilinear;
        importer.anisoLevel = 8;
        importer.maxTextureSize = 2048;
        importer.textureCompression = TextureImporterCompression.CompressedHQ;
        importer.SaveAndReimport();
        Debug.Log("Love graffiti atlas written to " + OutPath);
    }
}
