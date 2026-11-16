#target illustrator
// v14 —— 冰鹅族激光机枪：保留参考图布局，重排几何与比例，追求好看
function C(r,g,b){ var c=new RGBColor(); c.red=r; c.green=g; c.blue=b; return c; }
var H = 128;

var LINE   = C(16,18,22);
var BODY   = C(64,76,89);
var BODY_L = C(100,116,132);
var BODY_D = C(38,48,60);
var PART   = C(103,132,155);
var PART_L = C(148,172,194);
var DARK   = C(24,30,38);
var DARK_L = C(62,72,84);
var WHITE  = C(228,233,238);
var ICE_D  = C(52,124,152);
var ICE    = C(127,216,240);
var ICE_L  = C(208,246,255);

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
function tint(doc,x,y,w,h,color,mode,op,r){
  var p=box(doc,x,y,w,h,color,0,(r||1.5));
  p.opacity=op; p.blendingMode=mode; return p;
}
// 体积块：底 + 顶部亮带 + 底部暗带 + 高光线
function mass(doc,x,y,w,h,base,rad,lw){
  box(doc,x,y,w,h,base,(lw||2.2),(rad||3));
  if(h>10){
    tint(doc,x+2,y+2,w-4,h*0.28,BODY_L,BlendModes.SCREEN,30,Math.max(1,(rad||3)-1));
    tint(doc,x+2,y+h-2-h*0.20,w-4,h*0.20,BODY_D,BlendModes.MULTIPLY,26,Math.max(1,(rad||3)-1));
    box(doc,x+3,y+3,w-6,1.2,WHITE,0,0.6);
  }
}

function main(){
  var doc=app.documents.add(DocumentColorSpace.RGB,256,128);

  // ===== 枪托（斜切）=====
  poly(doc,[[10,42],[42,34],[52,34],[52,74],[18,78]],true,LINE,2.4,BODY);
  poly(doc,[[12,44],[41,37],[50,37],[50,44],[13,52]],false,BODY_L,1.6,null);
  box(doc,10,42,7,34,BODY_D,2.2,2);                 // 托底板
  box(doc,14,58,26,7,PART,1.8,2);                   // 贴腮块
  box(doc,15,59,24,2,WHITE,0,1);

  // ===== 机匣 =====
  mass(doc,50,38,100,40,BODY,4,2.8);
  box(doc,54,42,92,5,WHITE,0,2);                    // 顶面白条
  // 顶部导轨
  for(var i=0;i<11;i++) box(doc,56+i*8,34,5,5,DARK,1.2,1);

  // ===== 顶部电池 =====
  mass(doc,64,14,44,22,BODY,3,2.6);
  box(doc,68,18,36,10,ICE_D,1.6,2);
  box(doc,70,20,32,6,ICE,0,1.5);
  box(doc,72,21,28,2,ICE_L,0,0.8);
  box(doc,72,30,28,3,DARK,1.2,1);                   // 电池底座\n  box(doc,70,33,6,7,BODY,1.4,1);\n  box(doc,96,33,6,7,BODY,1.4,1);

  // ===== 握把 + 扳机护圈 =====
  poly(doc,[[92,78],[114,78],[108,112],[86,112]],true,LINE,2.4,PART);
  poly(doc,[[94,81],[111,81],[106,108],[89,108]],false,PART_L,1.6,null);
  for(var g=0;g<3;g++) box(doc,89+g*0.6,88+g*6,17-g*1.2,2.4,BODY_D,0,1);
  poly(doc,[[114,78],[114,90],[124,96],[136,90],[136,78]],false,LINE,2.2,null);

  // ===== 弹匣 =====

  // ===== 前护木 =====
  mass(doc,150,44,58,26,BODY,4,2.6);
  box(doc,154,48,50,4,WHITE,0,1.6);
  for(var k=0;k<4;k++) box(doc,156+k*12,58,8,8,ICE_D,1.2,1.4);
  for(var k2=0;k2<4;k2++) box(doc,157.4+k2*12,59.4,5.2,5.2,ICE,0,1);

  // ===== 枪管 + 制退器 =====
  mass(doc,208,50,40,14,DARK,3,2.2);
  box(doc,212,52,18,3,DARK_L,0,1);
  box(doc,244,42,14,30,BODY,2.4,3);
  box(doc,246,47,9,3,ICE_D,1.0,0.8);
  box(doc,246,53,9,3,ICE_D,1.0,0.8);
  box(doc,246,59,9,3,ICE,1.0,0.8);

  // ===== 侧面能量条 =====
  box(doc,54,52,90,10,ICE_D,1.8,2);
  box(doc,57,54.5,84,5,ICE,0,1.5);
  box(doc,59,56,80,1.6,ICE_L,0,0.8);

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
