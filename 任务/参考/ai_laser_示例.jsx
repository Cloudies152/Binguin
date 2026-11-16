#target illustrator
// v17 —— 明度结构对齐 AI 画法：白顶面 + 灰蓝中段 + 近黑，多段过渡
function C(r,g,b){ var c=new RGBColor(); c.red=r; c.green=g; c.blue=b; return c; }
var H = 128;

var LINE   = C(11,13,16);
var WHITE  = C(246,248,250);
var WHITE_D= C(206,214,222);
var MID    = C(86,96,110);
var MID_D  = C(58,68,80);
var DARK   = C(24,29,37);
var DARK_L = C(52,60,72);
var ICE    = C(127,216,240);
var ICE_D  = C(52,124,152);
var ICE_L  = C(210,246,255);

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
// 白顶面 + 灰蓝中段 + 暗底 的复合块（模仿 AI 的明度分层）
function gradMass(doc,x,y,w,h,rad,lw,topRatio){
  var tr = topRatio || 0.42;
  box(doc,x,y,w,h,MID,(lw||2.2),(rad||3));                 // 中段基色
  box(doc,x+1.6,y+1.6,w-3.2,h*tr,WHITE,0,Math.max(1,(rad||3)-0.8));   // 顶部白
  box(doc,x+1.6,y+1.6,w-3.2,h*tr*0.55,WHITE,0,Math.max(1,(rad||3)-0.8)); // 再加亮
  box(doc,x+1.6,y+h-1.6-h*0.26,w-3.2,h*0.26,MID_D,0,Math.max(1,(rad||3)-0.8)); // 底部中暗
  box(doc,x+1.6,y+h-1.6-h*0.10,w-3.2,h*0.10,DARK,0,Math.max(1,(rad||3)-0.8));  // 最底暗
  // 白面与中段之间加过渡带
  tint(doc,x+1.6,y+1.6+h*tr*0.8,w-3.2,h*0.18,MID,BlendModes.MULTIPLY,30,Math.max(1,(rad||3)-0.8));
  box(doc,x+2.6,y+2.6,w-5.2,1.2,WHITE,0,0.6);              // 顶部高光线
}

function main(){
  var doc=app.documents.add(DocumentColorSpace.RGB,256,128);

  // ===== 枪托 =====
  poly(doc,[[8,40],[40,32],[52,32],[52,76],[16,80]],true,LINE,2.4,MID);
  poly(doc,[[10,42],[39,35],[50,35],[50,58],[12,62]],true,LINE,0,WHITE);
  box(doc,8,40,7,36,DARK,2.2,2);
  box(doc,14,62,26,7,MID,1.8,2);
  box(doc,15,63,24,2,WHITE,0,1);

  // ===== 机匣 =====
  gradMass(doc,50,34,102,46,4,2.8,0.44);
  // 面板分缝（细线，模仿 AI 的刻画）
  box(doc,52,56,98,1.2,DARK,0,0.4);
  box(doc,120,36,1.2,42,DARK,0,0.4);

  // ===== 顶部电池 =====
  gradMass(doc,66,10,44,24,3,2.6,0.40);
  box(doc,70,15,36,12,ICE_D,1.6,2);
  box(doc,72,17,32,8,ICE,0,1.5);
  box(doc,74,18.4,28,2.4,ICE_L,0,1);
  box(doc,74,29,28,3,DARK,1.2,1);

  // ===== 握把 + 扳机护圈 =====
  poly(doc,[[92,80],[114,80],[108,114],[86,114]],true,LINE,2.4,MID);
  poly(doc,[[94,83],[111,83],[106,110],[89,110]],true,LINE,0,WHITE);
  for(var g=0;g<3;g++) box(doc,89+g*0.6,90+g*7,17-g*1.2,2.4,MID_D,0,1);
  poly(doc,[[114,80],[114,94],[124,100],[136,94],[136,80]],false,LINE,2.2,null);

  // ===== 前护木 =====
  gradMass(doc,152,42,54,28,4,2.6,0.40);
  // 散热口
  for(var k=0;k<4;k++){
    box(doc,158+k*12,54,8,10,ICE_D,1.4,1.4);
    box(doc,159.4+k*12,55.4,5.2,7.2,ICE,0,1);
  }

  // ===== 枪管 + 制退器 =====
  box(doc,206,52,40,12,DARK,2.2,2);
  box(doc,210,54,32,2.4,DARK_L,0,1);
  box(doc,244,44,14,28,MID,2.4,3);
  for(var m=0;m<3;m++) box(doc,246,48+m*7,9,3.4,ICE_D,1.0,0.8);

  // ===== 机匣侧面能量条 =====
  box(doc,56,60,88,10,ICE_D,1.8,2);
  box(doc,59,62.4,82,5,ICE,0,1.5);
  box(doc,61,64,78,1.6,ICE_L,0,0.8);

  // ===== 顶部导轨 =====
  for(var i=0;i<10;i++) box(doc,56+i*9,30,5,5,DARK,1.2,1);

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
