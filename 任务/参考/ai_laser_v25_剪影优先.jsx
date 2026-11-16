#target illustrator
function C(r,g,b){var c=new RGBColor();c.red=r;c.green=g;c.blue=b;return c;}
var H=128, OFF=9;
var DG=C(58,66,74), SB=C(72,136,178), EB=C(84,200,240), SH=C(40,46,52), WH=C(255,255,255), K1=C(20,20,20);
var doc=app.documents.add(DocumentColorSpace.RGB,256,128);
function R(x,y,w,h,r,fill,sw){var t=H-(y+OFF);var p=doc.pathItems.roundedRectangle(t,x,w,h,r,r);if(fill){p.filled=true;p.fillColor=fill;}else{p.filled=false;}if(sw>0){p.stroked=true;p.strokeColor=K1;p.strokeWidth=sw;}else{p.stroked=false;}return p;}
R(128,74,1,9,2,EB,0);
R(112,74,2,9,2,EB,0);
R(205,66,2,9,2,EB,0);
R(190,66,1,9,2,EB,0);
R(174,66,2,9,2,EB,0);
R(159,66,1,9,2,EB,0);
R(50,66,10,9,2,EB,0);
R(221,35,9,9,2,EB,0);
R(120,27,87,2,2,EB,0);
R(3,19,49,49,2,EB,0);
R(89,11,2,10,2,EB,0);
R(73,11,2,10,2,EB,0);
var sil=doc.pathItems.add(); sil.setEntirePath([[0,86],[0,40],[6,40],[8,38],[8,33],[15,31],[15,25],[13,23],[8,23],[8,1],[61,1],[61,15],[55,16],[53,18],[53,22],[61,25],[61,30],[63,32],[69,33],[69,38],[61,41],[61,53],[63,55],[68,55],[70,53],[70,48],[75,48],[78,40],[83,40],[85,38],[85,34],[87,32],[91,32],[93,30],[93,25],[99,24],[101,22],[101,17],[103,16],[144,16],[146,17],[146,22],[148,24],[215,24],[218,32],[223,32],[225,30],[225,25],[238,24],[240,26],[239,53],[241,55],[255,55],[255,86],[241,86],[240,91],[237,94],[110,93],[108,95],[108,100],[100,103],[100,109],[62,109],[62,103],[60,101],[55,101],[52,93],[10,94],[8,93],[8,88],[6,86]]); sil.closed=true; sil.filled=true; sil.fillColor=DG; sil.stroked=false;
var hs=[];
hs.push(R(126,72,5,13,2,null,0));
hs.push(R(110,72,6,13,2,null,0));
hs.push(R(203,64,6,13,2,null,0));
hs.push(R(188,64,5,13,2,null,0));
hs.push(R(172,64,6,13,2,null,0));
hs.push(R(157,64,5,13,2,null,0));
hs.push(R(48,64,14,13,2,null,0));
hs.push(R(219,33,13,13,2,null,0));
hs.push(R(118,25,91,6,2,null,0));
hs.push(R(1,17,53,53,2,null,0));
hs.push(R(87,9,6,14,2,null,0));
hs.push(R(71,9,6,14,2,null,0));

doc.selection=null; sil.selected=true;
for(var i=0;i<hs.length;i++) hs[i].selected=true;
app.executeMenuCommand("group");
app.executeMenuCommand("Live Pathfinder Subtract");
app.executeMenuCommand("expandStyle");
app.executeMenuCommand("ungroup");
var body=doc.selection[0]; body.stroked=false;

var bx=581, by=0;
R(200,46,40,14,2,DG,0);
R(60,44,110,0.1,0,null,1.5);
R(150,34,60,0.1,0,null,1.5);
R(56,22,70,2.5,1,WH,0);
R(150,24,60,2.5,1,WH,0);
body.stroked=true; body.strokeColor=K1; body.strokeWidth=2.2; body.strokeJoin=StrokeJoin.ROUNDENDJOIN;
doc.artboards[0].artboardRect=[0,128,256,0];
var o=new ExportOptionsPNG24(); o.artBoardClipping=true; o.antiAliasing=true; o.transparency=true;
doc.exportFile(new File("W:/dsh/zengbing/Tools/laser_ai.png"),ExportType.PNG24,o);
var o2=new ExportOptionsPNG24(); o2.artBoardClipping=true; o2.antiAliasing=true; o2.transparency=true; o2.horizontalScale=200; o2.verticalScale=200;
doc.exportFile(new File("W:/dsh/zengbing/Tools/laser_ai_2x.png"),ExportType.PNG24,o2);
doc.close(SaveOptions.DONOTSAVECHANGES);