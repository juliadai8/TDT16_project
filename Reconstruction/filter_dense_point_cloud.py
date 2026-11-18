"""Isolate the bottle in a dense OpenMVS PLY while preserving per-point view data.

Separate from filter_point_cloud.py, which operates on sparse COLMAP features.
The unmodified input is retained. The table plane is estimated from surrounding
points, never from the old mesh's missing-bottom cutoff.
"""
import argparse
import hashlib
import json
from pathlib import Path
import struct
import re
import numpy as np
from scipy.spatial import cKDTree


def read_records(path):
    raw=Path(path).read_bytes()
    end=raw.index(b'end_header')+len(b'end_header')
    while raw[end] in (10,13):end+=1
    header=raw[:end].decode('ascii')
    if 'format binary_little_endian' not in header:raise ValueError('Expected binary little-endian PLY')
    count=int(next(s.split()[2] for s in header.splitlines() if s.startswith('element vertex ')))
    expected=['x','y','z','red','green','blue','nx','ny','nz','view_indices','view_weights']
    if [s.split()[-1] for s in header.splitlines() if s.startswith('property ')]!=expected:
        raise ValueError('Unexpected OpenMVS vertex layout')
    points=np.empty((count,3),float); normals=np.empty_like(points)
    starts=np.empty(count+1,np.int64);offset=end
    for i in range(count):
        starts[i]=offset
        points[i]=struct.unpack_from('<fff',raw,offset)
        normals[i]=struct.unpack_from('<fff',raw,offset+15)
        offset+=27
        for _ in range(2):
            n=raw[offset];offset+=1+4*n
    starts[count]=offset
    if offset!=len(raw):raise ValueError('Unexpected trailing or truncated PLY data')
    return raw,header,starts,points,normals


def main(args):
    raw,header,starts,points,normals=read_records(args.input)
    alignment=json.loads((args.project/'Reconstruction/bottle/unity_transform.json').read_text())
    scene=(args.project/'Assets/PhotoObjectLab/BottleVisualization.unity').read_text()
    root_scene=scene[scene.index('  m_Name: BottleRoot'):]
    def vector(field,keys):
        values=re.search(field+r': \{([^}]+)\}',root_scene).group(1)
        parsed=dict((key,float(value)) for key,value in re.findall(r'(\w+): ([^,}]+)',values))
        return np.array([parsed[key] for key in keys])
    q=vector('m_LocalRotation',['x','y','z','w']);q/=np.linalg.norm(q)
    translation=vector('m_LocalPosition',['x','y','z'])
    def rotate(v):return v+2*np.cross(q[:3],np.cross(q[:3],v)+q[3]*v)
    local=((points-alignment['center'])*alignment['scale'])[:,alignment['axis_order']]
    world=rotate(local)+translation
    wn=rotate(normals[:,alignment['axis_order']])
    radius=np.linalg.norm(world[:,[0,2]],axis=1)
    candidates=(radius>.36)&(radius<.7)&(world[:,1]>-.65)&(world[:,1]<-.08)&(np.abs(wn[:,1])>.7)
    table=world[candidates]
    if len(table)<100:raise ValueError('Insufficient surrounding table samples')
    rng=np.random.default_rng(42)
    sample=table[rng.choice(len(table),min(5000,len(table)),replace=False)]
    best=None;score=0
    for _ in range(300):
        a,b,c=sample[rng.choice(len(sample),3,replace=False)]
        n=np.cross(b-a,c-a);length=np.linalg.norm(n)
        if length<1e-8:continue
        n/=length
        if abs(n[1])<.95:continue
        dist=np.abs((sample-a)@n)
        selected=dist<.008
        if selected.sum()>score:score=int(selected.sum());best=(a,n)
    if best is None:raise ValueError('Could not fit an approximately horizontal table')
    inliers=table[np.abs((table-best[0])@best[1])<.008]
    center=inliers.mean(axis=0)
    n=np.linalg.svd(inliers-center,full_matrices=False)[2][-1]
    if n[1]<0:n=-n
    height=(world-center)@n
    # Wide bottle envelope extends below the recovered base. The fitted table
    # removes the supporting surface. Near-contact points with outward side
    # normals survive, avoiding a new arbitrary horizontal slice of the bottle.
    envelope=(radius<.335)&(world[:,1]<1.8)&(world[:,1]>-.6)
    above=(height>.01)|((height>-.08)&(np.abs(wn[:,1])<.65)&(radius>.12))
    mask=envelope&above
    # Learn the bottle's lateral silhouette from a stable cylindrical body band,
    # then extend it down to the base. This removes table skirts without using
    # a height cutoff from the incomplete original mesh.
    angles=np.mod(np.arctan2(world[:,2],world[:,0]),2*np.pi)
    bins=36;which=np.minimum((angles/(2*np.pi)*bins).astype(int),bins-1)
    body=(world[:,1]>.08)&(world[:,1]<.4)&(radius<.335)&(np.abs(wn[:,1])<.5)
    limits=np.empty(bins)
    for k in range(bins):
        neighbors=body&(((which-k)%bins<=1)|((k-which)%bins<=1))
        if neighbors.sum()<20:raise ValueError('Insufficient body support for lateral filtering')
        limits[k]=np.percentile(radius[neighbors],98)+.006
    mask&=(world[:,1]>=.12)|(radius<=limits[which])
    ids=np.flatnonzero(mask)
    neighbors=cKDTree(world[ids]).query(world[ids],k=9,workers=2)[0][:,1:].mean(axis=1)
    median=float(np.median(neighbors));mad=float(np.median(np.abs(neighbors-median)))
    threshold=max(median+6*1.4826*mad,.012)
    mask[ids[neighbors>threshold]]=False
    kept=np.flatnonzero(mask)
    if len(kept)<1000:raise ValueError('Unexpectedly small bottle selection')
    output=args.output;output.parent.mkdir(parents=True,exist_ok=True)
    newheader=header.replace('element vertex '+str(len(points)),'element vertex '+str(len(kept)),1)
    with output.open('wb') as stream:
        stream.write(newheader.encode('ascii'))
        for i in kept:stream.write(raw[starts[i]:starts[i+1]])
    report=dict(input=str(args.input.resolve()),input_sha256=hashlib.sha256(raw).hexdigest(),
        input_points=len(points),retained_points=len(kept),removed_points=int((~mask).sum()),
        table_candidates=len(table),table_inliers=len(inliers),table_point_world=center.tolist(),
        table_normal_world=n.tolist(),bottle_radius_limit_world=.335,
        table_clearance_world=.01,side_normal_contact_tolerance_world=-.08,
        lower_body_radial_limits=limits.tolist(),lower_body_envelope_learned_height_band=[.08,.4],
        statistical_neighbor_count=8,statistical_mean_distance_threshold_world=threshold,
        retained_world_height_range=[float(world[mask,1].min()),float(world[mask,1].max())],
        retained_points_below_old_mesh=int((world[mask,1]<-.01).sum()),
        per_point_camera_views_and_weights_preserved=True,
        note='No hole filling or synthetic geometry. Selection is above a fitted table plane with a side-normal exception at contact. Full raw source retained.')
    output.with_suffix('.filter.json').write_text(json.dumps(report,indent=2)+'\n')
    print(json.dumps(report,indent=2))


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--project',type=Path,required=True)
    parser.add_argument('--input',type=Path,required=True)
    parser.add_argument('--output',type=Path,required=True)
    main(parser.parse_args())
