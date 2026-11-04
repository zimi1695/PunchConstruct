"""Reproduce the original selection cue; do not revise its timbre without author audition."""
import math
import random
import struct
import wave
from pathlib import Path
ROOT = Path(__file__).resolve().parent.parent

def main():
    rate = 44100
    rng = random.Random(26026)
    samples = []
    for i in range(int(rate * .78)):
        t = i / rate
        value = 0.0
        for start, gain in ((.025, .65), (.19, 1.0)):
            u = t - start
            if u < 0:
                continue
            attack = 1 - math.exp(-u * 1800)
            body = math.sin(2 * math.pi * (120 * u + 9 * (1 - math.exp(-u * 45)))) * math.exp(-u * 27)
            metal = sum(a * math.sin(2 * math.pi * f * u) * math.exp(-u * decay)
                        for f, a, decay in ((690, .16, 13), (1107, .10, 18), (1783, .035, 25)))
            contact = rng.uniform(-1, 1) * .12 * math.exp(-u * 160)
            value += gain * attack * (body * .50 + metal + contact)
        if .06 < t < .19:
            u = t - .06
            value += .045 * math.sin(math.pi * u / .13) ** 2 * math.sin(2 * math.pi * (360 * u + 250 * u * u))
        value *= min(1, (.78 - t) / .04)
        samples.append(value)
    gain = .72 / max(abs(v) for v in samples)
    data = b''.join(struct.pack('<h', round(v * gain * 32767)) for v in samples)
    source = ROOT / '资料/音效素材/选人音效.wav'
    with wave.open(str(source), 'wb') as output:
        output.setparams((1, 2, rate, 0, 'NONE', 'not compressed'))
        output.writeframes(data)
    (ROOT / 'PunchConstruct/assets/audio/character_select.wav').write_bytes(source.read_bytes())
    print('Restored first selection cue: 0.78 seconds, mono PCM16, 44100 Hz.')

if __name__ == '__main__':
    main()
