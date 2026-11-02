"""Serves and smash: the right arm solved again so the racket is held like the forehand.

Their racket keys (tennis_animations.py) asked for wrists no arm has: the racket hanging straight down behind
the head while the hand was above it, and forearms turned 135-165 degrees from thumb-up. relax_wrist clamped
those, so the hand flipped between frames (the serve at 27-28, the trophy the game holds during the toss, and
46-47 after contact; the smash at 7, 11 and 29) and the forearm looked like it turned all the way round.

Here the racket is held with the forehand's diagonal grip (AthleteAnimator turns it in over frames 0-8 and back
over the last 8), and the right arm is keyed in joint space: which way the elbow points, the forearm twist
(within TWIST_LIMIT of thumb-up) and the wrist bend (FLEX, DEV), interpolated between key poses. A key pose is
the closest the arm gets to a wanted racket while keeping the racket CLEAR of the body: the racket as it was
(track), then keys. After contact the hand swings out in front of the body (follow) rather than past the
shoulder, which folded the arm and flipped the forearm. The body stays as it was; the left hand takes the
racket where it is now.

groundstroke_animations.py runs this on the first bake of each clip, kept as <name>_source.
"""
import math
from pathlib import Path
import bpy
from mathutils import Matrix, Vector, Quaternion, kdtree
import tennis_animations as ta

DIAGONAL = 45         # in-game grip: the handle across the palm, degrees about the racket face (as the forehand)
TWIST_LIMIT = 80      # forearm twist from thumb-up, either way
FLEX = (-60, 60)      # wrist: palm flexion (-) / extension (+)
DEV = (-35, 20)       # wrist: ulnar (-) / radial (+) deviation (hand to forearm, whatever the grip)
FOREARM_SHARE = 0.6   # of the twist on the forearm bone, the rest on the hand (no twist bones), as relax_wrist
CLEAR = 0.05          # racket to body (m)
RACKET = Path(__file__).resolve().parents[1] / 'Props' / 'Final' / 'TennisRacket_LowPoly_Clean.blend'

# first, last: frames solved again (last and on stay as they were, only regripped).
# track: frames where the racket goes where it went. grasp_kept: the handle stays where it was up to [0] and from
# [3], the wrist from [1] to [2]. follow: the hand's way out in front after contact (follow_through).
# left: (frame, weight) of the left hand on the racket throat, as tennis_animations.py keys it.
# keys: frame -> (shaft, face, face weight) wanted, character r, f, u.
SERVE = dict(
    first=0, last=60, track=(0, 4, 8, 12, 16, 20, 24), grasp_kept=(24, 28, 52, 56),
    follow=dict(start=41, via=(45, (.25, -.15, 1., .45)), end=48), left=((0, 1), (12, 0), (48, 0), (58, 1)),
    # Trophy (the game holds 28 during the toss) with the racket up behind the head, racket drop behind it,
    # up to contact facing the net, pronated, down to the left
    keys={28: ((-.3, -.2, .93), (1, 0, 0), .02),
          32: ((-.45, -.45, -.77), (-.77, -.05, .64), .02),
          36: ((.3, -.9, .3), (-1, 0, 0), .02),
          40: ((-.35, .2, .92), (0, 1, 0), 1),
          44: ((.25, .85, -.45), (.9, -.3, 0), .3),
          48: ((-.5, -.3, -.8), (0, -1, 0), .02)})
CLIPS = {
    'Serve': SERVE,
    # Slice: across the ball and down to the left. Kick: up through the ball before coming down.
    'ServeSlice': dict(SERVE, keys={**SERVE['keys'], 40: ((-.25, .2, .95), (.3, 1, 0), 1), 44: ((-.5, .7, -.4), (.8, .5, 0), .3)}),
    'ServeKick': dict(SERVE, keys={**SERVE['keys'], 40: ((-.45, .15, .88), (0, 1, 0), 1), 44: ((.2, .5, .85), (.3, .7, -.6), .05)}),
    # The serve's swing from the ready pose. The game holds up to 15 while the ball comes, so 15 stays the
    # trophy and the racket drops behind the head on the way to contact
    'Smash': dict(
        first=0, last=40, track=(), grasp_kept=(0, 4, 34, 38),
        follow=dict(start=23, via=(27, (.25, -.15, 1., .45)), end=30), left=((0, 1), (8, 0), (30, 0), (44, 1)),
        keys={4: ((0, .6, .8), (1, 0, 0), .02),
              8: ((-.3, -.2, .93), (1, 0, 0), .02),
              15: ((-.3, -.2, .93), (1, 0, 0), .02),
              18: ((-.45, -.45, -.77), (-.77, -.05, .64), .02),
              20: ((.3, -.9, .3), (-1, 0, 0), .02),
              22: ((-.35, .2, .92), (0, 1, 0), 1),
              26: ((.25, .85, -.45), (.9, -.3, 0), .3),
              30: ((-.5, -.3, -.8), (0, -1, 0), .02)}),
}


def matrix(q, position):
    m = q.to_matrix().to_4x4(); m.translation = position
    return m


def grip_weight(f, end):
    """How far the game's diagonal grip is in (AthleteAnimator.ApplyRacketGrip)."""
    t = max(0., min(1., f / 8., (end - f) / 8.))
    return t * t * (3 - 2 * t)


def grip(f, end):
    """The racket in the hand: the ready grip turned diagonally about the grasp point."""
    g = Matrix.Translation((0, 0, ta.RIGHT_GRIP))
    return ta.racket_in_hand() @ g @ Matrix.Rotation(math.radians(DIAGONAL * grip_weight(f, end)), 4, 'Y') @ g.inverted()


def neutral(a, e, w):
    """Upper arm and forearm with the elbow hinge across them: forearm twist 0 is thumb-up."""
    u = (e - a).normalized(); v = (w - e).normalized(); z = u.cross(v).normalized()
    return Matrix((u.cross(z), u, z)).transposed().to_quaternion(), Matrix((v.cross(z), v, z)).transposed().to_quaternion()


def decompose(forearm, hand):
    """Hand relative to the twist-free forearm -> twist about the forearm, wrist bend (flex, deviation)."""
    rel = forearm.inverted() @ hand
    if rel.w < 0: rel.negate()
    twist = 2 * math.atan2(rel.y, rel.w)
    axis, angle = (Quaternion((0, 1, 0), twist).inverted() @ rel).to_axis_angle()
    if angle > math.pi: angle -= 2 * math.pi
    return twist, Vector((axis.x * angle, axis.z * angle))


def bend_q(bend):
    angle = bend.length
    return Quaternion() if angle < 1e-9 else Quaternion((bend.x / angle, 0, bend.y / angle), angle)


def elbow_on_circle(arm, a, w, hint):
    """The elbow toward hint, and the wrist pulled within reach."""
    l1 = arm.data.bones['RightArm'].length; l2 = arm.data.bones['RightForeArm'].length
    axis = w - a; d = axis.length; axis.normalize()
    d = min(l1 + l2 - .012, max(.08, d))
    along = (l1 * l1 - l2 * l2 + d * d) / (2 * d); radius = math.sqrt(max(0, l1 * l1 - along * along))
    h = hint - a; h = (h - axis * h.dot(axis)).normalized()
    return a + axis * along + h * radius, a + axis * d, axis, h


def bend_dir(a, e, w):
    """Which way the elbow points off the shoulder-wrist line. Interpolating this rather than the elbow position
    keeps the elbow from jumping across the line when the position passes close to it."""
    axis = (w - a).normalized()
    return ((e - a) - axis * (e - a).dot(axis)).normalized()


def joints(mats):
    """Joint values of a pose: elbow direction in the chest's axes, total twist, forearm bone twist, wrist bend."""
    a, e, w = (mats[b].translation.copy() for b in ('RightArm', 'RightForeArm', 'RightHand'))
    _, fore = neutral(a, e, w)
    twist, bend = decompose(fore, mats['RightHand'].to_quaternion())
    q = fore.inverted() @ mats['RightForeArm'].to_quaternion()
    if q.w < 0: q.negate()
    return dict(elbow=mats['Spine2'].to_quaternion().inverted() @ bend_dir(a, e, w), twist=twist, roll=2 * math.atan2(q.y, q.w), bend=bend)


def set_joints(arm, mats, p, anchor):
    """Joint values -> right arm bones; the wrist goes where wrist_at puts it for that hand."""
    a = mats['RightArm'].translation.copy(); w = mats['RightHand'].translation.copy()
    body = mats['Spine2'].to_quaternion()
    for _ in range(4):
        e, w2, _, _ = elbow_on_circle(arm, a, w, a + body @ p['elbow'])
        upper, fore = neutral(a, e, w2)
        w = wrist_at(anchor, fore @ Quaternion((0, 1, 0), p['twist']) @ bend_q(p['bend']))
    mats['RightArm'] = matrix(upper, a)
    mats['RightForeArm'] = matrix(fore @ Quaternion((0, 1, 0), p['roll']), e)
    mats['RightHand'] = matrix(fore @ Quaternion((0, 1, 0), p['twist']) @ bend_q(p['bend']), w2)


def grasp_point():
    """Where the hand holds the handle, in hand space (the diagonal grip turns about it)."""
    return ta.racket_in_hand() @ Vector((0, 0, ta.RIGHT_GRIP))


def wrist_at(anchor, hand):
    """anchor = (wrist, grasp, weight): 1 keeps the handle where it was (the hand turns about the grasp point, like
    the game's grip change), 0 keeps the wrist where it was."""
    wrist, grasp, k = anchor
    return wrist.lerp(grasp - hand @ grasp_point(), k)


def racket_points(count=48):
    """Spread-out vertices of the racket model (origin at the butt, Z along the shaft)."""
    with bpy.data.libraries.load(str(RACKET)) as (src, dst): dst.meshes = list(src.meshes)
    verts = [v.co.copy() for me in dst.meshes for v in me.vertices]
    materials = {m for me in dst.meshes for m in me.materials if m}
    for me in dst.meshes: bpy.data.meshes.remove(me)
    for m in materials:
        if not m.users: bpy.data.materials.remove(m)
    picked = [min(verts, key=lambda v: v.z)]; dist = [(v - picked[0]).length for v in verts]
    while len(picked) < count:
        i = max(range(len(verts)), key=dist.__getitem__); picked.append(verts[i])
        dist = [min(d, (v - verts[i]).length) for d, v in zip(dist, verts)]
    return picked


def body_tree(arm, f, left_free):
    """The skinned body at frame f of the active action, without the right arm and, while it holds the racket,
    the left forearm and hand."""
    bpy.context.scene.frame_set(f); dg = bpy.context.evaluated_depsgraph_get()
    skip = ('RightArm', 'RightForeArm', 'RightHand', 'RightShoulder') + (() if left_free else ('LeftForeArm', 'LeftHand'))
    inv = arm.matrix_world.inverted(); pts = []
    for o in bpy.context.scene.objects:
        if o.type != 'MESH' or o.parent != arm or not o.visible_get(): continue
        names = {g.index: g.name for g in o.vertex_groups}
        ev = o.evaluated_get(dg); me = ev.to_mesh(); M = inv @ ev.matrix_world
        for v in o.data.vertices:
            if sum(g.weight for g in v.groups if names.get(g.group) in skip) < .2: pts.append(M @ me.vertices[v.index].co)
        ev.to_mesh_clear()
    tree = kdtree.KDTree(len(pts))
    for k, q in enumerate(pts): tree.insert(q, k)
    tree.balance()
    return tree


def clearance_cost(points, tree):
    return 3000 * sum(max(0., CLEAR - tree.find(p)[2]) ** 2 for p in points)


def solve_key(arm, mats, racket, face_weight, held, previous, anchor, body, torso, shaft_weight=10):
    """Key pose for a wanted racket (shaft, face). Where the handle is kept, the wrist follows the answer, so solve
    again from there, staying with the first answer."""
    w = wrist_at(anchor, mats['RightHand'].to_quaternion()); p = None
    for _ in range(3 if anchor[2] > 0 else 1):
        p, hand = solve_at(arm, mats, w, racket, face_weight, held, previous, body, torso, shaft_weight, p)
        w = wrist_at(anchor, hand)
    return p


def solve_at(arm, mats, w, racket, face_weight, held, previous, body, torso, shaft_weight, stay):
    """Closest racket the arm can hold with the wrist at w: elbow direction, twist and wrist bend in range, the
    racket clear of the body tree and the arm of the torso, close to the previous key. held = (grip, racket points)."""
    R = math.radians
    a = mats['RightArm'].translation.copy(); chest = mats['Spine2'].to_quaternion()
    st, nt = ta.racket_axes(ta.D(*racket[0]), ta.D(*racket[1]))
    g = held[0].to_quaternion(); points = [held[0] @ v for v in held[1]]
    _, w2, axis, ref = elbow_on_circle(arm, a, w, mats['RightForeArm'].translation)
    def toward(d):
        e, _, _, _ = elbow_on_circle(arm, a, w2, a + chest @ d)
        return bend_dir(a, e, w2), e
    prev_bend, prev_e = toward(previous['elbow'])
    stay_bend = toward(stay['elbow'])[0] if stay else None
    torso0 = torso(mats, arm, a, mats['RightForeArm'].translation, w2)  # the pose as it was may touch it already
    bends = [(fx, dz, bend_q(Vector((R(fx), R(dz)))) @ g) for fx in range(FLEX[0], FLEX[1] + 1, 10) for dz in range(DEV[0], DEV[1] + 1, 5)]
    best = None
    for deg in range(-150, 151, 5):
        e, _, _, _ = elbow_on_circle(arm, a, w2, a + Quaternion(axis, R(deg)) @ ref)
        d = bend_dir(a, e, w2)
        base = (.3 * R(deg) ** 2 + 10000 * max(0., torso(mats, arm, a, e, w2) - torso0)
                + 30 * (e - prev_e).length_squared + d.angle(prev_bend) ** 2 + (3 * d.angle(stay_bend) ** 2 if stay else 0))
        _, fore = neutral(a, e, w2)
        for twist in range(-TWIST_LIMIT, TWIST_LIMIT + 1, 5):
            qt = fore @ Quaternion((0, 1, 0), R(twist))
            ct = base + .2 * R(twist) ** 2 + .15 * (R(twist) - previous['twist']) ** 2 + (3 * (R(twist) - stay['twist']) ** 2 if stay else 0)
            for fx, dz, qb in bends:
                m = (qt @ qb).to_matrix()
                bend = Vector((R(fx), R(dz)))
                cost = (ct + shaft_weight * m.col[2].angle(st) ** 2 + face_weight * m.col[1].angle(nt) ** 2 + .2 * bend.length_squared
                        + .1 * (bend - previous['bend']).length_squared + (3 * (bend - stay['bend']).length_squared if stay else 0))
                if best is not None and cost >= best[0]: continue
                hand = qt @ qb @ g.inverted()
                cost += clearance_cost([w2 + hand @ p for p in points], body)
                if best is None or cost < best[0]: best = (cost, e, R(twist), bend, hand)
    _, e, twist, bend, hand = best
    return dict(elbow=chest.inverted() @ bend_dir(a, e, w2), twist=twist, roll=FOREARM_SHARE * twist, bend=bend), hand


def catmull(p0, p1, p2, p3, t):
    return .5 * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t + (-p0 + 3 * p1 - 3 * p2 + p3) * t ** 3)


def between(keys, f):
    """Joint values at f, a Catmull-Rom spline through the key poses."""
    fs = sorted(keys)
    i = max(j for j in range(len(fs) - 1) if fs[j] <= f)
    t = (f - fs[i]) / (fs[i + 1] - fs[i])
    k = [keys[fs[max(0, i - 1)]], keys[fs[i]], keys[fs[i + 1]], keys[fs[min(len(fs) - 1, i + 2)]]]
    p = {n: catmull(*[x[n] for x in k], t) for n in ('elbow', 'twist', 'bend')}
    p['elbow'].normalize()
    # Within range, or between the two keys where a pose kept as it was is outside it (the ready wrist)
    def clamp(v, lo, hi, a, b): return max(min(lo, a, b), min(max(hi, a, b), v))
    R = math.radians
    p['twist'] = clamp(p['twist'], -R(TWIST_LIMIT + 5), R(TWIST_LIMIT + 5), k[1]['twist'], k[2]['twist'])
    p['roll'] = FOREARM_SHARE * p['twist']
    p['bend'] = Vector((clamp(p['bend'].x, R(FLEX[0]), R(FLEX[1]), k[1]['bend'].x, k[2]['bend'].x),
                        clamp(p['bend'].y, R(DEV[0]), R(DEV[1]), k[1]['bend'].y, k[2]['bend'].y)))
    return p


def regrip(arm, mats, held):
    """Same racket in the same place, held with the diagonal grip: the hand turns about the grasp point."""
    hand = mats['RightHand'].to_quaternion() @ ta.racket_in_hand().to_quaternion() @ held.to_quaternion().inverted()
    a = mats['RightArm'].translation.copy()
    grasp = mats['RightHand'] @ grasp_point()
    e, w, _, _ = elbow_on_circle(arm, a, grasp - hand @ grasp_point(), mats['RightForeArm'].translation)
    upper, fore = neutral(a, e, w)
    twist, bend = decompose(fore, hand)
    mats['RightArm'] = matrix(upper, a)
    mats['RightForeArm'] = matrix(fore @ Quaternion((0, 1, 0), FOREARM_SHARE * twist), e)
    mats['RightHand'] = matrix(hand, w)


def follow_through(frames, grasp, start, via, end):
    """The hand swings out in front of the body at arm's length (via: frame, (left, up, forward) in the chest's
    axes, distance from the shoulder) instead of straight from above the head to the hip past the shoulder."""
    vf, (x, y, z, radius) = via
    def rel(f):
        m = frames[f]
        return m['Spine2'].to_quaternion().inverted() @ (grasp[f] - m['RightArm'].translation)
    keys = {start - 4: rel(start - 4), start: rel(start), vf: Vector((x, y, z)).normalized() * radius, end: rel(end), end + 4: rel(end + 4)}
    fs = sorted(keys)
    for f in range(start + 1, end):
        i = max(j for j in range(len(fs) - 1) if fs[j] <= f)
        t = (f - fs[i]) / (fs[i + 1] - fs[i])
        p = [keys[fs[k]] for k in (i - 1, i, i + 1, i + 2)]
        d = catmull(*[q.normalized() for q in p], t).normalized() * catmull(*[q.length for q in p], t)
        grasp[f] = frames[f]['RightArm'].translation + frames[f]['Spine2'].to_quaternion() @ d


def left_hold(mats, racket, weight):
    """ta.hold_racket_left on the racket as the game holds it."""
    s = racket.col[2].to_3d().normalized(); n = racket.col[1].to_3d().normalized()
    target = ta.hand_matrix(racket.translation + s * ta.THROAT, s, -n, False)
    shoulder = mats['LeftArm'].translation
    to = target.translation - shoulder
    reach = min(max(to.length, abs(ta.UPPER_ARM - ta.FOREARM) + 1e-3), ta.UPPER_ARM + ta.FOREARM - 1e-3)
    axis = to.normalized()
    side = ta.elbow_pole(shoulder, target, mats['LeftForeArm'].translation - shoulder) - shoulder
    side = (side - axis * side.dot(axis)).normalized()
    a = (ta.UPPER_ARM ** 2 - ta.FOREARM ** 2 + reach * reach) / (2 * reach)
    elbow = shoulder + axis * a + side * math.sqrt(max(0.0, ta.UPPER_ARM ** 2 - a * a))
    wrist = shoulder + axis * reach
    for bone, head, tail in (('LeftArm', shoulder, elbow), ('LeftForeArm', elbow, wrist)):
        q = mats[bone].to_quaternion()
        aimed = (mats[bone].to_3x3() @ Vector((0, 1, 0))).rotation_difference((tail - head).normalized()) @ q
        mats[bone] = matrix(q.slerp(aimed, weight), mats[bone].translation.lerp(head, weight))
    mats['LeftHand'] = matrix(mats['LeftHand'].to_quaternion().slerp(target.to_quaternion(), weight), mats['LeftHand'].translation.lerp(wrist, weight))


def left_weight(f, keys):
    """How far the left hand holds the racket throat at f, linear between (frame, weight) keys."""
    for (f0, w0), (f1, w1) in zip(keys, keys[1:]):
        if f0 <= f <= f1: return w0 + (w1 - w0) * (f - f0) / (f1 - f0)
    return keys[-1][1]


def solve(arm, act, name, torso_penalty):
    """Pose matrices per frame of the corrected clip, from its first bake act (made the active action here).
    torso_penalty: groundstroke_animations.torso_penalty."""
    clip = CLIPS[name]
    ta.GRIP_ANGLE = 0
    ad = arm.animation_data; ad.action = act; ad.action_slot = act.slots[0]
    end = int(act.frame_range[1]); frames = []
    for f in range(end + 1):
        bpy.context.scene.frame_set(f); bpy.context.view_layer.update()
        frames.append({p.name: p.matrix.copy() for p in arm.pose.bones})
    held = [grip(f, end) for f in range(end + 1)]
    original = [m['RightHand'] @ ta.racket_in_hand() for m in frames]
    grasp = [r @ Vector((0, 0, ta.RIGHT_GRIP)) for r in original]
    before = [g.copy() for g in grasp]
    follow_through(frames, grasp, **clip['follow'])
    wrist = [m['RightHand'].translation + g - b for m, g, b in zip(frames, grasp, before)]
    lo0, lo1, hi0, hi1 = clip['grasp_kept']
    def kept(f):
        t = 1. if f <= lo0 or f >= hi1 else 0. if lo1 <= f <= hi0 else (lo1 - f) / (lo1 - lo0) if f < lo1 else (f - hi0) / (hi1 - hi0)
        return t * t * (3 - 2 * t)
    anchor = [(wrist[f], grasp[f], kept(f)) for f in range(end + 1)]
    points = racket_points()
    wanted = {f: ((-original[f].col[2].x, -original[f].col[2].y, original[f].col[2].z),
                  (-original[f].col[1].x, -original[f].col[1].y, original[f].col[1].z), 3) for f in clip['track']}
    wanted.update(clip['keys'])
    bodies = {f: body_tree(arm, f, left_weight(f, clip['left']) == 0) for f in wanted}
    for f, m in enumerate(frames): regrip(arm, m, held[f])
    first, last = clip['first'], clip['last']
    keys = {f: joints(frames[f]) for f in (last, last + 4)}
    if first in wanted:
        previous = dict(joints(frames[first]), twist=0.)  # the forearm as it was could be past its range
    else:
        keys[first] = previous = joints(frames[first])   # starts as it was (the smash from the ready pose)
    for f in sorted(wanted):
        shaft, face, face_weight = wanted[f]
        keys[f] = previous = solve_key(arm, frames[f], (shaft, face), face_weight, (held[f], points), previous, anchor[f], bodies[f], torso_penalty)
    for f in range(first if first in wanted else first + 1, last): set_joints(arm, frames[f], between(keys, f), anchor[f])
    for f, m in enumerate(frames):
        w = left_weight(f, clip['left'])
        if w > 0: left_hold(m, m['RightHand'] @ held[f], w)
    near = min(bodies[f].find(frames[f]['RightHand'] @ held[f] @ p)[2] for f in bodies for p in points)
    twists = [math.degrees(joints(m)['twist']) for m in frames]
    print(f'[overhead] {name}: twist {min(twists):.0f}..{max(twists):.0f}, racket to body at keys {near * 100:.1f} cm')
    return frames
