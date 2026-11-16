#target illustrator
function C(r,g,b){ var c=new RGBColor(); c.red=r; c.green=g; c.blue=b; return c; }
var H=128, OFF=10;
var DG=C(58,66,74), LG=C(232,236,240), SB=C(72,136,178), EB=C(84,200,240), SH=C(40,46,52), WH=C(255,255,255), K1=C(20,20,20);
var doc=app.documents.add(DocumentColorSpace.RGB,256,128);
function R(x,y,w,h,r,fill,sw,sc){
  var t=H-(y+OFF);
  var p=doc.pathItems.roundedRectangle(t,x,w,h,r,r);
  if(fill){p.filled=true;p.fillColor=fill;}else{p.filled=false;}
  if(sw>0){p.stroked=true;p.strokeWidth=sw;p.strokeColor=sc||K1;p.strokeJoin=StrokeJoin.ROUNDENDJOIN;}else{p.stroked=false;}
  return p;}
function PT(pts,fill,sw){ var a=[]; for(var i=0;i<pts.length;i++)a.push([pts[i][0],H-(pts[i][1]+OFF)]);
  var p=doc.pathItems.add(); p.setEntirePath(a); p.closed=true;
  if(fill){p.filled=true;p.fillColor=fill;}else{p.filled=false;}
  if(sw>0){p.stroked=true;p.strokeColor=K1;p.strokeWidth=sw;}else{p.stroked=false;} return p; }
R(40,72,84,5,2,SH,0);
R(124,56,112,5,2,SH,0);
R(14,40,12,22,1,EB,0);
R(60,8,24,8,1,EB,0);
R(52,22,60,8,1,EB,0);
R(52,50,60,7,1,EB,0);
R(76,80,14,14,1,EB,0);
R(108,80,14,14,1,EB,0);
R(136,46,10,10,1,EB,0);
R(152,46,10,10,1,EB,0);
R(168,46,10,10,1,EB,0);
R(184,46,10,10,1,EB,0);
R(200,46,10,10,1,EB,0);
R(216,46,10,10,1,EB,0);
var parts=[];
parts.push(R(6,72,44,36,2,DG,0));
parts.push(PT([[10,32],[46,38],[42,76],[8,72]],DG,0));
parts.push(R(40,16,84,60,4,DG,0));
parts.push(R(56,4,32,16,2,DG,0));
parts.push(R(72,76,22,24,2,DG,0));
parts.push(R(104,76,22,24,2,DG,0));
parts.push(R(124,16,112,44,4,DG,0));
parts.push(R(236,18,16,40,2,DG,0));
var hh=[];
hh.push(R(14,40,12,22,1,null,0));
hh.push(R(60,8,24,8,1,null,0));
hh.push(R(52,22,60,8,1,null,0));
hh.push(R(52,50,60,7,1,null,0));
hh.push(R(76,80,14,14,1,null,0));
hh.push(R(108,80,14,14,1,null,0));
hh.push(R(136,46,10,10,1,null,0));
hh.push(R(152,46,10,10,1,null,0));
hh.push(R(168,46,10,10,1,null,0));
hh.push(R(184,46,10,10,1,null,0));
hh.push(R(200,46,10,10,1,null,0));
hh.push(R(216,46,10,10,1,null,0));

doc.selection=null;
for (var i=0;i<parts.length;i++) parts[i].selected=true;
app.executeMenuCommand("group");
app.executeMenuCommand("Live Pathfinder Add");
app.executeMenuCommand("expandStyle");
app.executeMenuCommand("ungroup");
var shell=doc.selection[0];
doc.selection=null;
shell.selected=true;
for (var j=0;j<hh.length;j++) hh[j].selected=true;
app.executeMenuCommand("group");
app.executeMenuCommand("Live Pathfinder Subtract");
app.executeMenuCommand("expandStyle");
app.executeMenuCommand("ungroup");
var body=doc.selection[0];
body.stroked=true; body.strokeColor=K1; body.strokeWidth=3.0; body.strokeJoin=StrokeJoin.ROUNDENDJOIN;

R(40,44,84,0.1,0,null,1.5);
R(124,38,112,0.1,0,null,1.5);
R(51,16,0.1,60,0,null,1.5);
R(124,16,0.1,44,0,null,1.5);
R(47,52,5,5,1,DG,1.5);
R(48,53,2.5,2.5,0,SH,0);
R(60,52,5,5,1,DG,1.5);
R(61,53,2.5,2.5,0,SH,0);
R(100,52,5,5,1,DG,1.5);
R(101,53,2.5,2.5,0,SH,0);
R(112,52,5,5,1,DG,1.5);
R(113,53,2.5,2.5,0,SH,0);
R(58,10,6,5,1,DG,1.2);
R(68,10,6,5,1,DG,1.2);
R(78,10,6,5,1,DG,1.2);
R(88,10,6,5,1,DG,1.2);
R(98,10,6,5,1,DG,1.2);
R(108,10,6,5,1,DG,1.2);
R(118,10,6,5,1,DG,1.2);
R(136,26,10,3,1,SH,0);
R(154,26,10,3,1,SH,0);
R(172,26,10,3,1,SH,0);
R(190,26,10,3,1,SH,0);
R(208,26,10,3,1,SH,0);
R(92,34,20,8,1,SH,1.5);
PT([[118,76],[118,92],[128,98],[140,92],[140,76]],null,2.0);
R(122,76,3,10,1,DG,1.2);
R(232,26,10,24,2,DG,2.0);
R(234,32,6,4,1,EB,0);
R(234,40,6,4,1,EB,0);
R(52,20,60,2.5,1,WH,0);
R(136,20,84,2.5,1,WH,0);
R(12,36,26,2.5,1,WH,0);
doc.artboards[0].artboardRect=[0,128,256,0];
var o=new ExportOptionsPNG24(); o.artBoardClipping=true; o.antiAliasing=true; o.transparency=true;
doc.exportFile(new File("W:/dsh/zengbing/Tools/laser_ai.png"),ExportType.PNG24,o);
var o2=new ExportOptionsPNG24(); o2.artBoardClipping=true; o2.antiAliasing=true; o2.transparency=true; o2.horizontalScale=200; o2.verticalScale=200;
doc.exportFile(new File("W:/dsh/zengbing/Tools/laser_ai_2x.png"),ExportType.PNG24,o2);
doc.close(SaveOptions.DONOTSAVECHANGES);