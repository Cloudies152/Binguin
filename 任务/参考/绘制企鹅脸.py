# -*- coding: utf-8 -*-
from PIL import Image, ImageDraw
import numpy as np
from collections import deque
import cv2

src = r"W:\dsh\zengbing\Tools\ai_in\kick10_8_8a13ced2.png"
im = Image.open(src).convert("RGBA"); a=np.array(im); rgb=a[...,:3].astype(np.int16); h,w=rgb.shape[:2]
tol=24; nw=(rgb.min(axis=2)>255-tol*2)&((rgb.max(axis=2)-rgb.min(axis=2))<tol)
vis=np.zeros((h,w),bool); q=deque()
for x in range(w):
    for y in (0,h-1):
        if nw[y,x] and not vis[y,x]: vis[y,x]=True; q.append((y,x))
for y in range(h):
    for x in (0,w-1):
        if nw[y,x] and not vis[y,x]: vis[y,x]=True; q.append((y,x))
while q:
    y,x=q.popleft()
    for dy,dx in ((1,0),(-1,0),(0,1),(0,-1)):
        ny,nx=y+dy,x+dx
        if 0<=ny<h and 0<=nx<w and not vis[ny,nx] and nw[ny,nx]:
            vis[ny,nx]=True; q.append((ny,nx))
alpha=np.where(vis,0,255).astype(np.uint8)
n,labels,stats,_=cv2.connectedComponentsWithStats((alpha>0).astype(np.uint8),8)
keep=(labels==1+int(np.argmax(stats[1:,cv2.CC_STAT_AREA])))
out=np.dstack([a[...,:3], np.where(keep,255,0).astype(np.uint8)])
ys,xs=np.where(keep); x0,x1,y0,y1=xs.min(),xs.max(),ys.min(),ys.max()
img=Image.fromarray(out,"RGBA")
cx=(x0+x1)//2
col=np.where(keep[:,cx])[0]; top,bot=col.min(),col.max(); hh=bot-top+1
r=int(hh*0.97)//2; cy=(top+bot)//2
lw=max(8,int(hh*0.080))     # 更明显的黑描边
d=ImageDraw.Draw(img)
d.ellipse([cx-r,cy-r,cx+r,cy+r], fill=(251,251,252,255), outline=(20,20,20,255), width=lw)
er=max(4,int(hh*0.072))
for s in (-1,1):
    ex=cx+s*int(hh*0.150); ey=cy-int(hh*0.045)
    d.ellipse([ex-er,ey-er,ex+er,ey+er], fill=(22,24,28,255))
    gr=max(2,er//3); gx,gy=ex-er//3, ey-er//3
    d.ellipse([gx-gr,gy-gr,gx+gr,gy+gr], fill=(255,255,255,255))
bw=max(6,int(hh*0.105)); bh=max(5,int(hh*0.070))
bx,by=cx, cy+int(hh*0.115)
d.polygon([(bx-bw,by-bh),(bx+bw,by-bh),(bx,by+bh)], fill=(245,152,44,255))
crop=img.crop((x0,y0,x1+1,y1+1))
cw,ch=crop.size; sc=min((256-6)/cw,(128-6)/ch)
crop=crop.resize((max(1,int(cw*sc)),max(1,int(ch*sc))), Image.LANCZOS)
canvas=Image.new("RGBA",(256,128),(0,0,0,0))
canvas.alpha_composite(crop, ((256-crop.width)//2,(128-crop.height)//2))
canvas.save(r"W:\dsh\zengbing\Tools\penguin_kick_256.png")
canvas.resize((512,256), Image.LANCZOS).save(r"W:\dsh\zengbing\Tools\penguin_kick_256_2x.png")
print("ok")

