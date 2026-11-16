#target illustrator
// v18 —— 细节细化实验：5 段明度 + 面板分缝 + 散热栅 + 线缆 + 制退器
function C(r,g,b){ var c=new RGBColor(); c.red=r; c.green=g; c.blue=b; return c; }
var H=128;

var LINE=C(10,12,15);
var C1=C(250,252,254);  // 最亮
var C2=C(208,216,224);  // 亮
var C3=C(140,154,170);  // 中亮
var C4=C(84,96,112);    // 中
var C5=C(46,55,68);     // 暗
var C6=C(22,27,35);     // 近黑
var ICE=C(127,216,240), ICE_D=C(48,118,148), ICE_L=C(214,247,255);
var BRASS=C(196,150,74);

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
// 渐变体积：竖向 3 色标（亮->中->暗）
function gradMass(doc,x,y,w,h,rad,lw){
  var g=doc.gradients.add(); g.type=GradientType.LINEAR;
  g.gradientStops[0].color=C1; g.gradientStops[0].rampPoint=0;
  g.gradientStops[1].color=C4; g.gradientStops[1].rampPoint=52;
  while (g.gradientStops.length < 3) g.gradientStops.add();
  var last = g.gradientStops[g.gradientStops.length-1];
  last.color=C6; last.rampPoint=100;
  var p=box(doc,x,y,w,h,C4,(lw||2.4),(rad||3));
  var gc=new GradientColor(); gc.gradient=g;
  gc.origin=[x, H-y]; gc.destination=[x, H-(y+h)];
  p.fillColor=gc;
  box(doc,x+2.4,y+2.4,w-4.8,0.9,ICE_L,0,0.4);
}
function mass5(doc,x,y,w,h,rad,lw){ gradMass(doc,x,y,w,h,rad,lw); }
function seamH(doc,x,y,w){ box(doc,x,y,w,1.0,LINE,0,0); box(doc,x,y+1.0,w,0.8,C2,0,0); }
function seamV(doc,x,y,h){ box(doc,x,y,1.0,h,LINE,0,0); box(doc,x+1.0,y,0.8,h,C2,0,0); }

function main(){
  var doc=app.documents.add(DocumentColorSpace.RGB,256,128);

  // ===== 枪托 =====
  poly(doc,[[6,38],[38,30],[52,30],[52,78],[14,82]],true,LINE,2.4,C4);
  poly(doc,[[8,40],[37,32.5],[50,32.5],[50,52],[10,58]],true,LINE,0,C1);
  poly(doc,[[10,58],[50,52],[50,58],[12,63]],true,LINE,0,C2);
  box(doc,6,38,6,40,C6,2.2,2);                    // 托底板
  box(doc,12,66,28,6,C3,1.6,1.6);                 // 贴腮
  box(doc,12.6,66.8,26.8,1.6,C1,0,0.6);
  seamH(doc,10,74,32);

  // ===== 机匣 =====
  poly(doc,[[52,30],[152,30],[152,66],[142,74],[52,74]],true,LINE,2.6,C4);  // 尾部斜切
  mass5(doc,52,34,96,38,3,0);
  // 面板分缝
  seamV(doc,86,36,36);
  seamV(doc,120,36,36);
  seamH(doc,54,52,92);
  // 抛壳口 + 小盖板
  box(doc,96,38,18,7,C6,1.2,1);
  box(doc,98,39.4,14,2.2,C5,0,0.6);
  // 侧面能量条
  box(doc,56,58,84,9,ICE_D,1.6,1.6);
  box(doc,59,60,78,5,ICE,0,1.2);
  box(doc,61,61.4,74,1.4,ICE_L,0,0.6);

  // ===== 顶部电池 + 线缆 =====
  mass5(doc,64,8,46,24,3,2.4);
  box(doc,68,13,38,12,ICE_D,1.6,1.6);
  box(doc,70,15,34,8,ICE,0,1.2);
  box(doc,72,16.4,30,2.2,ICE_L,0,0.8);
  seamH(doc,66,27,42);
  // 线缆（从电池到机匣）
  poly(doc,[[110,26],[118,30],[120,36]],false,C6,2.2,null);
  poly(doc,[[110,26],[118,30],[120,36]],false,C5,0.9,null);

  // ===== 顶部导轨 =====
  for(var i=0;i<9;i++) box(doc,56+i*10,26,6,6,C5,1.2,1);

  // ===== 握把 =====
  poly(doc,[[94,74],[116,74],[110,112],[86,112]],true,LINE,2.4,C4);
  poly(doc,[[96,77],[113,77],[108,108],[89,108]],true,LINE,0,C3);
  for(var g=0;g<4;g++) box(doc,90+g*0.4,86+g*6,17-g*1.0,2.0,C5,0,0.6);
  // 扳机 + 护圈
  poly(doc,[[116,74],[116,92],[126,98],[138,92],[138,74]],false,LINE,2.2,null);
  box(doc,120,74,2.6,10,C5,1.0,0.8);

  // ===== 前护木 =====
  poly(doc,[[152,30],[208,30],[214,38],[214,70],[206,78],[152,78]],true,LINE,2.6,C4);
  mass5(doc,152,34,56,38,3,0);
  // 散热栅（4 格）
  for(var k=0;k<4;k++){
    box(doc,157+k*12,50,9,18,ICE_D,1.2,1.2);
    box(doc,158.4+k*12,51.4,6.2,15.2,ICE,0,0.9);
    box(doc,159.2+k*12,52.4,4.6,3.0,ICE_L,0,0.5);
  }
  seamH(doc,154,46,52);
  seamH(doc,154,71,52);

  // ===== 枪管 + 制退器 =====
  box(doc,214,48,26,16,C6,2.2,2);
  box(doc,216,50,22,2.6,C5,0,1);
  for(var r=0;r<3;r++) box(doc,218+r*7,54,3,8,C5,0,0.8);   // 管箍
  box(doc,240,42,16,28,C4,2.4,3);
  mass5(doc,240,42,16,28,2,0);
  for(var m=0;m<3;m++) box(doc,243,46+m*8,10,4,ICE_D,1.0,0.8);
  box(doc,245,48.5,6,1.4,ICE,0,0.5);

  // ===== 挂绳环 =====
  box(doc,148,72,8,6,C3,1.4,1);

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
