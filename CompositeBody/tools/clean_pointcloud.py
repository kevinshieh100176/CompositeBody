#!/usr/bin/env python
"""
Isolate a scanned figure from the room around it, and write a point cloud Unity can use.

Built for the 真人 captures: every held pose in S3 arrives as a scan of a room with a person
somewhere inside it, and every one needs the same six operations in the same order. Doing that
by hand in CloudCompare once is fine; doing it per pose is not, which is why this exists.

The order of operations is the part that matters, and it is not arbitrary:

  1. crop     -- - first, always. A tight box removes 90-95% of the points, and every step
                  below is O(n) or worse. Statistical outlier removal over a million points is
                  slow; over twenty-five thousand it is instant, for the same result.
  2. floor    -- by RANSAC plane fit, never by slicing at a height. A scan floor is never
                  level: the sample this was written against is tilted 0.20 degrees, which over
                  eight metres leaves a wedge of floor at one end while amputating the shoes at
                  the other. Fitting costs nothing and removes the whole class of problem.
  3. feet     -- plane removal alone either keeps a disc of floor under the subject or takes
                  their shoes off. So the footprint is measured from the clean mid-body bands,
                  dilated one cell, and low points are kept only inside it.
  4. cluster  -- keep the largest connected component, which drops background fragments that
                  survived the crop.
  5. outliers -- statistical, then optionally radius. Depth scans always carry a fuzzy halo
                  along silhouette edges; this is what takes it off.
  6. transform - the Unity frame (handedness flip included) happens last, on the way out, so
                  the pipeline above only ever works in the scanner's own coordinates.

Open3D does the heavy lifting where it is installed, because its RANSAC, DBSCAN and outlier
filters are better tested than anything worth hand-rolling here. Everything has a NumPy fallback
so the script still runs in an environment without it -- an asset-prep step that cannot run
because of a missing wheel is not much use the week of a shoot.

Usage
-----
    # a figure somewhere in a room scan, isolated by a cylinder around them
    py clean_pointcloud.py scan.ply -o person.ply --near -0.43 -1.87 --radius 1.2

    # the same, plus the Unity-ready copy (feet at y=0, centred, Z negated)
    py clean_pointcloud.py scan.ply -o person.ply --near -0.43 -1.87 --unity person_unity.ply

    # an explicit box instead, and a mesh input sampled for density rather than using vertices
    py clean_pointcloud.py scene-mesh.ply -o person.ply \
        --crop -1.2 0.4 -1.5 0.6 -2.6 -1.1 --sample-surface 2000000

Run with --dry-run to see the per-stage point counts without writing anything.
"""

from __future__ import annotations

import argparse
import math
import struct
import sys
from collections import defaultdict, deque

try:
    import numpy as np
except ImportError:                                      # pragma: no cover
    np = None

try:
    import open3d as o3d
except ImportError:                                      # pragma: no cover
    o3d = None


# --------------------------------------------------------------------------------------
# PLY reading
#
# Written by hand rather than delegated to Open3D because the two scanners in use here emit
# different files -- one ASCII with float xyz, one binary-little-endian with *double* xyz plus
# a face block -- and the failure mode of handing the wrong one to a reader that silently
# returns an empty cloud is a confusing afternoon. Parsing the header ourselves also tells us
# whether the file is a mesh before we decide how to load it.
# --------------------------------------------------------------------------------------

_PLY_SCALAR = {
    "char": ("b", 1), "uchar": ("B", 1), "int8": ("b", 1), "uint8": ("B", 1),
    "short": ("h", 2), "ushort": ("H", 2), "int16": ("h", 2), "uint16": ("H", 2),
    "int": ("i", 4), "uint": ("I", 4), "int32": ("i", 4), "uint32": ("I", 4),
    "float": ("f", 4), "float32": ("f", 4), "double": ("d", 8), "float64": ("d", 8),
}


class PlyHeader:
    def __init__(self):
        self.fmt = "ascii"
        self.length = 0
        self.elements = []           # [(name, count, [(prop, type, is_list)])]

    @property
    def vertex_count(self):
        for name, count, _ in self.elements:
            if name == "vertex":
                return count
        return 0

    @property
    def face_count(self):
        for name, count, _ in self.elements:
            if name == "face":
                return count
        return 0

    def vertex_props(self):
        for name, _, props in self.elements:
            if name == "vertex":
                return props
        return []


def read_ply_header(path):
    h = PlyHeader()
    with open(path, "rb") as f:
        if f.readline().strip() != b"ply":
            raise ValueError(f"{path} is not a PLY file")
        current = None
        while True:
            raw = f.readline()
            if not raw:
                raise ValueError("PLY header never ended")
            line = raw.decode("ascii", "replace").strip()
            parts = line.split()
            if not parts:
                continue
            if parts[0] == "format":
                h.fmt = parts[1]
            elif parts[0] == "element":
                current = (parts[1], int(parts[2]), [])
                h.elements.append(current)
            elif parts[0] == "property" and current is not None:
                if parts[1] == "list":
                    current[2].append((parts[4], (parts[2], parts[3]), True))
                else:
                    current[2].append((parts[2], parts[1], False))
            elif parts[0] == "end_header":
                h.length = f.tell()
                return h


def read_ply_points(path, verbose=True):
    """Return (xyz, rgb) as float64 / uint8 arrays. Faces are skipped: this is a point tool."""
    h = read_ply_header(path)
    n = h.vertex_count
    props = h.vertex_props()
    names = [p[0] for p in props]
    if verbose:
        kind = "mesh" if h.face_count else "point cloud"
        print(f"  {path}: {kind}, {n:,} vertices"
              + (f", {h.face_count:,} faces" if h.face_count else "")
              + f", {h.fmt}")

    want = ("x", "y", "z")
    if not all(w in names for w in want):
        raise ValueError(f"vertex element has no x/y/z (got {names})")
    has_rgb = all(c in names for c in ("red", "green", "blue"))

    if h.fmt == "ascii":
        xyz = [None] * n
        rgb = [None] * n
        ix, iy, iz = (names.index(c) for c in want)
        ir, ig, ib = ((names.index(c) for c in ("red", "green", "blue")) if has_rgb else (0, 0, 0))
        with open(path, "r") as f:
            f.seek(0)
            for line in f:
                if line.strip() == "end_header":
                    break
            k = 0
            for line in f:
                if k >= n:
                    break
                p = line.split()
                if len(p) < len(names):
                    continue
                xyz[k] = (float(p[ix]), float(p[iy]), float(p[iz]))
                rgb[k] = (int(p[ir]), int(p[ig]), int(p[ib])) if has_rgb else (200, 200, 200)
                k += 1
        xyz, rgb = xyz[:k], rgb[:k]
    else:
        little = "little" in h.fmt
        endian = "<" if little else ">"
        fmt = endian
        offs = {}
        size = 0
        for pname, ptype, is_list in props:
            if is_list:
                raise ValueError("list property inside the vertex element is not supported")
            code, width = _PLY_SCALAR[ptype]
            offs[pname] = len(fmt) - 1
            fmt += code
            size += width
        with open(path, "rb") as f:
            f.seek(h.length)
            blob = f.read(n * size)
        xyz = [None] * n
        rgb = [None] * n
        gx, gy, gz = offs["x"], offs["y"], offs["z"]
        gr, gg, gb = ((offs["red"], offs["green"], offs["blue"]) if has_rgb else (0, 0, 0))
        for k, rec in enumerate(struct.iter_unpack(fmt, blob)):
            xyz[k] = (rec[gx], rec[gy], rec[gz])
            rgb[k] = (rec[gr], rec[gg], rec[gb]) if has_rgb else (200, 200, 200)

    if np is not None:
        return np.asarray(xyz, dtype=np.float64), np.asarray(rgb, dtype=np.uint8)
    return xyz, rgb


def sample_mesh_surface(path, count):
    """
    Points spread evenly over a mesh's faces, rather than its vertices.

    A reconstructed mesh has its vertices wherever the reconstruction put them -- dense across
    detailed patches, sparse over flat ones -- so using them as a point cloud inherits that
    unevenness. Sampling the surface decouples the point count from the topology: ask for four
    million and you get four million, evenly spaced, from a mesh with two hundred thousand
    vertices. For the figures this matters most on the face, which is the part the reconstruction
    smooths and the part the audience is a metre away from.
    """
    if o3d is None:
        raise SystemExit("--sample-surface needs Open3D (py -m pip install open3d)")
    mesh = o3d.io.read_triangle_mesh(path)
    if len(mesh.triangles) == 0:
        raise SystemExit(f"{path} has no faces to sample; drop --sample-surface")
    if not mesh.has_vertex_colors():
        print("  warning: mesh has no vertex colours, sampled points will be grey")
    cloud = mesh.sample_points_uniformly(number_of_points=int(count), use_triangle_normal=False)
    xyz = np.asarray(cloud.points, dtype=np.float64)
    if cloud.has_colors():
        rgb = (np.asarray(cloud.colors) * 255.0).round().clip(0, 255).astype(np.uint8)
    else:
        rgb = np.full((len(xyz), 3), 200, dtype=np.uint8)
    print(f"  sampled {len(xyz):,} points from {len(mesh.triangles):,} faces")
    return xyz, rgb


def scale_about_origin(xyz, factor):
    if factor == 1.0:
        return xyz
    if np is not None:
        return np.asarray(xyz) * factor
    return [(p[0] * factor, p[1] * factor, p[2] * factor) for p in xyz]


def write_ply_points(path, xyz, rgb, ascii_out=True, comment=""):
    n = len(xyz)
    header = ("ply\n"
              f"format {'ascii 1.0' if ascii_out else 'binary_little_endian 1.0'}\n"
              + (f"comment {comment}\n" if comment else "")
              + f"element vertex {n}\n"
              "property float x\nproperty float y\nproperty float z\n"
              "property uchar red\nproperty uchar green\nproperty uchar blue\n"
              "end_header\n")
    if ascii_out:
        with open(path, "w") as f:
            f.write(header)
            for (x, y, z), (r, g, b) in zip(xyz, rgb):
                f.write(f"{x:.5f} {y:.5f} {z:.5f} {int(r)} {int(g)} {int(b)}\n")
    else:
        with open(path, "wb") as f:
            f.write(header.encode("ascii"))
            pack = struct.Struct("<fffBBB").pack
            for (x, y, z), (r, g, b) in zip(xyz, rgb):
                f.write(pack(float(x), float(y), float(z), int(r), int(g), int(b)))
    print(f"  wrote {path}  ({n:,} points, {'ascii' if ascii_out else 'binary'})")


# --------------------------------------------------------------------------------------
# Stages
# --------------------------------------------------------------------------------------

def stage_crop(xyz, rgb, args):
    """Box or cylinder. Cheapest possible win, so it runs before anything else."""
    keep = None
    if args.crop:
        x0, x1, y0, y1, z0, z1 = args.crop
        if np is not None:
            keep = ((xyz[:, 0] >= x0) & (xyz[:, 0] <= x1) &
                    (xyz[:, 1] >= y0) & (xyz[:, 1] <= y1) &
                    (xyz[:, 2] >= z0) & (xyz[:, 2] <= z1))
        else:
            keep = [x0 <= p[0] <= x1 and y0 <= p[1] <= y1 and z0 <= p[2] <= z1 for p in xyz]
    elif args.near:
        cx, cz = args.near
        r2 = args.radius ** 2
        if np is not None:
            keep = ((xyz[:, 0] - cx) ** 2 + (xyz[:, 2] - cz) ** 2) <= r2
        else:
            keep = [(p[0] - cx) ** 2 + (p[2] - cz) ** 2 <= r2 for p in xyz]
    if keep is None:
        return xyz, rgb
    return _select(xyz, rgb, keep)


def _select(xyz, rgb, mask):
    if np is not None:
        m = np.asarray(mask, dtype=bool)
        return xyz[m], rgb[m]
    return ([p for p, k in zip(xyz, mask) if k],
            [c for c, k in zip(rgb, mask) if k])


def _lsq_plane(pts):
    """Least-squares y = Ax + Bz + C, returned as the plane (-A, 1, -B, -C)."""
    n = len(pts)
    if n < 8:
        return None
    if np is not None:
        P = np.asarray(pts)
        G = np.column_stack([P[:, 0], P[:, 2], np.ones(n)])
        try:
            (A, B, C), *_ = np.linalg.lstsq(G, P[:, 1], rcond=None)
        except np.linalg.LinAlgError:
            return None
        return (-float(A), 1.0, -float(B), -float(C))

    Sx = Sz = Sy = Sxx = Szz = Sxz = Sxy = Szy = 0.0
    for x, y, z in pts:
        Sx += x; Sz += z; Sy += y
        Sxx += x * x; Szz += z * z; Sxz += x * z; Sxy += x * y; Szy += z * y
    M = [[Sxx, Sxz, Sx, Sxy], [Sxz, Szz, Sz, Szy], [Sx, Sz, float(n), Sy]]
    for i in range(3):
        p = max(range(i, 3), key=lambda r: abs(M[r][i]))
        M[i], M[p] = M[p], M[i]
        if abs(M[i][i]) < 1e-12:
            return None
        for r in range(3):
            if r == i:
                continue
            f = M[r][i] / M[i][i]
            for cc in range(i, 4):
                M[r][cc] -= f * M[i][cc]
    A, B, C = (M[i][3] / M[i][i] for i in range(3))
    return (-A, 1.0, -B, -C)


def fit_floor_plane(xyz, args):
    """
    The floor, as a plane oriented so 'above it' is positive.

    Two deliberate choices here.

    Fitted over the bottom slice of the crop only, not the whole box. The floor is at the
    bottom by definition, whereas a crop tight enough to be useful is usually dominated by
    vertical structure -- the subject, a wall, a pillar -- and a fit over everything returns
    one of those instead.

    And fitted by trimmed least squares rather than RANSAC, because this has to be
    reproducible. Open3D's segment_plane draws its own samples and is not stabilised by
    o3d.utility.random.seed, so the same scan cleaned twice produced planes 7 mm apart and
    point counts that differed by fifty -- which is a poor property for a step that bakes
    assets. Trimming re-fits after discarding gross residuals, which on a floor slab reaches
    the same answer every time.

    Near-horizontal is still checked rather than assumed, because the bottom slice of a crop
    around a tall object can be mostly that object's own sides.
    """
    ymin = min(p[1] for p in xyz)
    pts = [p for p in xyz if p[1] <= ymin + args.floor_search]
    if len(pts) < 32:
        pts = list(xyz)

    plane = None
    for _ in range(4):
        plane = _lsq_plane(pts)
        if plane is None:
            return None
        a, b, c, d = plane
        norm = math.sqrt(a * a + b * b + c * c) or 1.0
        res = [abs((p[0] * a + p[1] * b + p[2] * c + d) / norm) for p in pts]
        srt = sorted(res)
        med = srt[len(srt) // 2]
        cut = max(args.floor_tolerance, 2.5 * med)
        keep = [p for p, r in zip(pts, res) if r <= cut]
        if len(keep) < 32 or len(keep) == len(pts):
            break
        pts = keep

    a, b, c, d = plane
    if abs(b) / (math.sqrt(a * a + b * b + c * c) or 1.0) < 0.80:
        return None                                  # not a floor; leave it alone
    if b < 0:
        a, b, c, d = -a, -b, -c, -d                  # orient the normal upward
    return (a, b, c, d)


def height_above(xyz, plane):
    a, b, c, d = plane
    norm = math.sqrt(a * a + b * b + c * c) or 1.0
    if np is not None:
        return (xyz[:, 0] * a + xyz[:, 1] * b + xyz[:, 2] * c + d) / norm
    return [(p[0] * a + p[1] * b + p[2] * c + d) / norm for p in xyz]


def stage_floor(xyz, rgb, plane, args):
    """Drop the floor, but keep low points that sit inside the subject's own footprint."""
    h = height_above(xyz, plane)
    cell = args.footprint_cell

    if args.keep_feet:
        # Footprint from the clean mid-body bands only -- measuring it at ankle height would
        # just re-measure the floor we are trying to remove.
        # Measured from the leg band, not the whole figure. Projecting shoulders and
        # outstretched arms straight down produces a footprint the width of the body, and the
        # floor inside it then survives as a disc the subject appears to be standing on.
        mask = set()
        lo, hi = args.footprint_band
        for i in range(len(xyz)):
            if lo < h[i] < hi:
                mask.add((int(xyz[i][0] // cell), int(xyz[i][2] // cell)))
        dil = set(mask)
        for cx, cz in mask:
            for dx in (-1, 0, 1):
                for dz in (-1, 0, 1):
                    dil.add((cx + dx, cz + dz))
        # The guard, not the clearance, is what the footprint is tested against. A scanned
        # floor is a slab with scatter, not a surface: in the sample this was written against,
        # floor points sit up to 7 cm above the fitted plane, so testing at the clearance
        # (3 cm) waves most of the floor straight through and the subject arrives with a metre
        # of ground stuck to their feet.
        #
        # Points well below the plane are dropped whatever the footprint says. Shoe soles rest
        # on the floor, so they sit at h of roughly zero; anything further under it is scanner
        # noise or the underside of the slab, and keeping it inside the footprint was dragging
        # a 13 cm skirt of debris along with the feet.
        below = -abs(args.floor_tolerance)
        keep = [h[i] >= args.footprint_guard or
                (h[i] >= below and
                 (int(xyz[i][0] // cell), int(xyz[i][2] // cell)) in dil)
                for i in range(len(xyz))]
        note = f"footprint {len(mask)} cells (dilated {len(dil)}), guard {args.footprint_guard} m"
    else:
        keep = [hi_ >= args.floor_clearance for hi_ in h]
        note = "no footprint mask"
    return _select(xyz, rgb, keep), note


def stage_cluster(xyz, rgb, args):
    """
    Keep the largest connected component.

    Largest rather than tallest, which is the other obvious rule: a leftover slice of wall is
    both bigger and taller than a person, so neither rule survives a bad crop. The crop is
    what makes this correct, so the runner-up clusters get reported -- if the one that was
    kept is not obviously the subject, the box is wrong, not the threshold.
    """
    if len(xyz) == 0:
        return xyz, rgb, 0, []

    if o3d is not None and np is not None:
        cloud = o3d.geometry.PointCloud(o3d.utility.Vector3dVector(xyz))
        labels = np.asarray(cloud.cluster_dbscan(eps=args.cluster_eps,
                                                 min_points=args.cluster_min, print_progress=False))
        if labels.size == 0 or labels.max() < 0:
            return xyz, rgb, 0, []
        counts = np.bincount(labels[labels >= 0])
        order = np.argsort(counts)[::-1][:3]
        top = [(int(counts[k]),
                float(xyz[labels == k][:, 1].max() - xyz[labels == k][:, 1].min()))
               for k in order]
        best = int(counts.argmax())
        return (*_select(xyz, rgb, labels == best), int(counts.size), top)

    # Fallback: connected components over an occupied voxel grid, which for a scan this dense
    # separates the same things DBSCAN would and costs one pass.
    v = args.cluster_eps
    vox = defaultdict(list)
    for i in range(len(xyz)):
        vox[(int(xyz[i][0] // v), int(xyz[i][1] // v), int(xyz[i][2] // v))].append(i)
    NB = [(a, b, c) for a in (-1, 0, 1) for b in (-1, 0, 1) for c in (-1, 0, 1)
          if (a, b, c) != (0, 0, 0)]
    seen, best, count, sizes = set(), [], 0, []
    for v0 in vox:
        if v0 in seen:
            continue
        count += 1
        q = deque([v0]); seen.add(v0); cc = []
        while q:
            cur = q.popleft(); cc.append(cur)
            for d in NB:
                nb = (cur[0] + d[0], cur[1] + d[1], cur[2] + d[2])
                if nb in vox and nb not in seen:
                    seen.add(nb); q.append(nb)
        idx = [i for cvox in cc for i in vox[cvox]]
        ys = [xyz[i][1] for i in idx]
        sizes.append((len(idx), max(ys) - min(ys)))
        if len(idx) > len(best):
            best = idx
    keep = [False] * len(xyz)
    for i in best:
        keep[i] = True
    sizes.sort(reverse=True)
    return (*_select(xyz, rgb, keep), count, sizes[:3])


def stage_outliers(xyz, rgb, args):
    """Statistical first, then radius if asked. Takes the halo off silhouette edges."""
    if len(xyz) == 0 or args.sor_neighbors <= 0:
        return xyz, rgb

    if o3d is not None and np is not None:
        cloud = o3d.geometry.PointCloud(o3d.utility.Vector3dVector(xyz))
        _, keep = cloud.remove_statistical_outlier(nb_neighbors=args.sor_neighbors,
                                                   std_ratio=args.sor_std)
        mask = np.zeros(len(xyz), dtype=bool); mask[keep] = True
        xyz, rgb = _select(xyz, rgb, mask)
        if args.radius_outlier:
            cloud = o3d.geometry.PointCloud(o3d.utility.Vector3dVector(xyz))
            _, keep = cloud.remove_radius_outlier(nb_points=args.radius_min_points,
                                                  radius=args.radius_outlier)
            mask = np.zeros(len(xyz), dtype=bool); mask[keep] = True
            xyz, rgb = _select(xyz, rgb, mask)
        return xyz, rgb

    # Fallback: drop points whose neighbourhood is far emptier than the cloud's own norm.
    #
    # The threshold is a fraction of the median neighbourhood count rather than a fixed
    # number, because the right absolute count depends entirely on the scan's density -- at
    # 8 mm spacing a 50 mm cell holds hundreds of points, so any constant small enough for a
    # sparse cloud waves the whole halo through. The cell is deliberately finer than the
    # cluster voxel so edge points actually end up in thin cells.
    v = max(args.cluster_eps / 2.0, 0.01)
    vox = defaultdict(int)
    key = []
    for i in range(len(xyz)):
        k = (int(xyz[i][0] // v), int(xyz[i][1] // v), int(xyz[i][2] // v))
        key.append(k); vox[k] += 1
    NB = [(a, b, c) for a in (-1, 0, 1) for b in (-1, 0, 1) for c in (-1, 0, 1)]
    counts = [sum(vox.get((k[0] + d[0], k[1] + d[1], k[2] + d[2]), 0) for d in NB) for k in key]
    srt = sorted(counts)
    med = srt[len(srt) // 2] if srt else 0
    floor_count = max(2, int(med / max(1.0, args.sor_std) / 4))
    return _select(xyz, rgb, [c >= floor_count for c in counts])


def stage_downsample(xyz, rgb, args):
    """Uniform density. Last, so earlier stages see every point they were tuned against."""
    if not args.voxel or len(xyz) == 0:
        return xyz, rgb
    v = args.voxel
    first = {}
    order = []
    for i in range(len(xyz)):
        k = (int(xyz[i][0] // v), int(xyz[i][1] // v), int(xyz[i][2] // v))
        if k not in first:
            first[k] = i
            order.append(i)
    keep = [False] * len(xyz)
    for i in order:
        keep[i] = True
    return _select(xyz, rgb, keep)


def to_unity(xyz, plane):
    """
    Feet on y=0, centred in XZ, Z negated.

    Negating Z is the right-handed to left-handed conversion, which mirrors the subject. That
    is correct and unavoidable -- but it does swap their left and right, which matters if the
    cloud is ever lined up against video of the same take.
    """
    xs = [p[0] for p in xyz]; zs = [p[2] for p in xyz]
    cx = (min(xs) + max(xs)) / 2.0
    cz = (min(zs) + max(zs)) / 2.0
    if plane is not None:
        h = height_above(xyz, plane)
        out = [(xyz[i][0] - cx, h[i], -(xyz[i][2] - cz)) for i in range(len(xyz))]
    else:
        y0 = min(p[1] for p in xyz)
        out = [(p[0] - cx, p[1] - y0, -(p[2] - cz)) for p in xyz]
    if np is not None:
        return np.asarray(out, dtype=np.float64)
    return out


# --------------------------------------------------------------------------------------

def survey(xyz, args, input_path):
    """
    List places a standing figure could be, so --near can be filled in without a viewer.

    Looks for the one thing a standing person reliably is and the room reliably is not: a
    narrow column of points that starts at the floor and stops between 1.3 and 2.1 m, mostly
    solid the whole way up. Walls fail it on width, furniture on height, and the floor itself
    on both.

    The floor is taken from the densest height bin rather than from the lowest point, because
    a big scan's lowest point is usually a stray below the slab or a lower level entirely, and
    measuring every column from the wrong datum finds nothing at all.
    """
    bins = defaultdict(int)
    for p in xyz:
        bins[round(p[1] / 0.1) * 0.1] += 1
    floor_y = max(bins.items(), key=lambda kv: kv[1])[0]
    near = [p for p in xyz if abs(p[1] - floor_y) < 0.12]
    plane = _lsq_plane(near) if len(near) >= 32 else None
    if plane is None:
        plane = (0.0, 1.0, 0.0, -floor_y)
    a, b, c, d = plane
    norm = math.sqrt(a * a + b * b + c * c) or 1.0
    print(f"  floor at y={floor_y:+.2f} ({bins[floor_y]:,} pts in that bin), "
          f"plane {a:+.4f}x {b:+.4f}y {c:+.4f}z {d:+.4f}")

    C = args.survey_cell
    cells = defaultdict(set)
    for x, y, z in xyz:
        h = (x * a + y * b + z * c + d) / norm
        cells[(int(x // C), int(z // C))].add(int(h // 0.1))
    print(f"  {len(cells):,} occupied cells at {C} m")

    cand = {}
    for k, bandset in cells.items():
        lo, hi = min(bandset), max(bandset)
        if lo > 3:                                   # must start within 0.3 m of the floor
            continue
        if not (13 <= hi <= 21):                     # top between 1.3 and 2.1 m
            continue
        if len(bandset) / (hi - lo + 1) < 0.70:      # mostly solid, not a sparse edge
            continue
        cand[k] = hi * 0.1
    print(f"  {len(cand):,} cells hold a floor-to-1.3/2.1 m solid column")

    seen, groups = set(), []
    NB = [(i, j) for i in (-1, 0, 1) for j in (-1, 0, 1) if (i, j) != (0, 0)]
    for c0 in cand:
        if c0 in seen:
            continue
        q = deque([c0]); seen.add(c0); g = []
        while q:
            cur = q.popleft(); g.append(cur)
            for dd in NB:
                nb = (cur[0] + dd[0], cur[1] + dd[1])
                if nb in cand and nb not in seen:
                    seen.add(nb); q.append(nb)
        groups.append(g)

    scored = []
    for g in groups:
        xs = [k[0] * C for k in g]; zs = [k[1] * C for k in g]
        w, dz = max(xs) - min(xs) + C, max(zs) - min(zs) + C
        cx, cz = sum(xs) / len(g) + C / 2, sum(zs) / len(g) + C / 2
        top = max(cand[k] for k in g)
        plausible = (0.25 <= max(w, dz) <= 1.10 and max(w, dz) / min(w, dz) < 2.2
                     and 1.35 <= top <= 2.05)
        scored.append((plausible, len(g), cx, cz, w, dz, top))
    scored.sort(key=lambda s: (not s[0], -s[1]))

    if not scored:
        print("\n  nothing column-shaped found. The subject may be seated or against a wall --\n"
              "  open the file in CloudCompare, pick a point on them, and pass its X and Z.")
        return
    print(f"\n  {'cells':>5} {'W x D':>11} {'top':>6}   centre(x,z)       plausible")
    for pl, n, cx, cz, w, dz, top in scored[:12]:
        print(f"  {n:>5} {w:5.2f} x{dz:5.2f} {top:5.2f}m   ({cx:+6.2f},{cz:+6.2f})   "
              f"{'yes' if pl else 'no'}")
    best = [s for s in scored if s[0]]
    if best:
        _, _, cx, cz, _, _, _ = best[0]
        print(f"\n  most likely subject at ({cx:+.2f}, {cz:+.2f}). To extract it:\n")
        print(f"    py tools/clean_pointcloud.py \"{input_path}\" --near {cx:.2f} {cz:.2f} "
              f"--radius 1.2 \\\n        -o person.ply --unity person_unity.ply")
    else:
        print("\n  no plausible figure among them; the entries above are probably structure.")


def bounds_line(xyz):
    if len(xyz) == 0:
        return "empty"
    xs = [p[0] for p in xyz]; ys = [p[1] for p in xyz]; zs = [p[2] for p in xyz]
    return (f"{max(xs)-min(xs):.3f} x {max(ys)-min(ys):.3f} x {max(zs)-min(zs):.3f} m"
            f"   (y {min(ys):+.2f}..{max(ys):+.2f})")


def main(argv=None):
    ap = argparse.ArgumentParser(
        description="Isolate a scanned figure from a room scan and write a Unity-ready point cloud.",
        formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("input")
    ap.add_argument("-o", "--output", help="cleaned PLY, in the scanner's own frame")
    ap.add_argument("--unity", metavar="PATH",
                    help="also write this, with feet at y=0, XZ centred and Z negated for Unity")

    ap.add_argument("--survey", action="store_true",
                    help="don't clean anything -- report where a standing figure might be, "
                         "with the --near command to extract it. Run this first.")

    g = ap.add_argument_group("crop (pick one; strongly recommended)")
    g.add_argument("--crop", nargs=6, type=float, metavar=("XMIN", "XMAX", "YMIN", "YMAX", "ZMIN", "ZMAX"))
    g.add_argument("--near", nargs=2, type=float, metavar=("X", "Z"),
                   help="centre of a vertical cylinder around the subject")
    g.add_argument("--radius", type=float, default=1.2, help="cylinder radius, default 1.2 m")

    g = ap.add_argument_group("floor")
    g.add_argument("--no-floor", dest="remove_floor", action="store_false",
                   help="skip floor removal entirely")
    g.add_argument("--floor-tolerance", type=float, default=0.02,
                   help="RANSAC inlier distance, default 0.02 m")
    g.add_argument("--floor-search", type=float, default=0.30,
                   help="fit the plane over this much of the crop's bottom, default 0.30 m")
    g.add_argument("--floor-clearance", type=float, default=0.03,
                   help="drop points within this of the plane, default 0.03 m")
    g.add_argument("--no-keep-feet", dest="keep_feet", action="store_false",
                   help="do not mask low points against the body footprint (shoes will go)")
    g.add_argument("--footprint-band", nargs=2, type=float, default=(0.15, 0.60), metavar=("LO", "HI"),
                   help="height band the footprint is measured from -- the legs, not the whole "
                        "body, because that is what the feet are underneath. Default 0.15 0.60")
    g.add_argument("--footprint-cell", type=float, default=0.03,
                   help="footprint cell. Smaller hugs the legs more tightly and leaves less "
                        "floor underfoot, but needs a dense scan to stay connected; 0.02 suits "
                        "a sub-centimetre cloud. Default 0.03 m")
    g.add_argument("--footprint-guard", type=float, default=0.15,
                   help="below this height a point must be inside the footprint to survive; "
                        "raise it if floor still comes through, default 0.15 m")

    g = ap.add_argument_group("cluster and outliers")
    g.add_argument("--cluster-eps", type=float, default=0.05, help="DBSCAN eps, default 0.05 m")
    g.add_argument("--cluster-min", type=int, default=20, help="DBSCAN min points, default 20")
    g.add_argument("--sor-neighbors", type=int, default=20, help="0 disables, default 20")
    g.add_argument("--sor-std", type=float, default=2.0, help="std ratio, default 2.0")
    g.add_argument("--radius-outlier", type=float, default=0.0, metavar="R",
                   help="also run radius outlier removal at this radius")
    g.add_argument("--radius-min-points", type=int, default=16)

    g = ap.add_argument_group("output")
    g.add_argument("--sample-surface", type=int, default=0, metavar="N",
                   help="turn a mesh into N points spread evenly over its faces, instead of "
                        "using its vertices. Needs Open3D.")
    g.add_argument("--scale", type=float, default=1.0,
                   help="multiply all coordinates by this on the way out")
    g.add_argument("--target-height", type=float, default=0.0, metavar="M",
                   help="scale so the cleaned figure stands exactly this tall; overrides --scale. "
                        "Use the performer's real height.")
    g.add_argument("--voxel", type=float, default=0.0, help="voxel downsample size, 0 disables")
    g.add_argument("--binary", action="store_true", help="write binary PLY instead of ascii")
    g.add_argument("--dry-run", action="store_true", help="report the stages, write nothing")
    g.add_argument("--seed", type=int, default=7,
                   help="unused; kept so older command lines still run")
    g.add_argument("--survey-cell", type=float, default=0.25,
                   help="survey grid size, default 0.25 m")
    args = ap.parse_args(argv)

    if not args.survey and not args.dry_run and not args.output and not args.unity:
        ap.error("give -o/--output, or --unity, or --dry-run, or --survey")
    if args.crop and args.near:
        ap.error("--crop and --near are alternatives; pick one")

    print(f"open3d: {'yes, ' + o3d.__version__ if o3d else 'no (using fallbacks)'}"
          f"   numpy: {'yes, ' + np.__version__ if np is not None else 'no'}")
    print("\nload")
    if args.sample_surface:
        read_ply_header(args.input)                  # fails loudly if it is not a PLY at all
        print(f"  {args.input}: sampling the surface")
        xyz, rgb = sample_mesh_surface(args.input, args.sample_surface)
    else:
        xyz, rgb = read_ply_points(args.input)
    n0 = len(xyz)
    print(f"  {n0:,} points   {bounds_line(xyz)}")

    if args.survey:
        print("\nsurvey")
        survey(xyz, args, args.input)
        return 0

    print("\ncrop")
    xyz, rgb = stage_crop(xyz, rgb, args)
    print(f"  {len(xyz):,} points ({100*len(xyz)/max(1,n0):.1f}% kept)   {bounds_line(xyz)}")
    if len(xyz) == 0:
        print("\nnothing left after the crop -- check the box or the cylinder centre.")
        return 1

    plane = None
    if args.remove_floor:
        print("\nfloor")
        plane = fit_floor_plane(xyz, args)
        if plane is None:
            print("  no near-horizontal plane found; leaving the floor in place")
        else:
            a, b, c, d = plane
            tilt = math.degrees(math.acos(min(1.0, abs(b) / math.sqrt(a*a + b*b + c*c))))
            print(f"  plane {a:+.4f}x {b:+.4f}y {c:+.4f}z {d:+.4f}   tilt {tilt:.2f} deg")
            before = len(xyz)
            (xyz, rgb), note = stage_floor(xyz, rgb, plane, args)
            print(f"  {len(xyz):,} points (-{before-len(xyz):,})   {note}")

    print("\ncluster")
    before = len(xyz)
    xyz, rgb, ncl, top = stage_cluster(xyz, rgb, args)
    if top:
        print("  biggest: " + ",  ".join(f"{n:,} pts / {hh:.2f} m tall" for n, hh in top))
    print(f"  {ncl} clusters, kept the largest: {len(xyz):,} points (-{before-len(xyz):,})")

    print("\noutliers")
    before = len(xyz)
    xyz, rgb = stage_outliers(xyz, rgb, args)
    print(f"  {len(xyz):,} points (-{before-len(xyz):,})")

    if args.voxel:
        print("\ndownsample")
        before = len(xyz)
        xyz, rgb = stage_downsample(xyz, rgb, args)
        print(f"  {len(xyz):,} points (-{before-len(xyz):,}) at {args.voxel} m")

    figure_height = None
    if plane is not None and len(xyz):
        figure_height = max(height_above(xyz, plane))

    factor = args.scale
    if args.target_height:
        if not figure_height:
            print("\ncannot use --target-height without a floor plane; use --scale instead.")
            return 1
        factor = args.target_height / figure_height
        print(f"\nscale: {figure_height:.3f} m measured -> {args.target_height:.3f} m asked"
              f"   factor {factor:.4f}")
    elif factor != 1.0:
        print(f"\nscale: factor {factor:.4f}")

    print(f"\nresult: {len(xyz):,} points   {bounds_line(xyz)}")
    if figure_height is not None:
        print(f"  height above the floor plane: {figure_height:.3f} m"
              + (f"  ->  {figure_height*factor:.3f} m after scaling" if factor != 1.0 else ""))
    if len(xyz):
        mr = sum(int(c[0]) for c in rgb) // len(rgb)
        mg = sum(int(c[1]) for c in rgb) // len(rgb)
        mb = sum(int(c[2]) for c in rgb) // len(rgb)
        print(f"  mean RGB ({mr}, {mg}, {mb})")

    if args.dry_run:
        print("\ndry run, nothing written.")
        return 0

    print()
    scaled = f"; scaled x{factor:.4f}" if factor != 1.0 else ""
    if args.output:
        write_ply_points(args.output, scale_about_origin(xyz, factor), rgb,
                         ascii_out=not args.binary,
                         comment=f"cleaned from {args.input}{scaled}")
    if args.unity:
        # Scale after the frame change, where the origin is already the figure's feet, so the
        # factor cannot drag them off the floor.
        write_ply_points(args.unity, scale_about_origin(to_unity(xyz, plane), factor), rgb,
                         ascii_out=not args.binary,
                         comment=f"cleaned from {args.input}; feet at y=0, XZ centred, "
                                 f"Z negated for Unity{scaled}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
