#target illustrator
// 激光机枪 v7 —— 用上 Pathfinder 真挖孔 + 线性渐变做体积
// 256x128，屏幕坐标（y 向下）
function C(r,g,b){ var c=new RGBColor(); c.red=r; c.green=g; c.blue=b; return c; }
var H = 128;

var LINE  = C(18,22,28);
var BODY_D= C(40,50,62), BODY=C(64,76,89), BODY_L=C(96,112,128);
var PART_D= C(72,96,116), PART=C(103,132,155), PART_L=C(138,166,188);
var BARR_D= C(14,18,24), BARR=C(34,42,52), BARR_L=C(72,84,98);
var WHITE = C(232,237,242);
var CYAN_D= C(46,116,146), CYAN=C(127,216,240), CYAN_L=C(206,246,255);

function box(doc,x,y,w,h,fill,lw,r){
  var t=H-y;
  var p=(r&&r>0)?doc.pathItems.roundedRectangle(t,x,w,h,r,r):doc.pathItems.rectangle(t,x,w,h);
  p.filled=true; p.fillColor=fill;
  if(lw!==0){p.stroked=true;p.strokeColor=LINE;p.strokeWidth=(lw||2.2);}else{p.stroked=false;}
  return p;
}
function poly(doc,pts,closed,color,w,fill){
  var a=[];for(var i=0;i<pts.length;i++)a.push([pts[i][0],H-pts[i][1]]);
  var p=doc.pathItems.add();p.setEntirePath(a);p.closed=closed;
  if(fill){p.filled=true;p.fillColor=fill;}else{p.filled=false;}
  if(w>0){p.stroked=true;p.strokeColor=color;p.strokeWidth=w;
    p.strokeCap=StrokeCap.ROUNDENDCAP;p.strokeJoin=StrokeJoin.ROUNDENDJOIN;}
  return p;
}
// 线性渐变填充（垂直方向渐变，模拟圆柱体积）
function gradBox(doc,x,y,w,h,r,dark,light,lw,angleReverse){
  var grad = doc.gradients.add();
  grad.type = GradientType.LINEAR;
  grad.gradientStops[0].color = angleReverse ? light : dark;
  grad.gradientStops[0].rampPoint = 0;
  grad.gradientStops[1].color = angleReverse ? dark : light;
  grad.gradientStops[1].rampPoint = 100;
  var t=H-y;
  var p=(r&&r>0)?doc.pathItems.roundedRectangle(t,x,w,h,r,r):doc.pathItems.rectangle(t,x,w,h);
  p.filled=true;
  var gc=new GradientColor(); gc.gradient=grad;
  gc.origin=[x, H-y];                 // 顶部
  gc.destination=[x, H-(y+h)];        // 底部
  gc.angle=90;                        // 垂直
  p.fillColor=gc;
  if(lw!==0){p.stroked=true;p.strokeColor=LINE;p.strokeWidth=(lw||2.2);}else{p.stroked=false;}
  return p;
}
// 用 Pathfinder 在 target 上挖掉 cutters（真镂空）
function cutHoles(doc, target, cutters){
  doc.selection = null;
  target.selected = true;
  for (var i=0;i<cutters.length;i++) cutters[i].selected = true;
  app.executeMenuCommand("group");
  app.executeMenuCommand("Live Pathfinder Subtract");
  app.executeMenuCommand("expandStyle");
  app.executeMenuCommand("ungroup");
}

function main(){
  var doc=app.documents.add(DocumentColorSpace.RGB,256,128);

  // ===== 枪托 =====
  poly(doc,[[16,62],[46,56],[58,56],[58,96],[28,100]],true,LINE,2.4,BODY);
  box(doc,19,64,26,5,BODY_L,0,1.5);
  box(doc,16,64,7,32,BODY_D,2.0,1.5);
  box(doc,28,76,24,7,PART,1.8,1.5);
  box(doc,29,77,22,2,WHITE,0,1);

  // ===== 机匣（渐变体积）=====
  gradBox(doc,56,52,70,46,4,BODY_D,BODY_L,2.6,false);
  box(doc,62,64,24,28,PART,2.0,2);
  box(doc,63.4,65.4,21.2,7,PART_L,0,1.5);
  for(var i=0;i<3;i++){
    gradBox(doc,65.5+i*6.5,70,4,18,1.2,CYAN_D,CYAN,1.2,false);
    box(doc,66.4+i*6.5,71.5,1.6,12,CYAN_L,0,0.8);
  }
  box(doc,84,64,5,10,BARR_D,1.4,1);

  // ===== 顶部电池 =====
  gradBox(doc,74,30,46,20,3,BODY_D,BODY_L,2.4,false);
  box(doc,78,36,38,8,CYAN_D,1.6,2);
  box(doc,80,37.5,34,4,CYAN,0,1.5);
  box(doc,81,38.3,32,1.2,WHITE,0,0.6);
  for(var j=0;j<7;j++) box(doc,78+j*6,25,4,5,BARR_D,1.2,0.8);

  // ===== 护罩：先画主体，再挖真孔 =====
  var shroud = gradBox(doc,124,52,98,44,4,BARR_D,BARR_L,2.6,false);
  var vents = [];
  for(var k=0;k<4;k++) vents.push(box(doc,138+k*20,88,12,4,C(0,0,0),0,1));
  for(var m=0;m<3;m++) vents.push(box(doc,150,58+m*5,10,3.2,C(0,0,0),0,0.8));
  cutHoles(doc, shroud, vents);

  // 护罩上的附加件
  box(doc,128,58,90,7,PART,1.6,2);
  box(doc,129,59,88,2,PART_L,0,1);
  box(doc,150,52,6,44,PART,1.6,1);
  box(doc,190,52,6,44,PART,1.6,1);
  gradBox(doc,128,70,90,16,2,CYAN_D,CYAN,1.6,false);
  box(doc,131.5,74,83,3.4,WHITE,0,0);
  box(doc,131.5,79,83,3,CYAN_L,0,0);

  // ===== 枪口 =====
  gradBox(doc,222,54,18,40,3,BARR_D,BARR_L,2.4,true);
  for(var n=0;n<3;n++) box(doc,225,60+n*10,12,4,C(10,13,17),0,1);
  box(doc,236,66,5,16,CYAN,1.4,1);
  box(doc,237.2,68,2.6,12,CYAN_L,0,0.8);

  // ===== 握把 =====
  poly(doc,[[92,98],[112,98],[106,124],[86,124]],true,LINE,2.4,PART);
  poly(doc,[[95,100],[110,100],[106,120],[89,120]],false,PART_D,1.2,null);
  for(var g=0;g<3;g++) box(doc,92+g*0.8,104+g*6,14,2,PART_D,0,1);

  // ===== 弹匣 =====
  gradBox(doc,124,88,24,34,3,BARR_D,BARR_L,2.4,false);
  for(var r2=0;r2<3;r2++) box(doc,128,94+r2*8,16,2.4,PART,0,1);

  // ===== 扳机护圈 + 拉机柄 =====
  poly(doc,[[112,96],[112,107],[121,111],[130,107],[130,96]],false,LINE,2.2,null);
  box(doc,116,98,3,9,BARR_D,1.0,1);
  box(doc,58,60,12,4,PART,1.4,1);
  box(doc,70,57,4,10,BARR_D,1.2,1);

  doc.artboards[0].artboardRect=[0,128,256,0];
  var o=new ExportOptionsPNG24();o.artBoardClipping=true;o.antiAliasing=true;o.transparency=true;
  doc.exportFile(new File("W:/dsh/zengbing/Tools/laser_ai.png"),ExportType.PNG24,o);
  var o2=new ExportOptionsPNG24();o2.artBoardClipping=true;o2.antiAliasing=true;o2.transparency=true;
  o2.horizontalScale=200;o2.verticalScale=200;
  doc.exportFile(new File("W:/dsh/zengbing/Tools/laser_ai_2x.png"),ExportType.PNG24,o2);
  doc.close(SaveOptions.DONOTSAVECHANGES);
  return "ok";
}
main();
