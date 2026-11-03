import numpy as np,math,random,sys
from PIL import Image,ImageDraw
W,H=2048,614; random.seed(53)
# texture covers 5 m of row, 1.5 m high. Top to bottom: leaves, then bare trunks and wire.
im=Image.new('RGBA',(W,H),(0,0,0,0)); d=ImageDraw.Draw(im)
S=[(x,0) for x in (0,)]
for m in range(0,5):
    x=int((m+.5)*W/5)+random.randint(-20,20)
    d.line([(x,H),(x+random.randint(-12,12),int(H*.55))],fill=(58,44,32,255),width=13)   # trunk
    if m%2==0: d.rectangle([x+W//10-4,int(H*.08),x+W//10+4,H],fill=(120,112,100,255))     # stake
for y in (.72,.52,.28): d.line([(0,int(H*y)),(W,int(H*y))],fill=(90,90,88,255),width=2)
base=[(58,92,30),(74,110,36),(92,126,44),(48,80,30),(108,134,52),(66,104,40)]
def leaf(x,y,r,c,a):
    pts=[]
    for i in range(10):
        t=a+i/10*2*math.pi; q=r*(1 if i%2==0 else .62)*random.uniform(.85,1.1)
        pts.append((x+q*math.cos(t),y+q*.85*math.sin(t)))
    for o in (-W,0,W): d.polygon([(p[0]+o,p[1]) for p in pts],fill=c+(255,))
for k in range(5200):
    x=random.uniform(0,W)
    top=H*(.05+.08*(math.sin(x/W*2*math.pi*7)+1)*.5+.05*random.random())
    y=top+abs(random.gauss(0,H*.26))
    if y>H*.70: continue
    if random.random()<.32: continue   # gaps to see through
    c=random.choice(base); sh=random.uniform(.72,1.12)*(1-.28*(y/(H*.7)))
    leaf(x,y,random.uniform(11,21),tuple(min(255,int(v*sh)) for v in c),random.uniform(0,6.3))
im.save(sys.argv[1])
a=np.array(im)[:,:,3]/255; print('coverage',a.mean())
