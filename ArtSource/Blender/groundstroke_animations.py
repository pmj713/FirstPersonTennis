"""Rebuild only Forehand on TennisPlayer_Tennis.blend.

Run Blender -b <existing tennis blend> --python this_file -- <out.blend> <out.fbx> <existing.fbx>.
Contact remains frame 24, end 48. Backhand, other NLA actions and mesh data are preserved.
AthleteAnimator applies the matching grip only in these two clips: forehand
face rotation, and a diagonal backhand handle with a separately oriented face.
"""
import sys, math
from pathlib import Path
from copy import deepcopy
import bpy
from mathutils import Matrix, Vector, Quaternion
sys.path.insert(0, str(Path(__file__).parent))
import tennis_animations as ta

REBUILD_CLIPS = ('Forehand',)

# Wrist targets, not racket-head positions. The finish stays beside the shoulder.
# frame: (right wrist, shaft guide for authoring, free left wrist).
# The shaft is driven by the elbow and continuous forearm roll, not an IK target.
POSES = {
    'Forehand': {
        0: ((.10,.30,1.05),(-.3,.6,.7),(-.10,.34,1.10)),
        8: ((.42,.08,1.18),(.1,-.25,1),(-.05,.40,1.23)),
        16: ((.46,-.24,1.20),(.2,-.7,.65),(.10,.40,1.20)),
        20: ((.44,-.02,.98),(.3,-.9,-.2),(-.12,.32,1.13)),
        24: ((.34,.37,1.06),(1,.05,.1),(-.28,.18,1.08)),
        28: ((.10,.55,1.22),(-.6,.4,.6),(-.32,.14,1.04)),
        34: ((-.12,.46,1.32),(-.6,-.8,.1),(-.32,.10,1.02)),
        40: ((-.32,.12,1.40),(-.4,-.9,.05),(-.32,.10,1.02)),
        48: ((.10,.30,1.05),(-.3,.6,.7),(-.10,.34,1.10)),
    },
    'Backhand': {
        0: ((.04,.32,1.08),(-.3,.6,.7),None),
        8: ((-.27,.29,1.18),(-.2,-.2,1),None),
        16: ((-.42,.20,1.16),(-.3,-.6,.7),None),
        20: ((-.32,.34,1.00),(-1,-.4,-.2),None),
        24: ((-.04,.54,1.12),(-1,.05,.1),None),
        28: ((.12,.49,1.22),(.6,.4,.6),None),
        34: ((.38,.36,1.32),(.6,-.8,.1),None),
        40: ((.33,.22,1.32),(.4,-.9,.05),None),
        48: ((.04,.32,1.08),(-.3,.6,.7),None),
    },
}
ELBOWS = {
 'Forehand': [( .30,.12,.95),(.36,-.10,1.02),(.40,-.22,1.02),(.32,-.05,.91),(.26,.10,.96),(.20,.24,1.03),(.04,.32,1.13),(.02,.28,1.13),(.30,.12,.95)],
 'Backhand': [(.25,.12,.95),(.12,.25,1.05),(.02,.30,1.12),(.03,.35,1.00),(.10,.32,1.03),(.20,.34,1.13),(.32,.27,1.16),(.33,.20,1.16),(.25,.12,.95)],
}
for name in POSES:
    for f,elbow in zip(POSES[name],ELBOWS[name]):POSES[name][f]=(*POSES[name][f],elbow)
ROLLS = {'Forehand': [28,40,73,85,85,105,-55,-55,28],
         'Backhand': [25,0,-20,10,-10,10,10,20,25]}
for name in POSES:
    for f,roll in zip(POSES[name],ROLLS[name]):POSES[name][f]=(*POSES[name][f],(0,0,roll))
for f,bend in zip(POSES['Backhand'],[(0,0,0),(10,0,10),(0,0,0),(-10,0,-30),(-30,0,-30),(40,0,0),(0,0,40),(0,0,50),(0,0,0)]):
    POSES['Backhand'][f]=(*POSES['Backhand'][f],bend)
for f,bend in zip(POSES['Forehand'],[(0,0,0),(15,0,0),(25,0,0),(30,0,0),(0,0,0),(10,0,-10),(0,0,-10),(-10,0,-30),(0,0,0)]):
    POSES['Forehand'][f]=(*POSES['Forehand'][f],bend)

def envelope(f):
    t = min(1., f / 8., (48-f) / 8.)
    return t*t*(3-2*t)

def sample(keys, f, column):
    fs = list(keys)
    for i in range(len(fs)-1):
        if fs[i] <= f <= fs[i+1]:
            t = (f-fs[i])/(fs[i+1]-fs[i])
            a, b = ta.P(*keys[fs[i]][column]), ta.P(*keys[fs[i+1]][column])
            # Smooth Hermite with finite-difference tangents, including unequal spacing.
            p, n = max(0,i-1), min(len(fs)-1,i+2)
            va = (b-ta.P(*keys[fs[p]][column]))/(fs[i+1]-fs[p])
            vb = (ta.P(*keys[fs[n]][column])-a)/(fs[n]-fs[i])
            return a*(2*t**3-3*t*t+1)+b*(-2*t**3+3*t*t)+(va*(t**3-2*t*t+t)+vb*(t**3-t*t))*(fs[i+1]-fs[i])

def matrix(q, position):
    m=q.to_matrix().to_4x4(); m.translation=position
    return m

def torso_penalty(mats, arm, shoulder, elbow, wrist):
    """Keep arm segments outside a conservative torso envelope, including arm thickness."""
    rotation=mats['Spine2'].to_quaternion()@arm.data.bones['Spine2'].matrix_local.to_quaternion().inverted()
    center=mats['Spine2'].translation-rotation@Vector((0,0,.12))
    inv=rotation.inverted()
    penalty=0.
    for a,b in [(shoulder,elbow),(elbow,wrist)]:
        for t in [.25,.5,.75,1.]:
            p=inv@(a.lerp(b,t)-center)
            distance=math.sqrt((p.x/.23)**2+(p.y/.17)**2+(p.z/.32)**2)
            penalty+=max(0.,1.-distance)**2
    return penalty

def arm_pose(mats, arm, side, wrist, hint, hand=None, previous=None, finish=False):
    """Anatomical elbow plane; forearm pronation is separated from wrist bending."""
    a=mats[side+'Arm'].translation
    l1=arm.data.bones[side+'Arm'].length
    l2=arm.data.bones[side+'ForeArm'].length
    axis=wrist-a; distance=axis.length; axis.normalize()
    d=min(l1+l2-.012,max(.08,distance)); wrist=a+axis*d
    along=(l1*l1-l2*l2+d*d)/(2*d)
    radius=math.sqrt(max(0,l1*l1-along*along))
    preferred=(hint-axis*hint.dot(axis)).normalized()
    best=None
    for deg in range(-120,121,3):
        bend=Quaternion(axis,math.radians(deg))@preferred
        elbow=a+axis*along+bend*radius
        u=(elbow-a).normalized(); v=(wrist-elbow).normalized()
        z=u.cross(v).normalized()*(1 if side=='Right' else -1)
        qu=Matrix((u.cross(z),u,z)).transposed().to_quaternion()
        qf=Matrix((v.cross(z),v,z)).transposed().to_quaternion()
        cost=.2*math.radians(deg)**2
        if hand is not None:
            q=qf.inverted()@hand.to_quaternion()
            if q.w<0:q.negate()
            twist=2*math.atan2(q.y,q.w)
            roll=twist if finish else max(-math.radians(85),min(math.radians(85),twist))
            qf=qf@Quaternion((0,1,0),roll)
            bend_angle=v.angle(hand.col[1].to_3d())
            cost=(100 if finish else 15)*bend_angle**2+(0 if finish else 30)*max(0,abs(twist)-math.radians(85))**2+.2*math.radians(deg)**2
        cost+=10000*torso_penalty(mats,arm,a,elbow,wrist)
        if previous is not None:cost+=(200 if finish else 1500)*(elbow-previous).length_squared
        if best is None or cost<best[0]:best=(cost,elbow,qu,qf)
    _,elbow,qu,qf=best
    mats[side+'Arm']=matrix(qu,a)
    mats[side+'ForeArm']=matrix(qf,elbow)
    mats[side+'Hand']=matrix(qf if hand is None else hand.to_quaternion(),wrist)

def correct(mats, arm, name, f, previous=None):
    ta.GRIP_ANGLE=math.radians(45 if name=='Backhand' else 0)
    keys=POSES[name]
    wrist=sample(keys,f,0)
    hint=sample(keys,f,3)-(wrist+mats['RightArm'].translation)/2
    arm_pose(mats,arm,'Right',wrist,hint)
    fore=mats['RightForeArm']
    roll=math.radians(max(-85,min(140 if name=='Forehand' else 85,sample(keys,f,4).z)))
    q=fore.to_quaternion()@Quaternion((0,1,0),roll)
    mats['RightForeArm']=matrix(q,fore.translation)
    # A modest wrist deviation completes the racket orientation, never a forced flip.
    hand_q=q
    if name in ('Forehand','Backhand'):
        bend=sample(keys,f,5)
        hand_q=q@Quaternion((1,0,0),math.radians(-bend.x))@Quaternion((0,0,1),math.radians(bend.z))
    mats['RightHand']=matrix(hand_q,mats['RightHand'].translation)
    if name=='Forehand' and f==28 and previous is not None:
        previous['FinishHand']=hand_q.copy()
        previous['LastFinishHand']=hand_q.copy()
        previous['Right']=mats['RightForeArm'].translation.copy()
    if name=='Forehand' and 28<f:
        # Carry the head around the outside of the shoulder, not over the face.
        t=min(1.,(f-28)/12.);t=t*t*(3-2*t)
        # Sweep forward, left, then back; the shortest rotation cuts through the head.
        angle=math.radians(-65+235*t)
        shaft=Vector((math.sin(angle),-math.cos(angle),-.08*t)).normalized()
        wrist=mats['RightHand'].translation.copy()
        direction=(wrist-mats['RightForeArm'].translation).normalized()
        y=(direction-shaft*direction.dot(shaft)).normalized()
        x=-shaft;z=x.cross(y).normalized();y=z.cross(x).normalized()
        target=matrix(Matrix((x,y,z)).transposed().to_quaternion(),wrist)
        ease=min(1.,(f-28)/4.);ease=ease*ease*(3-2*ease)
        if previous is not None and 'FinishHand' in previous:
            target=matrix(previous['FinishHand'].slerp(target.to_quaternion(),ease),wrist)
            last=previous['LastFinishHand'];qtarget=target.to_quaternion()
            angle=last.rotation_difference(qtarget).angle
            angle=min(angle,2*math.pi-angle)
            target=matrix(last.slerp(qtarget,min(1.,math.radians(32)/max(angle,1e-6))),wrist)
            previous['LastFinishHand']=target.to_quaternion()
        arm_pose(mats,arm,'Right',wrist,hint,target,previous.get('Right') if previous else None,finish=True)
        if previous is not None:previous['Right']=mats['RightForeArm'].translation.copy()
    if name=='Backhand':
        racket=mats['RightHand']@ta.racket_in_hand()
        s=racket.col[2].to_3d(); n=racket.col[1].to_3d()
        # Left hand is stacked above the right, with its own bevel, not mirrored wrists.
        palm=Quaternion(s,math.radians(-45))@(-n)
        lh=ta.hand_matrix(racket.translation+s*ta.LEFT_GRIP,s,palm,False)
        arm_pose(mats,arm,'Left',lh.translation,ta.P(-.45,.25,-.75),lh,
                 previous.get('Left') if previous is not None else None)
        if previous is not None:previous['Left']=mats['LeftForeArm'].translation.copy()
    else:
        arm_pose(mats,arm,'Left',sample(keys,f,2),ta.P(-.4,.15,-.8))

def replace_takes(existing, generated):
    """Keep the existing FBX verbatim at element level except the selected animation graphs.

    A full Blender export can change unkeyed defaults in imported locomotion.
    Remap the selected takes onto the original models instead of replacing those assets.
    """
    from collections import Counter
    from io_scene_fbx import parse_fbx, encode_bin
    old,version=parse_fbx.parse(str(existing));new,new_version=parse_fbx.parse(str(generated))
    assert version==new_version
    def section(root,name):return next(e for e in root.elems if e.id==name)
    def graph(root):
        objects={e.props[0]:e for e in section(root,b'Objects').elems}
        links=section(root,b'Connections').elems
        ids={i for i,e in objects.items() if e.id==b'AnimationStack' and e.props[1].split(b'\0')[0] in {n.encode() for n in REBUILD_CLIPS}}
        assert len(ids)==len(REBUILD_CLIPS)
        for _ in range(3):ids|={c.props[1] for c in links if c.props[2] in ids}
        assert all(objects[i].id.startswith(b'Animation') for i in ids)
        return objects,links,ids
    oo,oc,oi=graph(old);no,nc,ni=graph(new)
    mapping={i:max(oo)+1+j for j,i in enumerate(sorted(ni))}
    external={c.props[2] for c in nc if c.props[1] in ni and c.props[2] not in ni}
    for i in external:
        matches=[j for j,e in oo.items() if e.id==no[i].id and e.props[1:]==no[i].props[1:]]
        assert len(matches)==1,(i,matches)
        mapping[i]=matches[0]
    def clone(e):
        return deepcopy(e)
    root=clone(old)
    objects=section(root,b'Objects')
    objects.elems[:]=[e for e in objects.elems if e.props[0] not in oi]
    for i in sorted(ni):
        e=clone(no[i]);e.props[0]=mapping[i];objects.elems.append(e)
    links=section(root,b'Connections')
    links.elems[:]=[e for e in links.elems if e.props[1] not in oi and e.props[2] not in oi]
    for c in nc:
        if c.props[1] in ni:
            e=clone(c);e.props[1]=mapping[c.props[1]];e.props[2]=mapping[c.props[2]];links.elems.append(e)
    counts=Counter(e.id for e in objects.elems)
    definitions=section(root,b'Definitions')
    for e in definitions.elems:
        if e.id==b'Count':e.props[0]=len(objects.elems)
        if e.id==b'ObjectType':section(e,b'Count').props[0]=counts[e.props[0]]
    def encode(e):
        methods={'Y':'int16','C':'char','B':'bool','Z':'int8','I':'int32','L':'int64','F':'float32','D':'float64',
                 'R':'bytes','S':'string','f':'float32_array','d':'float64_array',
                 'i':'int32_array','l':'int64_array','b':'bool_array','c':'byte_array'}
        result=encode_bin.FBXElem(e.id)
        for kind,value in zip(e.props_type,e.props):getattr(result,'add_'+methods[chr(kind)])(value)
        result.elems=[encode(c) for c in e.elems]
        return result
    encode_bin.write(str(generated),encode(root),version)

def main():
    out=sys.argv[sys.argv.index('--')+1:]
    if len(out)>2 and Path(out[1]).resolve()==Path(out[2]).resolve():
        raise ValueError('Export to a staging FBX, distinct from the existing FBX used to preserve other takes.')
    scene=bpy.context.scene; scene.render.fps=30
    arm=next(o for o in scene.objects if o.type=='ARMATURE')
    ad=arm.animation_data
    original_pose={p.name:(p.location.copy(),p.rotation_quaternion.copy(),p.scale.copy()) for p in arm.pose.bones}
    muted={t.name:t.mute for t in ad.nla_tracks}
    for t in ad.nla_tracks:t.mute=True
    originals={n:next(t.strips[0].action for t in ad.nla_tracks if t.name==n) for n in REBUILD_CLIPS}
    rig=ta.Rig(arm); rig.calibrate_poles()
    bodies={}
    for name in REBUILD_CLIPS:
        length,keys=deepcopy(ta.CLIPS[name])
        yaw={8:(-25,-55),16:(-35,-75),20:(-25,-50),24:(-5,0),30:(15,30),36:(20,45)}
        sign=1 if name=='Forehand' else -1
        for k in keys:
            if k['frame'] in yaw:
                h,t=yaw[k['frame']]; k.update(hips=h*sign,torso=t*sign)
            if name=='Backhand' and k['frame']==24:k.update(hips=10,torso=30)
            if name=='Backhand' and k['frame']==16:k.update(hips=42,torso=85)
            if name=='Backhand' and k['frame']==30:k.update(hips=-22,torso=-40)
            if name=='Backhand' and k['frame']==36:k.update(hips=-30,torso=-60)
            if name=='Forehand' and k['frame']==16:k.update(hips=-42,torso=-85)
            if name=='Forehand' and k['frame']==30:k.update(hips=22,torso=40)
            if name=='Forehand' and k['frame']==36:k.update(hips=30,torso=60)
        bodies[name]=rig.build('Body_'+name,length,keys)
    rig.cleanup()
    baked={}
    for name in REBUILD_CLIPS:
        frames=[];previous={}
        for f in range(49):
            ad.action=bodies[name];ad.action_slot=bodies[name].slots[0]
            scene.frame_set(f);bpy.context.view_layer.update()
            mats={p.name:p.matrix.copy() for p in arm.pose.bones}
            correct(mats,arm,name,f,previous)
            w=envelope(f)
            if w<1:
                ad.action=originals[name];ad.action_slot=originals[name].slots[0]
                scene.frame_set(0);bpy.context.view_layer.update()
                for bone in mats:
                    old=arm.pose.bones[bone].matrix
                    mats[bone]=matrix(old.to_quaternion().slerp(mats[bone].to_quaternion(),w),old.translation.lerp(mats[bone].translation,w))
            frames.append(mats)
        act=bpy.data.actions.new(name+'_corrected'); ad.action=act; last={}
        for f,mats in enumerate(frames):
            for bone in rig.order:
                parent=rig.parent[bone]
                ref=rig.rest[bone] if parent is None else mats[parent]@rig.rest[parent].inverted()@rig.rest[bone]
                loc,q,_=(ref.inverted()@mats[bone]).decompose()
                if bone in last and last[bone].dot(q)<0:q.negate()
                last[bone]=q.copy();pb=arm.pose.bones[bone]
                pb.location=loc;pb.rotation_quaternion=q;pb.scale=(1,1,1)
                pb.keyframe_insert('location',frame=f);pb.keyframe_insert('rotation_quaternion',frame=f)
        baked[name]=act
    ad.action=None
    for name,act in baked.items():
        track=next(t for t in ad.nla_tracks if t.name==name)
        for strip in list(track.strips):track.strips.remove(strip)
        strip=track.strips.new(name,0,act);strip.action_slot=act.slots[0]
        old=originals[name];old.name=name+'_previous';act.name=name;act.use_fake_user=True
        bpy.data.actions.remove(old)
        bpy.data.actions.remove(bodies[name])
    for t in ad.nla_tracks:t.mute=muted[t.name]
    for pb in arm.pose.bones:pb.location,pb.rotation_quaternion,pb.scale=original_pose[pb.name]
    bpy.ops.wm.save_as_mainfile(filepath=str(Path(out[0]).resolve()),compress=True)
    if len(out)>1:
        for o in scene.objects:o.select_set(False)
        for o in scene.objects:
            if o==arm or (o.parent==arm and o.type in {'MESH','EMPTY'}):o.select_set(True)
        bpy.context.view_layer.objects.active=arm
        bpy.ops.export_scene.fbx(filepath=str(Path(out[1]).resolve()),use_selection=True,object_types={'ARMATURE','MESH','EMPTY'},apply_scale_options='FBX_SCALE_ALL',axis_forward='-Z',axis_up='Y',add_leaf_bones=False,mesh_smooth_type='FACE',bake_anim=True,bake_anim_use_all_actions=False,bake_anim_use_nla_strips=True,bake_anim_force_startend_keying=True,bake_anim_simplify_factor=0.,path_mode='STRIP')
        if len(out)>2:replace_takes(Path(out[2]),Path(out[1]))
    print('GROUNDSTROKES_BUILT',list(baked))

if __name__=='__main__':main()







