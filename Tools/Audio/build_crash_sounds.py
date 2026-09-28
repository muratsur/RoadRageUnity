"""Builds the car crash sounds from CC0 recordings, by layering them the way crash
sound design is usually done: a heavy body thump, a sheet-metal crunch pitched down
to car size, breaking glass on the big hits, and a debris tail of metal settling.

Sources, all CC0 (public domain), from github.com/lavenderdotpet/CC0-Public-Domain-Sounds:
  kenney_impactsounds/            Kenney "Impact Sounds" (kenney.nl), CC0
  100-CC0-wood-metal-SFX/         "100 CC0 wood and metal SFX" (OpenGameArt), CC0
  75-cc0-breaking-falling-hit-sfx "75 CC0 breaking/falling/hit SFX" (OpenGameArt), CC0

    git clone --depth 1 --filter=blob:none --sparse \\
        https://github.com/lavenderdotpet/CC0-Public-Domain-Sounds cc0
    (cd cc0 && git sparse-checkout set kenney_impactsounds 100-CC0-wood-metal-SFX \\
        75-cc0-breaking-falling-hit-sfx)
    pip install soundfile numpy
    RR_CC0=cc0 python3 Tools/Audio/build_crash_sounds.py

Writes Assets/Resources/Audio/CrashCC0/crash_{heavy,medium,light}_N.ogg, 44.1 kHz mono.
"""
import os
import random

import numpy as np
import soundfile as sf

HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.environ.get("RR_CC0", "cc0")
OUT = os.path.join(HERE, "..", "..", "Assets", "Resources", "Audio", "CrashCC0")
SR = 44100
rng = random.Random(500)


def load(rel):
    a, sr = sf.read(os.path.join(SRC, rel), always_2d=True)
    a = a.mean(axis=1)
    if sr != SR:
        a = np.interp(np.arange(0, len(a) * SR / sr) * sr / SR, np.arange(len(a)), a)
    return a / max(1e-6, np.abs(a).max())


def pitch(a, factor):
    """Resample: factor < 1 lowers the pitch and stretches the sound - a small
    metal hit becomes a car door's worth of metal."""
    n = int(len(a) / factor)
    return np.interp(np.arange(n) * factor, np.arange(len(a)), a)


def lowpass(a, cutoff):
    spec = np.fft.rfft(a)
    f = np.fft.rfftfreq(len(a), 1 / SR)
    spec *= 1 / np.sqrt(1 + (f / cutoff) ** 4)
    return np.fft.irfft(spec, len(a))


def highpass(a, cutoff):
    spec = np.fft.rfft(a)
    f = np.fft.rfftfreq(len(a), 1 / SR)
    spec *= 1 - 1 / np.sqrt(1 + (f / cutoff) ** 4)
    return np.fft.irfft(spec, len(a))


def thump(seconds=0.35, freq=52.0):
    """The weight of the car: a short low body hit under the metal."""
    t = np.arange(int(seconds * SR)) / SR
    f = freq * (1 + 0.8 * np.exp(-t * 30))
    body = np.sin(2 * np.pi * np.cumsum(f) / SR) * np.exp(-t * 14)
    return body * np.minimum(1, t * 400)


def mix(layers, length):
    out = np.zeros(int(length * SR))
    for sound, delay, gain in layers:
        s = int(delay * SR)
        n = min(len(sound), len(out) - s)
        if n > 0:
            out[s:s + n] += sound[:n] * gain
    # Soft limiting keeps the transient's punch without clipping.
    out = np.tanh(out * 1.3) / np.tanh(1.3)
    fade = int(0.05 * SR)
    out[-fade:] *= np.linspace(1, 0, fade)
    return out / max(1e-6, np.abs(out).max()) * 0.9


def pick(names):
    return load(rng.choice(names))


KMETAL = [f"kenney_impactsounds/Audio/impactMetal_heavy_00{i}.ogg" for i in range(5)]
SHEET = [f"100-CC0-wood-metal-SFX/metal_sheet_0{i}.ogg" for i in range(1, 7)] + \
        ["100-CC0-wood-metal-SFX/metal_slam_01.ogg"]
HIT = [f"75-cc0-breaking-falling-hit-sfx/bfh1_metal_hit_0{i}.ogg" for i in range(1, 7)] + \
      [f"100-CC0-wood-metal-SFX/metal_hit_0{i}.ogg" for i in range(1, 6)]
GLASS = [f"75-cc0-breaking-falling-hit-sfx/bfh1_glass_breaking_0{i}.ogg" for i in range(1, 7)]
DEBRIS = [f"75-cc0-breaking-falling-hit-sfx/bfh1_metal_falling_0{i}.ogg" for i in range(1, 6)] + \
         ["100-CC0-wood-metal-SFX/metal_falling_01.ogg", "100-CC0-wood-metal-SFX/metal_falling_02.ogg"]
BREAK = [f"75-cc0-breaking-falling-hit-sfx/bfh1_breaking_0{i}.ogg" for i in range(1, 4)]


def heavy():
    return mix([
        (thump(0.45, rng.uniform(42, 55)), 0.0, 0.9),
        (lowpass(pitch(pick(KMETAL), rng.uniform(0.45, 0.6)), 2500), 0.0, 0.8),
        (pitch(pick(SHEET), rng.uniform(0.62, 0.75)), 0.005, 0.75),
        (pitch(pick(SHEET), rng.uniform(0.8, 0.95)), 0.045, 0.45),
        (highpass(pick(GLASS), 1500), rng.uniform(0.03, 0.07), 0.45),
        (pitch(pick(BREAK), 0.8), 0.06, 0.3),
        (lowpass(pitch(pick(DEBRIS), rng.uniform(0.7, 0.85)), 6000), rng.uniform(0.22, 0.32), 0.35),
    ], 1.9)


def medium():
    return mix([
        (thump(0.3, rng.uniform(55, 68)), 0.0, 0.7),
        (lowpass(pitch(pick(KMETAL), rng.uniform(0.55, 0.7)), 3000), 0.0, 0.7),
        (pitch(pick(SHEET), rng.uniform(0.72, 0.88)), 0.004, 0.7),
        (pitch(pick(HIT), rng.uniform(0.7, 0.85)), 0.05, 0.3),
        (lowpass(pitch(pick(DEBRIS), 0.9), 5000), rng.uniform(0.18, 0.26), 0.18),
    ], 1.2)


def light():
    return mix([
        (thump(0.18, rng.uniform(70, 85)), 0.0, 0.5),
        (lowpass(pitch(pick(KMETAL), rng.uniform(0.7, 0.85)), 3500), 0.0, 0.6),
        (pitch(pick(HIT), rng.uniform(0.75, 0.9)), 0.003, 0.55),
    ], 0.7)


def main():
    os.makedirs(OUT, exist_ok=True)
    for name, make, count in (("heavy", heavy, 4), ("medium", medium, 4), ("light", light, 3)):
        for i in range(count):
            path = os.path.join(OUT, f"crash_{name}_{i}.ogg")
            sf.write(path, make().astype(np.float32), SR, format="OGG", subtype="VORBIS")
            print("RR_SOUND", os.path.relpath(path, os.path.join(HERE, "..", "..")))
    print("RR_DONE")


if __name__ == "__main__":
    main()
