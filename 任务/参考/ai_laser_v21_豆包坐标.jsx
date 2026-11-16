#target illustrator
function C(r,g,b){ var c=new RGBColor(); c.red=r; c.green=g; c.blue=b; return c; }
var H=128, OFF=10;
function rect(x,y,w,h,fill,lw){
  var t = H - (y + OFF);
  var p = doc.pathItems.rectangle(t, x, w, h);
  if (fill) { p.filled = true; p.fillColor = fill; } else { p.filled = false; }
  if (lw>0){ p.stroked=true; p.strokeColor=C(26,26,26); p.strokeWidth=lw; } else { p.stroked=false; }
  return p; }
function poly(pts,fill,lw){
  var a=[]; for(var i=0;i<pts.length;i++){ a.push([pts[i][0], H-(pts[i][1]+OFF)]); }
  var p=doc.pathItems.add(); p.setEntirePath(a); p.closed=true;
  if (fill) { p.filled=true; p.fillColor=fill; } else { p.filled=false; }
  if (lw>0){ p.stroked=true; p.strokeColor=C(26,26,26); p.strokeWidth=lw; p.strokeJoin=StrokeJoin.MITERENDJOIN; } else { p.stroked=false; }
  return p; }
var K1=C(26,26,26), DG=C(58,66,74), LG=C(232,236,240), SB=C(72,136,178),
    EB=C(84,200,240), SH=C(40,46,52), WH=C(255,255,255);
var doc=app.documents.add(DocumentColorSpace.RGB,256,128);
var outl=[];
rect(6,107,44,3,SH,0);
rect(10,75,36,3,SH,0);
rect(40,75,84,3,SH,0);
rect(56,19,32,3,SH,0);
rect(72,99,22,3,SH,0);
rect(104,99,22,3,SH,0);
rect(124,59,112,3,SH,0);
rect(236,57,16,3,SH,0);
var p_A01 = rect(6,72,44,36,C(58,66,74),0);
outl.push(p_A01);
outl.push(poly([[10,32],[46,38],[42,76],[8,72]],C(72,136,178),0));
var p_A03 = rect(14,40,12,22,C(84,200,240),0);
var p_B01 = rect(40,16,84,60,C(58,66,74),0);
outl.push(p_B01);
var p_B02 = rect(56,4,32,16,C(58,66,74),0);
outl.push(p_B02);
var p_B03 = rect(60,8,24,8,C(84,200,240),0);
var p_B04 = rect(44,20,76,14,C(84,200,240),0);
var p_B05 = rect(44,48,76,12,C(84,200,240),0);
var p_C01 = rect(72,76,22,24,C(72,136,178),0);
outl.push(p_C01);
var p_C02 = rect(76,80,14,14,C(84,200,240),0);
var p_C03 = rect(104,76,22,24,C(72,136,178),0);
outl.push(p_C03);
var p_C04 = rect(108,80,14,14,C(84,200,240),0);
var p_D01 = rect(124,16,112,44,C(58,66,74),0);
outl.push(p_D01);
var p_D02 = rect(128,20,104,12,C(232,236,240),0);
var p_D03 = rect(130,44,96,16,C(58,66,74),0);
var p_D04 = rect(134,48,12,8,C(84,200,240),0);
var p_D05 = rect(150,48,12,8,C(84,200,240),0);
var p_D06 = rect(166,48,12,8,C(84,200,240),0);
var p_D07 = rect(182,48,12,8,C(84,200,240),0);
var p_D08 = rect(198,48,12,8,C(84,200,240),0);
var p_D09 = rect(214,48,12,8,C(84,200,240),0);
var p_E01 = rect(236,18,16,40,C(58,66,74),0);
outl.push(p_E01);
var p_E02 = rect(240,22,8,12,C(232,236,240),0);
rect(14,40,12,22,EB,1.5);
rect(60,8,24,8,EB,1.5);
rect(52,22,60,8,EB,1.5);
rect(52,50,60,7,EB,1.5);
rect(76,80,14,14,EB,1.5);
rect(108,80,14,14,EB,1.5);
rect(134,48,12,8,EB,1.5);
rect(150,48,12,8,EB,1.5);
rect(166,48,12,8,EB,1.5);
rect(182,48,12,8,EB,1.5);
rect(198,48,12,8,EB,1.5);
rect(214,48,12,8,EB,1.5);
rect(9,74,38,3,WH,0);
rect(13,34,30,3,WH,0);
rect(43,18,78,3,WH,0);
rect(59,6,26,3,WH,0);
rect(75,78,16,3,WH,0);
rect(107,78,16,3,WH,0);
rect(127,18,106,3,WH,0);
rect(239,20,10,3,WH,0);
rect(6,72,44,36,null,3.0);
poly([[10,32],[46,38],[42,76],[8,72]],null,3.0);
rect(40,16,84,60,null,3.0);
rect(56,4,32,16,null,3.0);
rect(72,76,22,24,null,3.0);
rect(104,76,22,24,null,3.0);
rect(124,16,112,44,null,3.0);
rect(236,18,16,40,null,3.0);
doc.artboards[0].artboardRect=[0,128,256,0];
var o=new ExportOptionsPNG24(); o.artBoardClipping=true; o.antiAliasing=true; o.transparency=true;
doc.exportFile(new File("W:/dsh/zengbing/Tools/laser_ai.png"),ExportType.PNG24,o);
var o2=new ExportOptionsPNG24(); o2.artBoardClipping=true; o2.antiAliasing=true; o2.transparency=true;
o2.horizontalScale=200; o2.verticalScale=200;
doc.exportFile(new File("W:/dsh/zengbing/Tools/laser_ai_2x.png"),ExportType.PNG24,o2);
doc.close(SaveOptions.DONOTSAVECHANGES);