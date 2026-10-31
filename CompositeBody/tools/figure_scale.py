#!/usr/bin/env python
"""
Scale a photogrammetric capture by the room it was taken in, not by the subject's own height.

A capture of this kind carries no real-world scale, and it carries a *different* arbitrary scale
every time: across the ten 真人 captures the same two performers measure anywhere from 2.44 to
3.47 units tall. The obvious fix -- normalise each file so the figure is 1.70 m -- is wrong in a
way that is easy to miss, because it assumes every pose is the same height. These poses are not.
B4 is hunched with his head down and his crown sits about 7% lower than it does in B2; forcing
both to one height makes him 7% too big, and the error lands on the one thing a co-located piece
cannot afford to get wrong, which is how large a virtual person is next to a real one.

So measure something that does not move. All ten captures are of the same room, and in every one
of them the floor the subject stands on and the ceiling above it are the same real distance apart.
In the capture's own units that distance is a ruler, and the figure measured against it is a
pose-independent fraction of the room:

    figure_units / room_units          3.6% spread across the five A captures
    figure_units                        15% spread across the same five

The residual spread is pose, and pose is real: a bowed head *should* measure shorter.

The room's height in metres is still unknown, so one number has to come from outside. Give one
capture a figure height with --figure-height and this prints the room height that implies; feed
that back as --room-height for the rest and they all follow from the one assumption, which is
then the only thing anyone has to revisit.

    # what does A1 standing 1.70 m make the room?
    py tools/figure_scale.py --room A1_raw.ply --figure "A1 - Cloud.ply" --figure-height 1.70

    # carry that to the other captures of the same room
    py tools/figure_scale.py --room A3_raw.ply --figure "A3 - Cloud.ply" --room-height 2.285

--factor prints the scale on its own, for passing straight to clean_pointcloud.py --scale, and
--sole prints the height to crop at, for the same command's --crop.

The floor comes from the figure rather than from the room, because the subject is standing on it
and nothing else in these scans is so reliably identified. Searching the room scan for it does
not work: the floor near the subject can be a few thousand points while a surface metres away is
hundreds of thousands, and the band that wins is then not the one the subject is on -- in two of
these captures that put the floor 12 cm above the soles resting on it. The ceiling still has to
come from the room scan, since the figure does not reach it.

A hand-made crop carries a tail of stray points below the subject, so the soles are taken as the
lowest *populated* band rather than the lowest point. Where a crop has clipped the feet off
instead, no part of the figure reaches the floor and --floor supplies it.
"""

from __future__ import annotations

import argparse
import sys

import numpy as np

HEADER_END = b"end_header\n"
BIN = 0.02


def read_xyz(path, stride=1):
    """
    Positions only, straight out of the binary block.

    Both scanners here write binary_little_endian with x/y/z then uchar rgb, so the whole file
    is one fixed-width record array and NumPy can take it in a single view. The room scans are
    two million points each and this is the difference between a second and a minute.
    """
    with open(path, "rb") as f:
        blob = f.read()
    cut = blob.find(HEADER_END)
    if cut < 0:
        raise ValueError(f"{path}: no PLY header")
    cut += len(HEADER_END)
    head = blob[:cut].decode("ascii", "replace")
    if "format binary_little_endian" not in head:
        raise ValueError(f"{path}: expected a binary little-endian PLY")
    try:
        n = next(int(l.split()[-1]) for l in head.splitlines()
                 if l.startswith("element vertex"))
    except StopIteration:
        raise ValueError(f"{path}: no vertex element")

    scalar = "<f8" if "property double x" in head else "<f4"
    props = [l.split()[-1] for l in head.splitlines() if l.startswith("property ")]
    if props[:3] != ["x", "y", "z"]:
        raise ValueError(f"{path}: expected x, y, z first, got {props[:3]}")
    fields = [("x", scalar), ("y", scalar), ("z", scalar)]
    fields += [(p, "u1") for p in props[3:]]           # rgb, and anything else trailing it
    rec = np.frombuffer(blob, dtype=np.dtype(fields), count=n, offset=cut)[::stride]
    return np.stack([rec["x"], rec["y"], rec["z"]], axis=1).astype(np.float64)


def densest_band(y, lo, hi):
    """Centre of the most populated BIN-wide slice of y within [lo, hi], and its count."""
    sel = y[(y >= lo) & (y <= hi)]
    if len(sel) < 50:
        return None, 0
    counts = np.bincount(((sel - lo) / BIN).astype(int))
    i = int(np.argmax(counts))
    return lo + (i + 0.5) * BIN, int(counts[i])


def sole_level(y, band=0.01, depth=0.40, share=0.30):
    """
    The lowest band of the figure that holds a surface rather than a scatter.

    Thresholded against the busiest band in the bottom `depth` instead of an absolute count, so
    it reads the same on a 200,000-point crop as on a 500,000-point one.
    """
    lo = float(y.min())
    counts = np.bincount(((y[y < lo + depth] - lo) / band).astype(int))
    return lo + int(np.argmax(counts >= share * counts.max())) * band


def measure(room_path, figure_path, floor=None, verbose=True):
    """
    Figure height and room height, both in the capture's own units.

    The top of the figure is a 99.98th percentile rather than the maximum: one stray point above
    the head would otherwise set the height, and a height is what everything downstream is
    divided by.
    """
    fig = read_xyz(figure_path)
    top = float(np.percentile(fig[:, 1], 99.98))
    sole = sole_level(fig[:, 1])
    if floor is None:
        floor = sole

    room = read_xyz(room_path, stride=2)
    ceiling, n_ceiling = densest_band(room[:, 1], top + 0.30, top + 3.0)
    if ceiling is None:
        raise SystemExit(f"no ceiling band above the figure in {room_path}")

    if verbose:
        print(f"  soles   y={sole:+.3f}  ({int((fig[:, 1] < sole).sum()):,} stray pts below them)")
        if floor != sole:
            print(f"  floor   y={floor:+.3f}  (given; the crop does not reach it)")
        print(f"  ceiling y={ceiling:+.3f}  ({n_ceiling:,} pts)")
        print(f"  figure {top - floor:.3f} u   room {ceiling - floor:.3f} u"
              f"   figure/room {(top - floor) / (ceiling - floor):.4f}")
    return top - floor, ceiling - floor, sole


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.strip().splitlines()[0])
    ap.add_argument("--room", required=True, help="the full room scan for this capture")
    ap.add_argument("--figure", required=True, help="the figure cropped out of that same scan")
    g = ap.add_mutually_exclusive_group(required=True)
    g.add_argument("--figure-height", type=float, metavar="M",
                   help="what this subject is assumed to be; prints the room height it implies")
    g.add_argument("--room-height", type=float, metavar="M",
                   help="the room height from an anchor capture; prints this figure's scale")
    ap.add_argument("--floor", type=float, metavar="Y",
                    help="the floor's height in this capture, for a crop that clipped the feet")
    ap.add_argument("--factor", action="store_true",
                    help="print the scale factor alone, for --scale")
    ap.add_argument("--sole", action="store_true",
                    help="print the soles' height alone, for the y floor of --crop")
    args = ap.parse_args(argv)

    quiet = args.factor or args.sole
    fig_u, room_u, sole = measure(args.room, args.figure, args.floor, verbose=not quiet)
    if args.sole:
        print(f"{sole:.4f}")
        return 0

    if args.figure_height:
        metres_per_unit = args.figure_height / fig_u
        if args.factor:
            print(f"{room_u * metres_per_unit:.6f}")
        else:
            print(f"\n  {args.figure_height:.3f} m figure -> {metres_per_unit:.4f} m/u"
                  f"   room height {room_u * metres_per_unit:.4f} m")
            print(f"\n  carry it with:  --room-height {room_u * metres_per_unit:.4f}")
    else:
        metres_per_unit = args.room_height / room_u
        if args.factor:
            print(f"{metres_per_unit:.6f}")
        else:
            print(f"\n  {args.room_height:.3f} m room -> {metres_per_unit:.4f} m/u"
                  f"   figure stands {fig_u * metres_per_unit:.4f} m")
            print(f"\n  clean it with:  --crop ... {sole:.4f} ... --no-floor"
                  f" --scale {metres_per_unit:.4f}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
