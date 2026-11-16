#target illustrator
// 激光加特林 v3 —— 256x128 屏幕坐标（y 向下）
function C(r,g,b){ var c=new RGBColor(); c.red=r; c.green=g; c.blue=b; return c; }
var H = 128;
var BLACK=C(16,18,22), DGRAY=C(52,62,74), GRAYBLUE=C(64,76,89), BLUE=C(103,132,155);
var WHITE=C(224,229,234), CYAN=C(127,216,240), CYAN_D=C(58,132,160);
var T1=C(186,198,210), T2=C(103,132,155), T3=C(30,38,48), HI=C(216,226,236);

function box(doc,x,y,w,h,fill,lw,r){
  var t = H - y;
  var p = (r && r>0) ? doc.pathItems.roundedRectangle(t,x,w,h,r,r) : doc.pathItems.rectangle(t,x,w,h);
  p.filled=true; p.fillColor=fill;
  if (lw!==0){ p.stroked=true; p.strokeColor=BLACK; p.strokeWidth=(lw||2.2); } else { p.stroked=false; }
  return p;
}
function circ(doc,cx,cy,r,fill,lw){
  var p=doc.pathItems.ellipse(H-cy+r, cx-r, r*2, r*2);
  p.filled=true; p.fillColor=fill;
  if (lw!==0){ p.stroked=true; p.strokeColor=BLACK; p.strokeWidth=(lw||2.2); } else { p.stroked=false; }
  return p;
}
function seg(doc,x1,y1,x2,y2,color,w){
  var p=doc.pathItems.add(); p.setEntirePath([[x1,H-y1],[x2,H-y2]]);
  p.closed=false; p.filled=false; p.stroked=true; p.strokeColor=color; p.strokeWidth=w;
  p.strokeCap=StrokeCap.ROUNDENDCAP; p.strokeJoin=StrokeJoin.ROUNDENDJOIN; return p;
}
function poly(doc,pts,closed,color,w,fill){
  var a=[]; for(var i=0;i<pts.length;i++) a.push([pts[i][0],H-pts[i][1]]);
  var p=doc.pathItems.add(); p.setEntirePath(a); p.closed=closed;
  if(fill){p.filled=true;p.fillColor=fill;}else{p.filled=false;}
  p.stroked=true; p.strokeColor=color; p.strokeWidth=w;
  p.strokeCap=StrokeCap.ROUNDENDCAP; p.strokeJoin=StrokeJoin.ROUNDENDJOIN; return p;
}

function main(){
  var doc=app.documents.add(DocumentColorSpace.RGB,256,128);

  // ===== 枪管簇：上亮 / 中中 / 下暗（六管环排俯视三根）=====
  box(doc,140,44,66,15,T1,2.6,3);
  seg(doc,143,46.4,203,46.4,HI,1.6);
  box(doc,140,82,66,15,T3,2.6,3);
  seg(doc,143,84.4,203,84.4,C(96,108,122),1.6);
  box(doc,140,62,68,20,T2,2.6,3);
  seg(doc,143,65.4,205,65.4,C(158,176,190),1.8);
  // 中间两道箍
  box(doc,158,42,7,58,DGRAY,2.2,2);
  box(doc,188,42,7,58,DGRAY,2.2,2);

  // ===== 机匣 =====
  box(doc,66,46,76,44,GRAYBLUE,2.8,4);
  box(doc,70,49,68,7,WHITE,0,2);
  box(doc,116,60,22,24,BLUE,2.0,2);

  // ===== 顶部大电池 =====
  box(doc,80,24,34,26,CYAN_D,2.4,3);
  box(doc,84,28,26,18,CYAN,1.8,2.5);
  seg(doc,88,33,106,33,C(236,252,255),1.8);
  box(doc,86,46,22,4,DGRAY,1.8,1);      // 电池底座

  // ===== 提把（拱形，跨在机匣上）=====
  box(doc,112,26,40,7,GRAYBLUE,2.2,3);
  box(doc,114,33,7,14,GRAYBLUE,2.0,2);
  box(doc,143,33,7,14,GRAYBLUE,2.0,2);

  // ===== 尾部 =====
  box(doc,40,54,28,30,DGRAY,2.2,3);
  for(var i=0;i<3;i++) box(doc,43,58+i*8,12,4,BLUE,1.2,1);

  // ===== 握把 + 扳机护圈 =====
  box(doc,96,88,22,34,BLUE,2.2,3.5);
  poly(doc,[[112,88],[112,102],[120,107],[128,102],[128,88]],false,BLACK,2.2,null);

  // ===== 枪口环 =====
  box(doc,206,38,13,66,GRAYBLUE,2.6,4);
  circ(doc,212.5,71,10,CYAN,1.8);
  circ(doc,212.5,71,3.8,C(234,252,255),0);

  doc.artboards[0].artboardRect=[0,128,256,0];
  var o=new ExportOptionsPNG24(); o.artBoardClipping=true; o.antiAliasing=true; o.transparency=true;
  doc.exportFile(new File("W:/dsh/zengbing/Tools/laser_ai.png"),ExportType.PNG24,o);
  var o2=new ExportOptionsPNG24(); o2.artBoardClipping=true; o2.antiAliasing=true; o2.transparency=true;
  o2.horizontalScale=200; o2.verticalScale=200;
  doc.exportFile(new File("W:/dsh/zengbing/Tools/laser_ai_2x.png"),ExportType.PNG24,o2);
  doc.close(SaveOptions.DONOTSAVECHANGES);
  return "ok";
}
main();
