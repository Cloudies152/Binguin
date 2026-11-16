#target illustrator
// 激光机枪 v6 —— 模仿 AI 绘画的画法：每个部件 2~3 档明暗 + 细高光线 + 深色描边
// 256x128，屏幕坐标（y 向下）
function C(r,g,b){ var c=new RGBColor(); c.red=r; c.green=g; c.blue=b; return c; }
var H = 128;

var LINE  = C(18,22,28);        // 描边（深蓝黑，不是纯黑）
var BODY  = C(64,76,89);        // #404C59
var BODY_L= C(86,102,118);      // 亮面
var BODY_D= C(44,54,66);        // 暗面
var PART  = C(103,132,155);     // #67849B
var PART_L= C(130,158,180);
var PART_D= C(74,98,118);
var BARR  = C(26,32,40);
var BARR_L= C(58,68,80);
var WHITE = C(228,233,238);
var CYAN  = C(127,216,240);
var CYAN_D= C(56,128,158);
var CYAN_L= C(196,242,252);

function box(doc,x,y,w,h,fill,lw,r){
  var t=H-y;
  var p=(r&&r>0)?doc.pathItems.roundedRectangle(t,x,w,h,r,r):doc.pathItems.rectangle(t,x,w,h);
  p.filled=true; p.fillColor=fill;
  if(lw!==0){p.stroked=true;p.strokeColor=LINE;p.strokeWidth=(lw||2.2);}else{p.stroked=false;}
  return p;
}
function circ(doc,cx,cy,r,fill,lw){
  var p=doc.pathItems.ellipse(H-cy+r,cx-r,r*2,r*2);
  p.filled=true; p.fillColor=fill;
  if(lw!==0){p.stroked=true;p.strokeColor=LINE;p.strokeWidth=(lw||2.2);}else{p.stroked=false;}
  return p;
}
function poly(doc,pts,closed,color,w,fill){
  var a=[];for(var i=0;i<pts.length;i++)a.push([pts[i][0],H-pts[i][1]]);
  var p=doc.pathItems.add();p.setEntirePath(a);p.closed=closed;
  if(fill){p.filled=true;p.fillColor=fill;}else{p.filled=false;}
  p.stroked=true;p.strokeColor=color;p.strokeWidth=w;
  p.strokeCap=StrokeCap.ROUNDENDCAP;p.strokeJoin=StrokeJoin.ROUNDENDJOIN;return p;
}
// 三段明暗的主体：底 + 顶部亮带 + 底部暗带 + 细高光线
function mass(doc,x,y,w,h,base,light,dark,radius,lw,inset){
  var i = inset === undefined ? 2.2 : inset;
  box(doc,x,y,w,h,base,lw,radius);
  // 顶部亮带（略内缩，避免压到描边）
  box(doc,x+i,y+i,w-2*i,(h*0.20),light,0,Math.max(0,radius-1.2));
  // 底部暗带
  box(doc,x+i,y+h-i-(h*0.15),w-2*i,(h*0.15),dark,0,Math.max(0,radius-1.2));
  // 顶部 1px 高光线
  box(doc,x+i+1,y+i+0.8,w-2*i-2,1.2,WHITE,0,0);
}

function main(){
  var doc=app.documents.add(DocumentColorSpace.RGB,256,128);

  // ===== 枪托 =====
  poly(doc,[[16,62],[46,56],[58,56],[58,96],[28,100]],true,LINE,2.4,BODY);
  poly(doc,[[19,64],[45,59],[58,59],[58,72],[27,80]],false,C(120,134,148),1.4,null);
  box(doc,16,64,7,32,BODY_D,2.0,1.5);
  box(doc,28,76,24,7,PART,1.8,1.5);
  box(doc,29,77,22,2.4,PART_L,0,1);

  // ===== 机匣 =====
  mass(doc,56,52,70,46,BODY,BODY_L,BODY_D,4,2.6);
  box(doc,62,64,24,28,PART,2.0,2);
  box(doc,63.2,65.2,21.6,8,PART_L,0,1.5);
  for(var i=0;i<3;i++){
    box(doc,65.5+i*6.5,70,4,18,CYAN_D,1.2,1);
    box(doc,66.5+i*6.5,71.5,2,12,CYAN_L,0,0.8);
  }
  box(doc,84,64,5,10,BARR,1.4,1);
  circ(doc,84,84,2.0,BODY_D,1.2);
  circ(doc,84,92,2.0,BODY_D,1.2);

  // ===== 顶部电池 =====
  mass(doc,74,30,46,20,BODY,BODY_L,BODY_D,3,2.4);
  box(doc,78,36,38,8,CYAN_D,1.6,2);
  box(doc,80,37.5,34,4,CYAN,0,1.5);
  box(doc,81,38.3,32,1.4,WHITE,0,0.6);
  for(var j=0;j<7;j++) box(doc,78+j*6,25,4,5,BARR,1.2,0.8);

  // ===== 护罩 / 枪管 =====
  mass(doc,124,52,98,44,BARR,BARR_L,C(12,16,20),4,2.6);
  box(doc,128,58,90,7,PART,1.6,2);
  box(doc,129,59,88,2.2,PART_L,0,1);
  box(doc,128,70,90,18,CYAN_D,1.6,2);
  box(doc,130,73,86,12,CYAN,1.4,2);
  box(doc,131.5,74.5,83,4,WHITE,0,0);
  box(doc,131.5,80,83,3.5,CYAN_L,0,0);
  for(var k=0;k<4;k++) box(doc,138+k*20,88,12,3,C(14,18,22),0,1);
  box(doc,150,52,6,44,PART,1.6,1);
  box(doc,190,52,6,44,PART,1.6,1);

  // ===== 枪口 =====
  box(doc,222,54,18,40,BARR,2.4,3);
  box(doc,223,55.5,16,6,BARR_L,0,2);
  for(var m=0;m<3;m++) box(doc,225,60+m*10,12,4,C(10,13,17),0,1);
  circ(doc,236,74,5,CYAN,1.6);
  circ(doc,236,74,2,CYAN_L,0);

  // ===== 握把 =====
  poly(doc,[[92,98],[112,98],[106,124],[86,124]],true,LINE,2.4,PART);
  poly(doc,[[95,100],[110,100],[106,120],[89,120]],false,PART_D,1.2,null);
  for(var g=0;g<3;g++) box(doc,92+g*0.8,104+g*6,14,2.2,PART_D,0,1);

  // ===== 弹匣 =====
  mass(doc,124,88,24,34,BARR,BARR_L,C(10,13,17),3,2.4,2.0);
  for(var r=0;r<3;r++) box(doc,128,94+r*8,16,2.6,PART,0,1);

  // ===== 扳机 + 护圈 =====
  poly(doc,[[112,96],[112,107],[121,111],[130,107],[130,96]],false,LINE,2.2,null);
  box(doc,116,98,3,9,BARR,1.0,1);

  // ===== 拉机柄 =====
  box(doc,58,60,12,4,PART,1.4,1);
  box(doc,70,57,4,10,BARR,1.2,1);

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
