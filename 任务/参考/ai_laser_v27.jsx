#target illustrator
function C(r,g,b){var c=new RGBColor();c.red=r;c.green=g;c.blue=b;return c;}
var H=128, OFF=9;
var DG=C(58,66,74), LG=C(238,242,246), SB=C(72,136,178), EB=C(84,200,240), WH=C(255,255,255), K1=C(20,20,20);
var doc=app.documents.add(DocumentColorSpace.RGB,256,128);
function R(x,y,w,h,r,fill,sw){var t=H-(y+OFF);var p=doc.pathItems.roundedRectangle(t,x,w,h,r,r);if(fill){p.filled=true;p.fillColor=fill;}else{p.filled=false;}if(sw>0){p.stroked=true;p.strokeColor=K1;p.strokeWidth=sw;}else{p.stroked=false;}return p;}
function P(pts,fill,sw){var a=[];for(var i=0;i<pts.length;i++)a.push([pts[i][0],H-(pts[i][1]+OFF)]);var p=doc.pathItems.add();p.setEntirePath(a);p.closed=true;if(fill){p.filled=true;p.fillColor=fill;}else{p.filled=false;}if(sw>0){p.stroked=true;p.strokeColor=K1;p.strokeWidth=sw;p.strokeJoin=StrokeJoin.ROUNDENDJOIN;}else{p.stroked=false;}return p;}
var s1=R(107,32,86,9,3,EB,0);
var s2=R(107,50,86,8,3,EB,0);
var body=P([[0,86],[0,40],[6,39],[15,28],[8,20],[8,1],[61,1],[61,13],[54,20],[69,35],[61,43],[61,50],[65,54],[103,17],[144,17],[150,24],[213,24],[220,31],[228,24],[236,24],[240,28],[240,53],[255,55],[255,86],[244,86],[237,93],[112,93],[101,103],[100,109],[62,109],[61,103],[50,93],[10,93],[6,87]],DG,0);
var hs=[R(107,32,86,9,3,null,0),R(107,50,86,8,3,null,0)];

doc.selection=null; body.selected=true;
for(var i=0;i<hs.length;i++) hs[i].selected=true;
app.executeMenuCommand("group");
app.executeMenuCommand("Live Pathfinder Subtract");
app.executeMenuCommand("expandStyle");
app.executeMenuCommand("ungroup");
var b2=doc.selection[0]; b2.stroked=false;

R(45,4,158,5,2,LG,0);
R(6,42,26,27,3,SB,0);
R(51,6,76,2.5,1,WH,0);
R(107,26,86,2.5,1,WH,0);
b2.stroked=true; b2.strokeColor=K1; b2.strokeWidth=2.4; b2.strokeJoin=StrokeJoin.ROUNDENDJOIN;
doc.artboards[0].artboardRect=[0,128,256,0];
var o=new ExportOptionsPNG24(); o.artBoardClipping=true; o.antiAliasing=true; o.transparency=true;
doc.exportFile(new File("W:/dsh/zengbing/Tools/laser_ai.png"),ExportType.PNG24,o);
var o2=new ExportOptionsPNG24(); o2.artBoardClipping=true; o2.antiAliasing=true; o2.transparency=true; o2.horizontalScale=200; o2.verticalScale=200;
doc.exportFile(new File("W:/dsh/zengbing/Tools/laser_ai_2x.png"),ExportType.PNG24,o2);
doc.close(SaveOptions.DONOTSAVECHANGES);