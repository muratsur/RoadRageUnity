"""Cuts the recorded crash from the Road Rage 3D project (roadrage3d/audio/crash.wav)
into three lengths for the game's three hit strengths:

  crash_real_heavy.ogg   the whole crash, impact to the last debris (~2.1 s)
  crash_real_medium.ogg  the impact and first crunch (0.9 s, faded)
  crash_real_light.ogg   just the impact (0.35 s, faded)

    python3 Tools/Audio/import_crash_recording.py ../roadrage3d/audio/crash.wav

Writes Assets/Resources/Audio/CrashReal/, 48 kHz stereo Vorbis.
"""
import os
import sys

import numpy as np
import soundfile as sf

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "..", "..", "Assets", "Resources", "Audio", "CrashReal")
SR = 48000


def main(path):
    a, sr = sf.read(path, always_2d=True)
    if sr != SR:
        t = np.arange(0, len(a) * SR / sr) * sr / SR
        a = np.stack([np.interp(t, np.arange(len(a)), a[:, c]) for c in range(a.shape[1])], 1)
    # Trim the silence after the last debris.
    level = np.abs(a).max(1)
    end = int(np.nonzero(level > 0.01)[0][-1]) + int(0.08 * SR)
    a = a[:end]
    os.makedirs(OUT, exist_ok=True)
    for name, seconds, fade in (("heavy", None, 0.08), ("medium", 0.9, 0.25), ("light", 0.35, 0.12)):
        clip = a if seconds is None else a[:int(seconds * SR)]
        clip = clip.copy()
        n = int(fade * SR)
        clip[-n:] *= np.linspace(1, 0, n)[:, None]
        clip /= max(1e-6, np.abs(clip).max()) / 0.95
        out = os.path.join(OUT, f"crash_real_{name}.ogg")
        sf.write(out, clip.astype(np.float32), SR, format="OGG", subtype="VORBIS")
        print("RR_SOUND", os.path.relpath(out, os.path.join(HERE, "..", "..")), f"{len(clip) / SR:.2f}s")
    print("RR_DONE")


if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else os.path.join(HERE, "..", "..", "..", "roadrage3d", "audio", "crash.wav"))
