#target illustrator
// v19 —— 照 Shotgun(DP-12) 的画法：细黑描边 + 白色边缘高光 + 平涂
function C(r,g,b){ var c=new RGBColor(); c.red=r; c.green=g; c.blue=b; return c; }
var H=128;

var BLK  = C(0,0,0);
var BODY = C(74,83,96);      // #4A5360
var BODY_L = C(94,104,118);
var BLUE = C(76,123,157);    // #4C7B9D
var BLUE_L = C(110,150,180);
var WHT  = C(255,255,255);
var ICE  = C(127,216,240), ICE_D=C(52,124,152), ICE_L=C(216,247,255);

function box(doc,x,y,w,h,fill,lw,r){
  var t=H-y;
  var p=(r&&r>0)?doc.pathItems.roundedRectangle(t,x,w,h,r,r):doc.pathItems.rectangle(t,x,w,h);
  p.filled=true; p.fillColor=fill;
  if(lw!==0){p.stroked=true;p.strokeColor=BLK;p.strokeWidth=(lw||1.6);}else{p.stroked=false;}
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
// DP-12 式：平涂 + 细黑描边 + 顶边白色高光条
function cel(doc,x,y,w,h,fill,rad,lw,rim){
  box(doc,x,y,w,h,fill,(lw||1.6),(rad||2));
  if(rim!==false && h>6){
    box(doc,x+1.2,y+1.2,w-2.4,2.0,WHT,0,0.8);
  }
}
function celPoly(doc,pts,fill,lw,rim){
  poly(doc,pts,true,BLK,(lw||1.6),fill);
  if(rim) {
    var rp=[]; var n=pts.length;
    // 顶边高光：沿多边形上沿内缩一条
    for(var i=0;i<n;i++){
      var p=pts[i];
      if(p[1] < pts[(i+1)%n][1]) rp.push([p[0]+1.5,p[1]+1.8]);
    }
    if(rp.length>=2) poly(doc,rp,false,WHT,1.6,null);
  }
}

function main(){
  var doc=app.documents.add(DocumentColorSpace.RGB,256,128);

  // ===== 枪托（斜切，白边高光）=====
  celPoly(doc,[[8,40],[40,32],[54,32],[54,78],[16,82]],BODY,1.8,true);
  box(doc,8,40,6,42,BLK,1.6,1.5);
  box(doc,14,64,26,7,BLUE,1.4,1.2);
  box(doc,15,65,24,2,WHT,0,0.8);

  // ===== 机匣 =====
  cel(doc,54,34,100,42,BODY,3,1.9);
  box(doc,54,34,100,4,WHT,0,2);          // 顶面白条
  // 侧面蓝色分块
  cel(doc,60,42,88,18,BLUE,2,1.6);
  box(doc,62,43.6,84,2.4,BLUE_L,0,1.2);
  // 能量核心
  box(doc,66,48,76,8,ICE_D,1.4,1.4);
  box(doc,68,49.4,72,5.2,ICE,0,1);
  box(doc,70,50.4,68,1.6,ICE_L,0,0.7);

  // ===== 顶部电池 =====
  cel(doc,68,8,42,24,BODY,3,1.8);
  box(doc,68,8,42,3.4,WHT,0,1.5);
  box(doc,72,15,34,11,ICE_D,1.4,1.4);
  box(doc,74,16.4,30,8.2,ICE,0,1);
  box(doc,76,17.4,26,2,ICE_L,0,0.8);
  // 电池到机匣的导线
  poly(doc,[[112,24],[120,30],[122,34]],false,BLK,2.0,null);

  // ===== 握把 =====
  celPoly(doc,[[96,76],[118,76],[112,114],[88,114]],BLUE,1.8,true);
  for(var g=0;g<4;g++) box(doc,92+g*0.4,88+g*6,17-g*1.0,1.8,BLACK_FIX(),0,0.6);
  // 扳机护圈
  poly(doc,[[118,76],[118,94],[128,100],[140,94],[140,76]],false,BLK,1.8,null);

  // ===== 前护木 =====
  celPoly(doc,[[156,30],[212,30],[218,38],[218,72],[210,80],[156,80]],BODY,1.9,true);
  // 散热口（黑孔 + 冰蓝内光）
  for(var k=0;k<4;k++){
    box(doc,161+k*12,50,9,20,BLK,0,1);
    box(doc,162.2+k*12,51.4,6.6,17.2,ICE_D,0,0.8);
    box(doc,163.2+k*12,52.6,4.6,4.0,ICE,0,0.5);
  }
  box(doc,158,44,52,2.2,WHT,0,1);

  // ===== 枪管 =====
  box(doc,218,50,22,14,BLK,1.6,1.5);
  box(doc,220,51.5,18,2.0,C(90,98,110),0,0.8);

  // ===== 制退器 =====
  cel(doc,238,42,16,30,BODY,2,1.8);
  box(doc,238,42,16,3,WHT,0,1.2);
  for(var m=0;m<3;m++){
    box(doc,240,47+m*8,12,4.4,BLK,0,0.8);
    box(doc,241.2,48.2+m*8,9.6,2.0,ICE_D,0,0.6);
  }

  // ===== 导轨齿 =====
  for(var i=0;i<10;i++) box(doc,58+i*9,29.5,6,4.5,C(40,46,56),1.1,0.8);

  doc.artboards[0].artboardRect=[0,128,256,0];
  var o=new ExportOptionsPNG24();o.artBoardClipping=true;o.antiAliasing=true;o.transparency=true;
  doc.exportFile(new File("W:/dsh/zengbing/Tools/laser_ai.png"),ExportType.PNG24,o);
  var o2=new ExportOptionsPNG24();o2.artBoardClipping=true;o2.antiAliasing=true;o2.transparency=true;
  o2.horizontalScale=200;o2.verticalScale=200;
  doc.exportFile(new File("W:/dsh/zengbing/Tools/laser_ai_2x.png"),ExportType.PNG24,o2);
  doc.close(SaveOptions.DONOTSAVECHANGES);
  return "ok";
}
function BLACK_FIX(){ return C(30,36,44); }
main();
