#target illustrator
function C(r,g,b){var c=new RGBColor();c.red=r;c.green=g;c.blue=b;return c;}
var H=128, OFF=9;
var DG=C(58,66,74), LG=C(234,238,242), SB=C(72,136,178), EB=C(84,200,240), WH=C(255,255,255), K1=C(22,22,22);
var doc=app.documents.add(DocumentColorSpace.RGB,256,128);
function R(x,y,w,h,r,fill,sw,sc){var t=H-(y+OFF);var p=doc.pathItems.roundedRectangle(t,x,w,h,r,r);if(fill){p.filled=true;p.fillColor=fill;}else{p.filled=false;}if(sw>0){p.stroked=true;p.strokeColor=sc||K1;p.strokeWidth=sw;}else{p.stroked=false;}return p;}
R(112,32,104,11,3,EB,0);
R(112,54,19,15,3,EB,0);
R(138,54,19,15,3,EB,0);
R(164,54,19,15,3,EB,0);
R(190,54,19,15,3,EB,0);
var body=doc.pathItems.add(); body.setEntirePath([[0,86],[0,40],[6,39],[15,28],[8,20],[8,1],[61,1],[61,13],[54,20],[69,35],[61,43],[61,50],[65,54],[103,17],[144,17],[150,24],[213,24],[220,31],[228,24],[236,24],[240,28],[240,53],[255,55],[255,86],[244,86],[237,93],[112,93],[101,103],[100,109],[62,109],[61,103],[50,93],[10,93],[6,87]]); body.closed=true; body.filled=true; body.fillColor=DG; body.stroked=false;
var hs=[];
hs.push(R(112,32,104,11,3,null,0));
hs.push(R(112,54,19,15,3,null,0));
hs.push(R(138,54,19,15,3,null,0));
hs.push(R(164,54,19,15,3,null,0));
hs.push(R(190,54,19,15,3,null,0));

doc.selection=null; body.selected=true;
for(var i=0;i<hs.length;i++) hs[i].selected=true;
app.executeMenuCommand("group");
app.executeMenuCommand("Live Pathfinder Subtract");
app.executeMenuCommand("expandStyle");
app.executeMenuCommand("ungroup");
var shell=doc.selection[0]; shell.stroked=false;

R(72,40,54,22,3,LG,0);
R(34,60,20,44,4,SB,0);
R(152,68,18,34,3,SB,0);
var hl=doc.pathItems.add(); hl.setEntirePath([[6,82],[12,88],[18,88],[24,88],[30,88],[36,88],[42,88],[48,88],[54,93],[60,97],[66,104],[72,104],[78,104],[84,104],[90,104],[96,104],[102,97],[108,93],[114,88],[120,88],[126,88],[132,88],[138,88],[144,88],[150,88],[156,88],[162,88],[168,88],[174,88],[180,88],[186,88],[192,88],[198,88],[204,88],[210,88],[216,88],[222,88],[228,88],[234,88],[240,85],[246,81]]); hl.closed=false; hl.filled=false; hl.stroked=true; hl.strokeColor=WH; hl.strokeWidth=2.6; hl.strokeCap=StrokeCap.ROUNDENDCAP;
R(66,86,100,0.1,0,null,1.0);
R(70,56,90,0.1,0,null,1.0);
shell.stroked=true; shell.strokeColor=K1; shell.strokeWidth=3.0; shell.strokeJoin=StrokeJoin.ROUNDENDJOIN;
doc.artboards[0].artboardRect=[0,128,256,0];
var o=new ExportOptionsPNG24(); o.artBoardClipping=true; o.antiAliasing=true; o.transparency=true;
doc.exportFile(new File("W:/dsh/zengbing/Tools/laser_ai.png"),ExportType.PNG24,o);
var o2=new ExportOptionsPNG24(); o2.artBoardClipping=true; o2.antiAliasing=true; o2.transparency=true; o2.horizontalScale=200; o2.verticalScale=200;
doc.exportFile(new File("W:/dsh/zengbing/Tools/laser_ai_2x.png"),ExportType.PNG24,o2);
doc.close(SaveOptions.DONOTSAVECHANGES);