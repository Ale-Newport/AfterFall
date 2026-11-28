"""Converts CMU motion capture (ASF skeleton + AMC motion) into AFTERFALL gait clips.

    python3 tools/mocap/convert_cmu.py <subject.asf> <trial.amc> <name> <out.json> [--fps 120]

The clip is one full gait cycle (left heel strike to the next), resampled to 48 frames and
expressed in the parameters the procedural rig already uses (HumanoidRig.Pose): pelvis offset and
rotation, chest and head rotation relative to their parents, upper-arm angles and elbow bend,
ankle targets for the leg IK and foot pitch. Positions are scaled so the capture's legs are as
long as the rig's (thigh + shin = 0.83 m) and given in a frame that moves with the body at its
average speed (x right, y up, z forward, like Unity), so planted feet slide back at exactly that
speed. The game plays a clip at the speed the character moves: one cycle per `stride` metres.

Source data: the CMU Graphics Lab Motion Capture Database, http://mocap.cs.cmu.edu (see
docs/ASSET_LICENSES.md). Only the converted cycles are kept in the repository.
"""

import json
import math
import sys

import numpy as np

RIG_LEG = 0.43 + 0.40
FRAMES = 48


def rot(axis, deg):
    a = math.radians(deg)
    c, s = math.cos(a), math.sin(a)
    if axis == 0:
        return np.array([[1, 0, 0], [0, c, -s], [0, s, c]])
    if axis == 1:
        return np.array([[c, 0, s], [0, 1, 0], [-s, 0, c]])
    return np.array([[c, -s, 0], [s, c, 0], [0, 0, 1]])


def euler_xyz(rx, ry, rz):
    """Static XYZ: x first, then y, then z (R = Rz Ry Rx), as the ASF/AMC format uses."""
    return rot(2, rz) @ rot(1, ry) @ rot(0, rx)


def parse_asf(path):
    bones = {"root": {"name": "root", "dir": np.zeros(3), "len": 0.0, "axis": np.eye(3), "dof": ["rx", "ry", "rz"]}}
    hierarchy = {}
    lines = [l.strip() for l in open(path)]
    i = 0
    section = None
    cur = None
    while i < len(lines):
        l = lines[i]
        if l.startswith(":"):
            section = l.split()[0]
            i += 1
            continue
        if section == ":bonedata":
            if l == "begin":
                cur = {"dof": []}
            elif l == "end":
                bones[cur["name"]] = cur
                cur = None
            elif cur is not None:
                w = l.split()
                if w[0] == "name":
                    cur["name"] = w[1]
                elif w[0] == "direction":
                    cur["dir"] = np.array([float(x) for x in w[1:4]])
                elif w[0] == "length":
                    cur["len"] = float(w[1])
                elif w[0] == "axis":
                    cur["axis"] = euler_xyz(float(w[1]), float(w[2]), float(w[3]))
                elif w[0] == "dof":
                    cur["dof"] = [d.lower() for d in w[1:]]
        elif section == ":hierarchy":
            w = l.split()
            if w and w[0] not in ("begin", "end"):
                hierarchy[w[0]] = w[1:]
        elif section == ":root":
            w = l.split()
            if w and w[0] == "axis":
                pass
        i += 1
    parent = {}
    for p, children in hierarchy.items():
        for c in children:
            parent[c] = p
    return bones, parent, hierarchy


def parse_amc(path):
    frames = []
    cur = None
    for l in open(path):
        l = l.strip()
        if not l or l.startswith("#") or l.startswith(":"):
            continue
        w = l.split()
        if w[0].isdigit() and len(w) == 1:
            cur = {}
            frames.append(cur)
        elif cur is not None:
            cur[w[0]] = [float(x) for x in w[1:]]
    return frames


def pose(bones, parent, hierarchy, frame):
    """Joint end positions and global bone matrices for one frame (the amc_parser convention)."""
    pos, mat = {}, {}
    root = frame["root"]
    mat["root"] = euler_xyz(root[3], root[4], root[5])
    pos["root"] = np.array(root[:3])
    order = ["root"]
    k = 0
    while k < len(order):
        for c in hierarchy.get(order[k], []):
            order.append(c)
        k += 1
    for name in order[1:]:
        b = bones[name]
        r = [0.0, 0.0, 0.0]
        vals = frame.get(name, [])
        for d, v in zip(b["dof"], vals):
            r["xyz".index(d[1])] = v
        C = b["axis"]
        mat[name] = mat[parent[name]] @ C @ euler_xyz(*r) @ C.T
        pos[name] = pos[parent[name]] + b["len"] * (mat[name] @ b["dir"])
    return pos, mat


def unity_euler_from_matrix(R):
    """Unity's Euler angles (degrees) for a rotation matrix R = Ry Rx Rz."""
    x = math.degrees(math.asin(max(-1.0, min(1.0, -R[1, 2]))))
    y = math.degrees(math.atan2(R[0, 2], R[2, 2]))
    z = math.degrees(math.atan2(R[1, 0], R[1, 1]))
    return [x, y, z]


def unity_matrix(e):
    return rot(1, e[1]) @ rot(0, e[0]) @ rot(2, e[2])


def basis_from(right, up):
    """A rotation whose columns are the right, up and forward axes (re-orthogonalised around up)."""
    up = up / np.linalg.norm(up)
    right = right - up * np.dot(right, up)
    right = right / np.linalg.norm(right)
    fwd = np.cross(right, up)  # a proper rotation: forward = right × up (as for the identity)
    return np.column_stack([right, up, fwd])


def convert(asf, amc, name, fps):
    bones, parent, hierarchy = parse_asf(asf)
    frames = parse_amc(amc)
    P = [pose(bones, parent, hierarchy, f) for f in frames]
    n = len(P)
    up = np.array([0.0, 1.0, 0.0])
    root = np.array([p[0]["root"] for p in P])
    travel = root[-1] - root[0]
    travel[1] = 0.0
    f = travel / np.linalg.norm(travel)
    right = np.cross(f, up)
    # The capture's handedness: the left hip must come out on the left.
    p0 = P[n // 2][0]
    if np.dot(p0["lhipjoint"] - p0["rhipjoint"], right) > 0:
        right = -right
    leg = bones["lfemur"]["len"] + bones["ltibia"]["len"]
    scale = RIG_LEG / leg

    def to_unity(v):
        return np.array([np.dot(v, right), np.dot(v, up), np.dot(v, f)])

    # Gait cycle: from one moment the left ankle is furthest forward (heel strike) to the next.
    rel = np.array([to_unity(p[0]["ltibia"] - p[0]["root"]) for p in P])
    z = rel[:, 2]
    strikes = [i for i in range(2, n - 2) if z[i] >= z[i - 1] and z[i] >= z[i + 1] and z[i] > np.percentile(z, 80)]
    # Merge near-duplicates and keep a cycle from the middle of the take (steady speed).
    merged = []
    for s in strikes:
        if not merged or s - merged[-1] > fps * 0.4:
            merged.append(s)
    if len(merged) < 2:
        raise SystemExit("no full gait cycle found")
    mid = len(merged) // 2
    a, b = merged[mid - 1], merged[mid]
    span = b - a
    rootU = np.array([to_unity(r) for r in root])
    stride = np.linalg.norm((rootU[b] - rootU[a]) * np.array([1, 0, 1])) * scale
    vel = (rootU[b] - rootU[a]) / span

    ankles = {s: np.array([to_unity(p[0][s + "tibia"]) for p in P]) for s in "lr"}
    toes = {s: np.array([to_unity(p[0][s + "foot"]) for p in P]) for s in "lr"}
    ground = min(ankles["l"][a:b + 1, 1].min(), ankles["r"][a:b + 1, 1].min())

    out = []
    pelvis_mean = None
    rows = []
    for k in range(FRAMES):
        t = a + span * k / FRAMES
        i0 = int(math.floor(t))
        w = t - i0
        i1 = min(i0 + 1, n - 1)

        def lerp(arr):
            return arr[i0] * (1 - w) + arr[i1] * w

        def J(name):
            return to_unity(P[i0][0][name]) * (1 - w) + to_unity(P[i1][0][name]) * w

        origin = rootU[a] + vel * (t - a)  # the body's frame, moving at its average speed
        pel = J("root")
        lhip, rhip = J("lhipjoint"), J("rhipjoint")
        upper = J("upperback")
        thorax = J("thorax")
        neck = J("lowerneck")
        head = J("head")
        lsh, rsh = J("lclavicle"), J("rclavicle")
        # Pelvis: right axis along the hips, up along the lower back.
        pelvisR = basis_from(rhip - lhip, upper - pel)
        chestR = basis_from(rsh - lsh, neck - upper)
        headR = basis_from(rsh - lsh, head - neck)
        pe = unity_euler_from_matrix(pelvisR)
        ce = unity_euler_from_matrix(pelvisR.T @ chestR)
        he = unity_euler_from_matrix(chestR.T @ headR)
        row = {
            "pelvis": ((pel - origin) * scale).tolist(),
            "pelvisRot": pe, "chestRot": ce, "headRot": he,
        }
        for s, side in (("l", "L"), ("r", "R")):
            sh, el, wr = J(s + "clavicle"), J(s + "humerus"), J(s + "radius")
            d = chestR.T @ ((el - sh) / np.linalg.norm(el - sh))  # upper arm in chest space
            zang = math.degrees(math.asin(max(-1.0, min(1.0, d[0]))))
            xang = math.degrees(math.atan2(-d[2], -d[1]))
            row["arm" + side] = [xang, 0.0, zang]
            u = (el - sh) / np.linalg.norm(el - sh)
            v = (wr - el) / np.linalg.norm(wr - el)
            row["fore" + side] = -math.degrees(math.acos(max(-1.0, min(1.0, float(np.dot(u, v))))))
            ank = lerp(ankles[s])
            toe = lerp(toes[s])
            foot = (ank - origin) * scale
            foot[1] = 0.08 + (ank[1] - ground) * scale
            row["foot" + side] = foot.tolist()
            fv = toe - ank
            row["pitch" + side] = math.degrees(math.atan2(-fv[1], fv[2]))
        rows.append(row)

    # Centre what the rig sets itself: feet keep their own sides, flat feet have no pitch, the pelvis
    # hangs at the rig's height (its bob and sway stay).
    for s in "LR":
        flat = sorted(r["pitch" + s] for r in rows)[len(rows) // 4]
        for r in rows:
            r["pitch" + s] -= flat
    # Rotations keep only their swing about the cycle's mean: the capture skeleton's rest posture is not the rig's.
    for key in ("pelvisRot", "chestRot", "headRot"):
        m = np.mean([r[key] for r in rows], axis=0)
        for r in rows:
            r[key] = [r[key][0] - m[0], r[key][1] - m[1], r[key][2] - m[2]]
    mean = np.mean([r["pelvis"] for r in rows], axis=0)
    for r in rows:
        r["pelvis"] = [r["pelvis"][0] - mean[0], r["pelvis"][1] - mean[1], r["pelvis"][2] - mean[2]]
    for s in "LR":
        mx = np.mean([r["foot" + s][0] for r in rows])
        for r in rows:
            r["foot" + s][0] -= mx
    duration = span / fps
    return {
        "name": name, "source": f"CMU mocap {asf.split('/')[-1].split('.')[0]}: {amc.split('/')[-1]}",
        "stride": round(float(stride), 4), "duration": round(duration, 4), "speed": round(float(stride / duration), 3),
        "frames": [{k: (round(v, 4) if isinstance(v, float) else [round(x, 4) for x in v]) for k, v in r.items()} for r in rows],
    }


def main():
    args = sys.argv[1:]
    fps = 120.0
    if "--fps" in args:
        i = args.index("--fps")
        fps = float(args[i + 1])
        del args[i:i + 2]
    if len(args) != 4:
        raise SystemExit(__doc__)
    clip = convert(args[0], args[1], args[2], fps)
    with open(args[3], "w") as fh:
        json.dump(clip, fh, separators=(",", ":"))
    print(f"{clip['name']}: stride {clip['stride']} m in {clip['duration']} s ({clip['speed']} m/s), {len(clip['frames'])} frames")


main()
