"""Checks the synthesised clips are actually audible, in range, and loopable."""

import math
import os
import sys
import wave

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import synth

FAILURES = []


def check(name, ok, detail=""):
    if ok:
        print(f"  ok    {name}")
    else:
        print(f"  FAIL  {name} {detail}")
        FAILURES.append(name)


def peak_rms(samples):
    peak = max(abs(s) for s in samples)
    rms = math.sqrt(sum(s * s for s in samples) / len(samples))
    return peak, rms


for path, make in sorted(synth.SOUNDS.items()):
    s = make()
    peak, rms = peak_rms(s)
    # Audible: the whole point is that these replace ten clips that were silent.
    check(f"{path} not silent", rms > 0.01, f"rms={rms:.4f}")
    # Unclipped: a synthesised buffer that pins at 1.0 buzzes on a phone speaker.
    check(f"{path} unclipped", peak <= 1.0, f"peak={peak:.3f}")
    check(f"{path} has headroom", peak < 0.999, f"peak={peak:.4f}")

# Deterministic: the same call twice gives the same samples, or the bake cannot
# be reproduced and every run churns the repository.
a = synth.SOUNDS["NOS/NOS"]()
b = synth.SOUNDS["NOS/NOS"]()
check("synthesis is deterministic", a == b)

# Sirens loop, so the seam must not click. Measured against the signal's own
# largest sample-to-sample step, not against an absolute level: a siren's loop
# point lands on a zero crossing, which is exactly where the waveform moves
# fastest, so the seam step is naturally the steepest step in the buffer. An
# absolute threshold called all three of these broken when they are seamless -
# the same trap the tiling bake hit, and the same fix.
for siren_path in [p for p in synth.SOUNDS if "siren" in p]:
    s = synth.SOUNDS[siren_path]()
    seam = abs(s[0] - s[-1])
    worst = max(abs(s[i + 1] - s[i]) for i in range(len(s) - 1))
    check(f"{siren_path} seam is no worse than the waveform",
          seam <= worst * 1.5, f"seam={seam:.4f} worst in-buffer step={worst:.4f}")

# One-shots must not end mid-swing, for the same reason.
for one_shot in ["Impacts/Cars/CarImpactHigh02", "NOS/NOS", "Tires/AsphaltFlatSkid"]:
    s = synth.SOUNDS[one_shot]()
    check(f"{one_shot} decays to silence", abs(s[-1]) < 0.05, f"tail={abs(s[-1]):.4f}")

# And the files on disk match what the generator makes.
out = os.path.join(os.path.dirname(os.path.abspath(__file__)), "../../Assets/Resources/Audio/SFX")
for path in sorted(synth.SOUNDS):
    f = os.path.join(out, path + ".wav")
    if not os.path.exists(f):
        check(f"{path} written", False, "missing on disk")
        continue
    with wave.open(f, "rb") as w:
        check(f"{path} is 22050 mono 16-bit",
              w.getframerate() == synth.RATE and w.getnchannels() == 1 and w.getsampwidth() == 2,
              f"{w.getframerate()}Hz {w.getnchannels()}ch {w.getsampwidth()*8}bit")

print()
if FAILURES:
    print(f"AUDIO_SYNTH TEST FAILED: {len(FAILURES)} check(s)")
    sys.exit(1)
print("AUDIO_SYNTH TEST OK")
