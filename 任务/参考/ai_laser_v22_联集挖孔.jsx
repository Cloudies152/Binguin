#target illustrator
function C(r,g,b){ var c=new RGBColor(); c.red=r; c.green=g; c.blue=b; return c; }
var H=128, OFF=10;
var DG=C(58,66,74), LG=C(232,236,240), SB=C(72,136,178), EB=C(84,200,240), SH=C(40,46,52), WH=C(255,255,255), K1=C(26,26,26);
var doc=app.documents.add(DocumentColorSpace.RGB,256,128);
function rrect(x,y,w,h,r,fill,strokeOnly){
  var t=H-(y+OFF);
  var p=doc.pathItems.roundedRectangle(t,x,w,h,r,r);
  if(fill){p.filled=true;p.fillColor=fill;}else{p.filled=false;}
  p.stroked=false;
  return p;}
function polyT(pts,fill){ var a=[]; for(var i=0;i<pts.length;i++)a.push([pts[i][0],H-(pts[i][1]+OFF)]);
  var p=doc.pathItems.add(); p.setEntirePath(a); p.closed=true;
  if(fill){p.filled=true;p.fillColor=fill;}else{p.filled=false;} p.stroked=false; return p; }
// 1 阴影层（底层）
rrect(40,74,84,4,2,SH,false);
rrect(124,58,112,4,2,SH,false);
rrect(6,104,44,4,2,SH,false);
// 2 蓝色发光（孔下方）
rrect(14,40,12,22,0,EB,false);
rrect(60,8,24,8,0,EB,false);
rrect(52,22,60,8,0,EB,false);
rrect(52,50,60,7,0,EB,false);
rrect(76,80,14,14,0,EB,false);
rrect(108,80,14,14,0,EB,false);
rrect(136,46,10,10,0,EB,false);
rrect(152,46,10,10,0,EB,false);
rrect(168,46,10,10,0,EB,false);
rrect(184,46,10,10,0,EB,false);
rrect(200,46,10,10,0,EB,false);
rrect(216,46,10,10,0,EB,false);
// 3 主外壳（联集 + 挖孔）
var parts=[];
parts.push(rrect(6,72,44,36,2,DG,false));
parts.push(polyT([[10,32],[46,38],[42,76],[8,72]],DG));
parts.push(rrect(40,16,84,60,4,DG,false));
parts.push(rrect(56,4,32,16,2,DG,false));
parts.push(rrect(72,76,22,24,2,DG,false));
parts.push(rrect(104,76,22,24,2,DG,false));
parts.push(rrect(124,16,112,44,4,DG,false));
parts.push(rrect(236,18,16,40,2,DG,false));
var hh=[];
hh.push(rrect(14,40,12,22,1,null,false));
hh.push(rrect(60,8,24,8,1,null,false));
hh.push(rrect(52,22,60,8,1,null,false));
hh.push(rrect(52,50,60,7,1,null,false));
hh.push(rrect(76,80,14,14,1,null,false));
hh.push(rrect(108,80,14,14,1,null,false));
hh.push(rrect(136,46,10,10,1,null,false));
hh.push(rrect(152,46,10,10,1,null,false));
hh.push(rrect(168,46,10,10,1,null,false));
hh.push(rrect(184,46,10,10,1,null,false));
hh.push(rrect(200,46,10,10,1,null,false));
hh.push(rrect(216,46,10,10,1,null,false));

doc.selection=null;
for (var i=0;i<parts.length;i++) parts[i].selected=true;
app.executeMenuCommand("group");
app.executeMenuCommand("Live Pathfinder Add");
app.executeMenuCommand("expandStyle");
app.executeMenuCommand("ungroup");
var shellPath=doc.selection[0];
doc.selection=null;
shellPath.selected=true;
for (var j=0;j<hh.length;j++) hh[j].selected=true;
app.executeMenuCommand("group");
app.executeMenuCommand("Live Pathfinder Subtract");
app.executeMenuCommand("expandStyle");
app.executeMenuCommand("ungroup");
var outer=doc.selection[0];
outer.stroked=true; outer.strokeColor=K1; outer.strokeWidth=2.0; outer.strokeJoin=StrokeJoin.ROUNDENDJOIN;

// 4 内部分割线（1.5pt）
var s1=rrect(40,44,84,1.5,0,null,false); s1.stroked=true; s1.strokeColor=K1; s1.strokeWidth=1.5;
var s2=rrect(124,38,112,1.5,0,null,false); s2.stroked=true; s2.strokeColor=K1; s2.strokeWidth=1.5;
// 5 高光（≤4 条，只在大面）
rrect(52,20,64,2.5,1,WH,false);
rrect(136,20,88,2.5,1,WH,false);
rrect(10,36,28,2.5,1,WH,false);
rrect(236,22,10,2.5,1,WH,false);
// 6 顶部描边（只描主外壳）
outer.duplicate(); var t=doc.selection[0]; t.filled=false; t.stroked=true; t.strokeColor=K1; t.strokeWidth=3.0;
doc.artboards[0].artboardRect=[0,128,256,0];
var o=new ExportOptionsPNG24(); o.artBoardClipping=true; o.antiAliasing=true; o.transparency=true;
doc.exportFile(new File("W:/dsh/zengbing/Tools/laser_ai.png"),ExportType.PNG24,o);
var o2=new ExportOptionsPNG24(); o2.artBoardClipping=true; o2.antiAliasing=true; o2.transparency=true; o2.horizontalScale=200; o2.verticalScale=200;
doc.exportFile(new File("W:/dsh/zengbing/Tools/laser_ai_2x.png"),ExportType.PNG24,o2);
doc.close(SaveOptions.DONOTSAVECHANGES);