"""Derive the accent sets for the two Midnight variants.

Each accent starts at the hue we want for it and is moved toward the variant's own text colour
until it clears WCAG AA against the surfaces it actually sits on. Moving rather than hand-picking
keeps the hue where it was chosen and makes the stopping point a measured one: the accent ends up
as close to its target as readability allows, and no closer.

The two variants pull in opposite directions. Midnight tints toward its indigo surface and lifts
its accents toward white; Midnight Light tints toward white and darkens its accents toward ink.
Both then need a separate fill dark enough to carry white text, because an accent readable as text
on its own tint cannot also carry white.

Usage: generate-midnight-accents.py [midnight|light] [--report]
"""

import sys

WHITE = (0xFF, 0xFF, 0xFF)
AA = 4.5

# Surfaces, and the direction an accent has to move to become readable on them. Midnight Light's
# values are sampled from the reference interface's own light surface rather than invented.
VARIANTS = {
    "midnight": {
        "surface": (0x15, 0x12, 0x43),
        "canvas":  (0x1C, 0x18, 0x56),
        "ink":     (0x0E, 0x0B, 0x2E),
        "toward":  WHITE,            # a dark canvas needs lighter accents
        "fill_hover": "lighter",     # the fill is darker than the accent, so hover lifts
    },
    "light": {
        "surface": (0xFF, 0xFF, 0xFF),
        "canvas":  (0xF8, 0xFA, 0xFF),
        "ink":     (0x0B, 0x1A, 0x65),   # the reference's own indigo ink
        "toward":  (0x0B, 0x1A, 0x65),   # a light canvas needs darker accents
        "fill_hover": "darker",          # here the fill already is the accent, so hover sinks
    },
}

MODE = next((a for a in sys.argv[1:] if not a.startswith("--")), "midnight")
if MODE not in VARIANTS:
    raise SystemExit(f"unknown variant {MODE}")
SURFACE = VARIANTS[MODE]["surface"]
CANVAS = VARIANTS[MODE]["canvas"]
INK = VARIANTS[MODE]["ink"]
TOWARD = VARIANTS[MODE]["toward"]
FILL_HOVER = VARIANTS[MODE]["fill_hover"]

TARGETS = {
    "Teal":    "#00D5C8",
    "Cobalt":  "#0065FE",   # the reference blue
    "Sky":     "#38BDF8",
    "Cyan":    "#22D3EE",
    "Emerald": "#10D9A0",
    "Rose":    "#F2295B",   # the reference pink
    "Violet":  "#7B4DFF",   # the reference violet
}

def un(s): s = s.lstrip("#"); return tuple(int(s[i:i + 2], 16) for i in (0, 2, 4))
def hx(c): return "#%02X%02X%02X" % c
def mix(a, b, t): return tuple(round(a[i] + (b[i] - a[i]) * t) for i in range(3))

def _lin(v):
    v /= 255.0
    return v / 12.92 if v <= 0.04045 else ((v + 0.055) / 1.055) ** 2.4

def lum(c): return 0.2126 * _lin(c[0]) + 0.7152 * _lin(c[1]) + 0.0722 * _lin(c[2])

def ratio(a, b):
    la, lb = lum(a), lum(b)
    hi, lo = max(la, lb), min(la, lb)
    return (hi + 0.05) / (lo + 0.05)

def strong(base):
    """The darkest-but-still-visible fill that white text clears.

    The accent itself cannot do this job. It has to stay light enough to read as text on the
    subtle fill, and a colour light enough for that is too light to carry white. The reference
    interface puts white on every button and on the selected destination, so a fill that can hold
    white is a separate colour - which is what it does too, pairing a #0065FE button with a deeper
    #0A44B4 navigation bar.
    """
    for step in range(0, 81):
        candidate = mix(base, INK, step / 100.0)
        # Headroom rather than bare AA: the hover state is a lighter version of this colour, and a
        # fill chosen at exactly 4.5 leaves nowhere for it to go, so hover collapses onto rest and
        # the control stops responding to the pointer. The margin buys that step back.
        if ratio(WHITE, candidate) >= AA + 0.7 and ratio(candidate, CANVAS) >= 3.0:
            return candidate
    # None rather than an error: whether a base can produce a usable fill is part of whether that
    # base is usable at all, so the lift search has to be able to reject it and carry on.
    return None

def derive(base):
    """A base colour and its companions, by the same steps the other variants use."""
    subtle = mix(SURFACE, base, 0.10)
    muted = mix(SURFACE, base, 0.28)
    on = WHITE if ratio(base, WHITE) >= ratio(base, INK) else INK
    fill = strong(base)
    if fill is None:
        return None

    # Hover and pressed move toward the accent and toward the ink respectively, but only as far as
    # white still carries: a hover state that quietly drops the label below AA is worse than a
    # flatter one, so each step is kept only if it measures.
    # Hover moves away from the page, which is lighter on a dark canvas and darker on a light one.
    # Either way it stops before the white label drops below AA.
    hover = fill
    target = base if FILL_HOVER == "lighter" else INK
    for step in range(1, 41):
        candidate = mix(fill, target, step / 100.0)
        if ratio(WHITE, candidate) < AA:
            break
        hover = candidate
    pressed = mix(fill, INK, 0.12)

    return {
        "Accent": base,
        "AccentHover": mix(base, TOWARD, 0.10),
        "AccentPressed": mix(base, TOWARD, 0.20),
        "AccentSubtle": subtle,
        "AccentMuted": muted,
        "TextOnAccent": on,
        "AccentStrong": fill,
        "AccentStrongHover": hover,
        "AccentStrongPressed": pressed,
    }

def passes(d):
    if d is None:
        return False
    return (min(ratio(d["Accent"], SURFACE),
                ratio(d["Accent"], d["AccentSubtle"]),
                ratio(d["Accent"], CANVAS)) >= AA
            and ratio(d["TextOnAccent"], d["Accent"]) >= AA)

def lift(target):
    """The smallest lift toward white that clears AA everywhere the accent is used."""
    base = un(target)
    for step in range(0, 61):
        candidate = mix(base, TOWARD, step / 100.0)
        d = derive(candidate)
        if passes(d):
            return d, step / 100.0
    raise SystemExit(f"{target} cannot be lifted into range")

ROLES = ["Accent", "AccentHover", "AccentPressed", "AccentSubtle", "AccentMuted", "TextOnAccent",
         "AccentStrong", "AccentStrongHover", "AccentStrongPressed"]

if __name__ == "__main__":
    import sys
    report = "--report" in sys.argv
    if report:
        print(f"{'accent':8} {'target':9} {'lifted':9} {'lift':>5} "
              f"{'on-surf':>8} {'on-subtle':>10} {'on-canvas':>10} {'text-on':>8} "
              f"{'strong':9} {'white':>7} {'w/hover':>7}")
    lines = []
    for name, target in TARGETS.items():
        d, amount = lift(target)
        if report:
            print(f"{name:8} {target:9} {hx(d['Accent']):9} {amount:5.0%} "
                  f"{ratio(d['Accent'], SURFACE):8.2f} {ratio(d['Accent'], d['AccentSubtle']):10.2f} "
                  f"{ratio(d['Accent'], CANVAS):10.2f} {ratio(d['TextOnAccent'], d['Accent']):8.2f} "
                  f"{hx(d['AccentStrong']):9} {ratio(WHITE, d['AccentStrong']):7.2f} "
                  f"{ratio(WHITE, d['AccentStrongHover']):7.2f}")
        for role in ROLES:
            lines.append(f'      <Color x:Key="{name}{role}Color">{hx(d[role])}</Color>')
    if not report:
        print("\n".join(lines))
