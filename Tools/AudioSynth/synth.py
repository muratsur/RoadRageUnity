"""Synthesises the sound effects the game asks for and does not have.

RoadRageAudioAndVFX loads fourteen clips by path. Ten of them do not exist: the
whole Assets/Resources/Audio/SFX tree was never in the repository, so the police
sirens, the NOS, the tyre skids and the medium car impact all resolve to null and
play nothing. The four that do resolve are Vehicle Physics Pro samples, and those
stay exactly as they are - real recordings beat synthesis every time.

The synthesis is ported from make_audio.gd in the shipped Godot build, which
generates its whole effects set this way and has been through real tuning: its
engine comment records that an earlier version's noise term "read as a ticking /
sand rattle", and the fix was to drop the modulation entirely. Same rates, same
envelope shapes, same idiom.

Seeded from sha1 rather than hash(), because Python randomises string hashing per
process and a bake that cannot be reproduced is not a bake.
"""

import argparse
import hashlib
import math
import os
import struct
import wave

RATE = 22050
TAU = math.pi * 2.0


def _frames(seconds):
    """Buffer length in samples, rounded rather than truncated.

    int(RATE * seconds) is a trap: 22050 * 1.4 is 30869.999999999996 in binary
    floating point, so a 1.4 s siren came out one sample short of a whole period
    and its loop seam landed mid-slope instead of on the zero crossing. The other
    two siren lengths happened to land above their integer and were fine, which is
    exactly how this kind of bug survives - it is correct for most inputs.
    """
    return int(round(RATE * seconds))


def _rng(name):
    """Deterministic per-sound noise source."""
    seed = int(hashlib.sha1(name.encode()).hexdigest()[:8], 16)
    state = seed or 1

    def nxt():
        nonlocal state
        state = (1103515245 * state + 12345) & 0x7FFFFFFF
        return state / 0x7FFFFFFF * 2.0 - 1.0

    return nxt


def _lerp(a, b, t):
    return a + (b - a) * t


def impact(name, seconds=0.5, gain=0.95):
    """Metallic crunch, low boom and inharmonic panel clang. _impact() in make_audio.gd."""
    noise = _rng(name)
    n = _frames(seconds)
    out = []
    prev = 0.0
    for i in range(n):
        t = i / RATE
        prev = _lerp(prev, noise(), 0.65)                  # mid/high band -> crunch
        crunch = prev * math.exp(-t * 22.0)
        boom = math.sin(TAU * 48.0 * t) * math.exp(-t * 11.0)
        metal = (math.sin(TAU * 410.0 * t) * 0.5
                 + math.sin(TAU * 763.0 * t) * 0.35
                 + math.sin(TAU * 1290.0 * t) * 0.22) * math.exp(-t * 15.0)
        out.append(max(-1.0, min(1.0, crunch * 0.95 + boom * 0.8 + metal * 0.4)) * gain)
    return out


def screech(name, seconds=0.9, centre=1200.0, gain=0.5):
    """Tyre skid: band-ish filtered noise under a wavering tone. _screech()."""
    noise = _rng(name)
    n = _frames(seconds)
    out = []
    prev = 0.0
    for i in range(n):
        t = i / RATE
        env = min(t * 8.0, 1.0) * math.exp(-t * 4.0 / max(seconds, 0.1) * 0.5)
        prev = _lerp(prev, noise(), 0.5)
        tone = math.sin(TAU * (centre + 200.0 * math.sin(TAU * 30.0 * t)) * t) * 0.4
        out.append((prev * 0.6 + tone) * env * gain)
    return out


def siren(name, low=660.0, high=940.0, step=0.35, gain=0.28):
    """Hi-lo two-tone, looping. _siren().

    The loop length is four steps, so the buffer holds a whole number of
    alternations and the seam lands on a phase boundary rather than mid-tone.
    Three variants exist because three squad cars sounding identical is worse
    than one siren.
    """
    length = step * 4.0
    n = _frames(length)
    out = []
    for i in range(n):
        t = i / RATE
        f = low if int(math.fmod(t, step * 2.0) / step) == 0 else high
        v = math.sin(TAU * f * t) + math.sin(TAU * f * 2.0 * t) * 0.22
        out.append(v * gain)
    return out


def whoosh(name, seconds=0.85, gain=0.7):
    """Nitrous: a hiss that swells and falls, with the band opening as it goes.

    Not in make_audio.gd - the shipped build has no NOS - so it is built in the
    same idiom: filtered noise shaped by an envelope, no oscillator sweep, since
    a pitched sweep is what makes this read as science fiction rather than gas.
    """
    noise = _rng(name)
    n = _frames(seconds)
    out = []
    prev = 0.0
    for i in range(n):
        t = i / RATE
        p = t / seconds
        env = min(t * 12.0, 1.0) * (1.0 - p) ** 1.6
        cut = _lerp(0.10, 0.55, p)                       # band opens as it vents
        prev = _lerp(prev, noise(), cut)
        out.append(prev * env * gain)
    return out


def blowoff(name, seconds=0.4, gain=0.6):
    """Turbo blow-off: a noise chuff with a descending whistle over it."""
    noise = _rng(name)
    n = _frames(seconds)
    out = []
    prev = 0.0
    for i in range(n):
        t = i / RATE
        env = min(t * 40.0, 1.0) * math.exp(-t * 7.0)
        prev = _lerp(prev, noise(), 0.45)
        whistle = math.sin(TAU * _lerp(2600.0, 900.0, t / seconds) * t) * 0.35
        out.append((prev * 0.8 + whistle) * env * gain)
    return out


SOUNDS = {
    "Impacts/Cars/CarImpactHigh02":   lambda: impact("impact-high", 0.55, 0.95),
    "Impacts/Cars/CarImpactMedium01": lambda: impact("impact-medium", 0.36, 0.62),
    "Tires/AsphaltSkid_Sideways":     lambda: screech("skid-side", 1.10, 1200.0, 0.55),
    "Tires/AsphaltFlatSkid":          lambda: screech("skid-flat", 0.80, 980.0, 0.45),
    "Horns/Sirens/siren_1":           lambda: siren("siren-1", 660.0, 940.0, 0.35),
    "Horns/Sirens/siren_2":           lambda: siren("siren-2", 700.0, 1000.0, 0.30),
    "Horns/Sirens/siren_3":           lambda: siren("siren-3", 620.0, 880.0, 0.40),
    "NOS/NOS":                        lambda: whoosh("nos", 0.85, 0.70),
    "NOS/NOSWhoosh2":                 lambda: whoosh("nos-2", 1.15, 0.62),
    "Chargers/Turbochargers/Medium/Common/TURBO_MED_MB_01": lambda: blowoff("turbo", 0.40, 0.60),
}


def write_wav(path, samples):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    frames = b"".join(struct.pack("<h", int(max(-1.0, min(1.0, s)) * 32767)) for s in samples)
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(frames)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", default="Assets/Resources/Audio/SFX")
    args = ap.parse_args()
    for name, make in sorted(SOUNDS.items()):
        samples = make()
        path = os.path.join(args.out, name + ".wav")
        write_wav(path, samples)
        print(f"  {len(samples) / RATE:5.2f}s  {path}")
    print(f"AUDIO_SYNTH wrote {len(SOUNDS)} clips at {RATE} Hz mono")


if __name__ == "__main__":
    main()
