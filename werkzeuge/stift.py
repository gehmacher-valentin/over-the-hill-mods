import numpy as np
from PIL import Image, ImageDraw, ImageFont
G=256
STIFTE=[(0.80,0.13,0.10),(0.13,0.38,0.85),(0.10,0.58,0.22)]
def papier(rng):
    n=rng.random((G,G)); w=rng.random((G//2+1,G//2+1)).repeat(2,0).repeat(2,1)[:G,:G]
    return 0.55*n+0.45*w
def strich(al,pap,rng,pts,br,dr,korn,deck):
    # pts Nx2, br/dr Nx; korn 0..1: wie stark das Papier durchscheint
    for (x,y),b,d in zip(pts,br,dr):
        if d<=0: continue
        x0,x1=max(0,int(x-b-1)),min(G-1,int(x+b+2)); y0,y1=max(0,int(y-b-1)),min(G-1,int(y+b+2))
        if x1<x0 or y1<y0: continue
        yy,xx=np.mgrid[y0:y1+1,x0:x1+1]
        k=np.clip((b-np.hypot(xx-x,yy-y))/1.2,0,1)
        p=pap[y0:y1+1,x0:x1+1]
        g=np.clip((d-p*korn)*4,0,1) if korn>0 else 1
        a=k*g*deck*d; s=al[y0:y1+1,x0:x1+1]; s+=a*(1-s)
def rund(rng,R,u,start,wob,spir,ell=0,kipp=0,mx=0,my=0):
    th=start+u
    r=np.full_like(u,R)
    for k in (2,3,5): r*=1+wob/k*1.4*np.sin(k*th*1.0+rng.random()*6.3)
    t=(u-u[0])/(u[-1]-u[0]); r*=1+spir*(t-.5)
    # langsames Zittern
    z=np.interp(t,np.linspace(0,1,24),rng.normal(0,wob*R*.18,24))
    r+=z
    x=np.cos(th)*r*(1+ell); y=np.sin(th)*r*(1-ell)
    c,s=np.cos(kipp),np.sin(kipp)
    return np.stack([G/2+mx+x*c-y*s, G/2+my+x*s+y*c],1),t
def druck(rng,t,lo,hi,ende=9):
    d=np.interp(t,np.linspace(0,1,14),rng.uniform(lo,hi,14))
    return d*np.clip(np.minimum(t,1-t)*ende+.25,0,1)
def kreis(v,f):
    rng=np.random.default_rng(11+f*7); pap=papier(rng); al=np.zeros((G,G)); R=G*.38; TP=2*np.pi
    def bahn(um,n=2200): return np.linspace(0,TP*um,n)
    if v==0: # bisher
        p,t=rund(rng,R,bahn(1.12),-.9,.03,.05); d=np.clip(np.minimum(t,1-t)*9,0,1)
        strich(al,pap,rng,p,5+5*d,.6+.4*d,.0,.35)
    if v==1: # ein Zug, raue Koernung, offen
        p,t=rund(rng,R,bahn(1.10),-.9,.05,.13,.03,.4); d=druck(rng,t,.6,1)
        strich(al,pap,rng,p,3.5+4*d,d,.85,.35)
    if v==2: # zwei schnelle Runden
        p,t=rund(rng,R*.97,bahn(2.15,4200),-1.2,.05,.13,.05,.5); d=druck(rng,t,.6,1,14)
        strich(al,pap,rng,p,2.5+2.2*d,d,.75,.4)
    if v==3: # mehrere duenne Skizzenrunden
        for i in range(4):
            p,t=rund(rng,R*rng.uniform(.93,1.04),bahn(rng.uniform(.95,1.15)),rng.random()*6.3,.045,rng.uniform(-.1,.1),rng.uniform(-.05,.05),rng.random()*3,*rng.uniform(-4,4,2))
            d=druck(rng,t,.5,.95); strich(al,pap,rng,p,1.8+1.6*d,d,.75,.4)
    if v==4: # breite stumpfe Mine, kreidig
        p,t=rund(rng,R*.98,bahn(1.14),-.7,.045,.12,.03,1.); d=druck(rng,t,.55,.85)
        strich(al,pap,rng,p,9+4*d,d,1.05,.25)
    if v==5: # hingeschmiert: eiernd, Enden schiessen ueber
        p,t=rund(rng,R*.95,bahn(1.06),-1.0,.09,.2,.07,.6); d=druck(rng,t,.6,1,12)
        strich(al,pap,rng,p,3.5+3.5*d,d,.8,.4)
        e=p[-1]+np.linspace(0,1,150)[:,None]*((p[-1]-p[-30])/np.linalg.norm(p[-1]-p[-30])*30)
        strich(al,pap,rng,e,np.linspace(5,1.5,150),np.linspace(.8,.4,150),.8,.4)
    if v==6: # zwei Runden + kurze Nachbesserungsstriche
        p,t=rund(rng,R*.96,bahn(1.75,3400),-1.4,.06,.12,.05,.3); d=druck(rng,t,.55,1,12)
        strich(al,pap,rng,p,2.8+2.4*d,d,.8,.4)
        for i in range(5):
            p,t=rund(rng,R*rng.uniform(.93,1.06),bahn(rng.uniform(.1,.25),500),rng.random()*6.3,.02,rng.uniform(-.12,.12))
            d=druck(rng,t,.6,1,5); strich(al,pap,rng,p,2.2+2*d,d,.8,.4)
    a=np.clip(al,0,1)*.95
    im=np.zeros((G,G,4)); im[...,:3]=STIFTE[f]; im[...,3]=a
    return Image.fromarray((im*255).astype('uint8'),'RGBA')
def symbol(d,x,y,r):
    d.ellipse([x-r,y-r,x+r,y+r],fill=(252,246,236)); s=r*.45
    d.polygon([(x-s*1.2,y),(x,y-s*1.1),(x+s*1.2,y)],fill=(30,30,30)); d.rectangle([x-s*.8,y,x+s*.8,y+s],fill=(30,30,30))
namen=["0  bisher","1  Ein Zug, rau","2  Zwei schnelle Runden","3  Skizze, viele dünne","4  Stumpfe Mine, kreidig","5  Hingeschmiert","6  Runden + Nachstriche"]
import glob
fs=[x for x in glob.glob('/usr/share/fonts/**/*.ttf',recursive=True) if 'Sans' in x and ('Regular' in x or x.endswith('DejaVuSans.ttf')) and 'Mono' not in x]
ft=ImageFont.truetype(sorted(fs)[0],20) if fs else ImageFont.load_default()
W=1180; H=len(namen)*230+50
b=Image.new('RGB',(W,H),(186,216,194)); d=ImageDraw.Draw(b)
rng=np.random.default_rng(1)
xs=np.arange(0,W,6)
for i in range(40):
    y0=i*H/38; d.line([(float(x),float(y0+18*np.sin(x/140+i*.5)+9*np.sin(x/53+i))) for x in xs],fill=(168,199,177),width=1)
d.text((270,12),"groß",font=ft,fill=0); d.text((930,12),"Spielgröße",font=ft,fill=0)
for v,n in enumerate(namen):
    y=50+v*230; d.text((14,y+95),n,font=ft,fill=(20,20,20))
    for f in range(3):
        k=kreis(v,f); g=k.resize((210,210),Image.LANCZOS); cx=370+f*205
        symbol(d,cx,y+110,42); b.paste(g,(cx-105,y+5),g)
        # Spielgroesse: Ring ~107 px, Symbol ~60 px
        s=k.resize((100,100),Image.LANCZOS) if False else k.resize((141,141),Image.LANCZOS)
        if f<2:
            cx=940+f*135; symbol(d,cx,y+110,30); b.paste(s,(cx-70,y+40),s)
b.save('varianten.png')
