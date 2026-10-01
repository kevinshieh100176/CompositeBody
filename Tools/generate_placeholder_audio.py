"""Generate the placeholder audio beds for the experience.

These are deliberately crude: a low bed, a water wash, a furniture knock and a
muffled unintelligible voice, synthesised from noise and sines. They exist so
that every beat's sound layer can be wired, levelled and spatialised now, and
each clip swapped for a real recording later without touching any scene.

Nothing here is meant to survive to the venue. Run from the repo root:

    py Tools/generate_placeholder_audio.py

Writes 48kHz 16-bit mono WAVs into CompositeBody/Assets/Audio/Placeholder/.
"""

import math
import os
import random
import struct
import wave

SR = 48000
OUT_DIR = os.path.join("CompositeBody", "Assets", "Audio", "Placeholder")


def write_wav(name, samples):
    os.makedirs(OUT_DIR, exist_ok=True)
    path = os.path.join(OUT_DIR, name)

    peak = max(abs(s) for s in samples) or 1.0
    # Leave headroom; these get mixed and spatialised, and a placeholder that
    # clips reads as a broken asset rather than a stand-in.
    gain = 0.72 / peak

    frames = bytearray()
    for s in samples:
        v = int(max(-1.0, min(1.0, s * gain)) * 32767)
        frames += struct.pack("<h", v)

    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(bytes(frames))

    print(f"  {name}  {len(samples) / SR:.2f}s")


def lowpass(samples, cutoff_hz):
    """One-pole lowpass. Crude, but it is what turns white noise into water."""
    dt = 1.0 / SR
    rc = 1.0 / (2 * math.pi * cutoff_hz)
    alpha = dt / (rc + dt)
    out = []
    prev = 0.0
    for s in samples:
        prev += alpha * (s - prev)
        out.append(prev)
    return out


def highpass(samples, cutoff_hz):
    low = lowpass(samples, cutoff_hz)
    return [s - l for s, l in zip(samples, low)]


def noise(n, seed):
    rng = random.Random(seed)
    return [rng.uniform(-1.0, 1.0) for _ in range(n)]


def ambience_low(duration=12.0, seed=1):
    """
    Loopable low bed for O-0. Every partial is given a whole number of cycles
    across the clip, so the end meets the start and the loop does not click.
    """
    n = int(SR * duration)
    out = [0.0] * n

    for base_hz, amp in ((38.0, 1.00), (55.0, 0.55), (73.0, 0.32), (109.0, 0.14)):
        cycles = round(base_hz * duration)
        hz = cycles / duration
        phase = random.Random(seed + cycles).uniform(0, 2 * math.pi)
        for i in range(n):
            out[i] += amp * math.sin(2 * math.pi * hz * i / SR + phase)

    # Slow swell, also whole-cycle so it loops.
    for i in range(n):
        swell = 0.75 + 0.25 * math.sin(2 * math.pi * 2 * i / n)
        out[i] *= swell

    rumble = lowpass(noise(n, seed + 99), 120.0)
    for i in range(n):
        out[i] += rumble[i] * 1.8

    # Taper only the first and last few milliseconds against DC offset at the seam.
    edge = int(SR * 0.004)
    for i in range(edge):
        k = i / edge
        out[i] *= k
        out[n - 1 - i] *= k

    return out


def distant_water(duration=3.2, seed=2):
    """A wash of water heard through a wall: lowpassed noise, bursty envelope."""
    n = int(SR * duration)
    src = lowpass(noise(n, seed), 900.0)
    out = []
    for i in range(n):
        t = i / SR
        env = 0.35 + 0.65 * abs(math.sin(2 * math.pi * 0.7 * t + 0.4))
        env *= min(1.0, t / 0.25) * min(1.0, (duration - t) / 0.5)
        out.append(src[i] * env)
    return out


def distant_furniture(duration=1.8, seed=3):
    """Something heavy set down in another room: a low thud plus a short scrape."""
    n = int(SR * duration)
    scrape = highpass(lowpass(noise(n, seed), 2600.0), 400.0)
    out = []
    for i in range(n):
        t = i / SR
        thud = math.sin(2 * math.pi * 74.0 * t) * math.exp(-t * 11.0)
        scrape_env = math.exp(-max(0.0, t - 0.06) * 6.0) if t > 0.03 else 0.0
        out.append(thud * 1.0 + scrape[i] * scrape_env * 0.45)
    return out


def distant_voice(duration=2.6, seed=4):
    """
    Someone talking two rooms away. Narrow band of noise, amplitude modulated at
    a syllable rate: it reads unmistakably as speech while carrying no words,
    which is exactly what O-0 asks for -- 'but the content cannot be made out'.
    """
    n = int(SR * duration)
    band = highpass(lowpass(noise(n, seed), 820.0), 280.0)
    rng = random.Random(seed + 7)

    # Syllable gate: alternating voiced runs and gaps, lengths jittered.
    gate = [0.0] * n
    i = 0
    while i < n:
        voiced = rng.random() < 0.72
        length = int(SR * rng.uniform(0.07, 0.19))
        level = rng.uniform(0.55, 1.0) if voiced else 0.0
        for j in range(i, min(n, i + length)):
            gate[j] = level
        i += length

    gate = lowpass(gate, 22.0)  # smooth the gate so syllables do not click

    out = []
    for i in range(n):
        t = i / SR
        fade = min(1.0, t / 0.2) * min(1.0, (duration - t) / 0.4)
        out.append(band[i] * gate[i] * fade)
    return out


def main():
    print("Generating placeholder audio into", OUT_DIR)
    write_wav("O0_AmbienceLow_loop.wav", ambience_low())
    write_wav("Distant_Water.wav", distant_water())
    write_wav("Distant_Furniture.wav", distant_furniture())
    write_wav("Distant_Voice.wav", distant_voice())
    print("Done. These are placeholders -- replace with real recordings.")


if __name__ == "__main__":
    main()
