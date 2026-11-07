#!/usr/bin/env python3
# would matching within a window around each landmark's predicted pixel in the newer view (from raw tracking
# and the recorded inverse depth) give more CORRECT matches than mutual NN over the whole image?
import struct,sys,numpy as np
exec(open('/Users/jt/source/lasertag/Logs/TwoPlayerTest/analysis/matchcheck.py').read().split('rng=np.random')[0])
def qinv_rot(q,v):
    x,y,z,w=q; return qrot((-x,-y,-z,w),v)
def project(p,pos,rot,fx,fy,cx,cy):
    c=qinv_rot(rot,p-pos)
    ok=c[:,2]>0.1
    u=cx+fx*c[:,0]/np.maximum(c[:,2],1e-3); v=cy-fy*c[:,1]/np.maximum(c[:,2],1e-3)
    return np.stack([u,v],1),ok
def windowed(va,vb,radius_px,ratio=0.97,mincos=0.75):
    fx,fy,cx,cy=va[2]
    ra=rays(va[4],*va[2],va[1])
    inv=va[7].astype(float); sig=va[8].astype(float)
    ranged=np.isfinite(sig)&(inv>0.005)&(sig<0.35*inv)
    A=np.where(ranged)[0]
    pts=va[0]+ra[A]/inv[A][:,None]
    # doubt along the ray: max(claimed, 0.05 D + 0.01 D^2), as a pixel radius seen from B
    D=1/inv[A]; doubt=np.maximum(sig[A]/inv[A]**2, 0.05*D+0.01*D*D)
    px,ok=project(pts,vb[0],vb[1],*vb[2])
    far,_=project(pts+ra[A]*doubt[:,None],vb[0],vb[1],*vb[2])
    rad=np.minimum(np.linalg.norm(far-px,axis=1),200)+radius_px
    da=va[5][A]/np.maximum(np.linalg.norm(va[5][A],axis=1,keepdims=True),1e-6); db=vb[5]/np.maximum(np.linalg.norm(vb[5],axis=1,keepdims=True),1e-6)
    S=da@db.T
    kb=vb[4]
    d2=((kb[None,:,0]-px[:,None,0])**2+(kb[None,:,1]-px[:,None,1])**2)
    inwin=(d2<=rad[:,None]**2)&ok[:,None]
    Sw=np.where(inwin,S,-2)
    bj=Sw.argmax(1); best=Sw[np.arange(len(A)),bj]
    S2=Sw.copy(); S2[np.arange(len(A)),bj]=-2; second=S2.max(1)
    bi=Sw.argmax(0)
    okm=(best>=mincos)&(second<ratio*best)&(bi[bj]==np.arange(len(A)))
    return A[okm],bj[okm]
rng=np.random.default_rng(1)
for R in sys.argv[1:]:
    V=list(views(ROOT+R+'/host.views'))
    fwd=[qrot(v[1],np.array([0,0,1.0])) for v in V]
    bins={(0,1):[],(1,2):[],(2,3):[],(3,4):[],(4,6):[]}
    tried=0
    for b in rng.permutation(len(V)):
        if tried>=300: break
        vb=V[b]
        cands=[a for a in range(len(V)) if V[a][3]<vb[3]-60e9 and np.linalg.norm((V[a][0]-vb[0])*[1,0,1])<6 and np.dot(fwd[a],fwd[b])>np.cos(np.radians(45))]
        if not cands: continue
        a=cands[rng.integers(len(cands))]; va=V[a]; tried+=1
        d=np.linalg.norm((va[0]-vb[0])*[1,0,1])
        ra=rays(va[4],*va[2],va[1]); rb=rays(vb[4],*vb[2],vb[1]); ta,tb=va[9],vb[9]
        def correct(ia,ib):
            known=np.isfinite(ta[ia])&np.isfinite(tb[ib])
            pa=va[0]+ra[ia]*ta[ia][:,None]; pb=vb[0]+rb[ib]*tb[ib][:,None]
            return (known&(np.linalg.norm(pa-pb,axis=1)<0.5+0.05*np.minimum(ta[ia],tb[ib]))).sum()
        inv=va[7]; sig=va[8]; ranged=np.isfinite(sig)&(inv>0.005)&(sig<0.35*inv)
        ia,ib=mutual(va[5][ranged],vb[5]); ia=np.where(ranged)[0][ia]
        row=[len(ia),correct(ia,ib)]
        for r in (10,20,40):
            ja,jb=windowed(va,vb,r); row+=[len(ja),correct(ja,jb)]
        for (lo,hi),L in bins.items():
            if lo<=d<hi: L.append(row)
    print(R,'ranged landmarks only; matches/correct: mutual NN whole image | window +10 px | +20 px | +40 px')
    for (lo,hi),L in bins.items():
        if not L: continue
        A=np.array(L,float); m=np.median(A,0); p=[100*np.mean(A[:,i]>=20) for i in (1,3,5,7)]
        print(f'  {lo}-{hi} m n={len(A):3d}: {m[0]:4.0f}/{m[1]:3.0f} ({p[0]:3.0f}% 20+) | {m[2]:4.0f}/{m[3]:3.0f} ({p[1]:3.0f}%) | {m[4]:4.0f}/{m[5]:3.0f} ({p[2]:3.0f}%) | {m[6]:4.0f}/{m[7]:3.0f} ({p[3]:3.0f}%)')
