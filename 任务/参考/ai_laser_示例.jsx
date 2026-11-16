#target illustrator
// v13 —— 按参考图逐行分布确定的主块结构 + 冰鹅族画风
function C(r,g,b){ var c=new RGBColor(); c.red=r; c.green=g; c.blue=b; return c; }
var H=128, K=256/66;

var LINE=C(16,18,22), BODY=C(64,76,89), BODY_L=C(98,114,130), BODY_D=C(38,48,60);
var PART=C(103,132,155), PART_L=C(142,168,190);
var DARK=C(22,27,34), DARK_L=C(58,68,80);
var WHITE=C(224,229,234), ICE_D=C(56,130,158), ICE=C(127,216,240), ICE_L=C(206,246,255);

function X(v){ return v*K; }
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
// 结构块：坐标用参考图单位
function blk(doc,rx,ry,rw,rh,base,rad,lw){
  var x=X(rx),y=X(ry),w=X(rw),h=X(rh);
  box(doc,x,y,w,h,base,(lw||2.2),(rad||2.5));
  if(h>X(2)){
    tint(doc,x+1.2,y+1.2,w-2.4,h*0.30,BODY_L,BlendModes.SCREEN,28,Math.max(1,(rad||2.5)-1));
    tint(doc,x+1.2,y+h-1.2-h*0.20,w-2.4,h*0.20,BODY_D,BlendModes.MULTIPLY,24,Math.max(1,(rad||2.5)-1));
    box(doc,x+2,y+2,w-4,1.0,WHITE,0,0.5);
  }
}

function main(){
  var doc=app.documents.add(DocumentColorSpace.RGB,256,128);

  // 1) 顶部小模块（x14-27, y0-4）
  blk(doc, 14, 0, 13, 4.5, BODY, 1.6, 2.0);

  // 2) 上沿横条（x2-61, y4-6）
  blk(doc, 2, 4, 59, 3, BODY, 1.6, 2.0);

  // 3) 主机匣（x0-65, y6-17）
  blk(doc, 0, 6, 65.5, 11, BODY, 2.4, 2.6);

  // 4) 前下部（x4-61, y17-21）
  blk(doc, 4, 17, 57, 4.5, BODY, 2.0, 2.2);

  // 5) 中部块（x2-37, y21-23）
  blk(doc, 2, 21, 35, 2.8, BODY, 1.6, 1.8);

  // 6) 握把（x2-15, y23-28）
  poly(doc,[[X(0.4),X(16.6)],[X(15.2),X(16.6)],[X(13.2),X(28)],[X(3.6),X(28)]],true,LINE,2.2,BODY);
  poly(doc,[[X(1.4),X(17.4)],[X(14.2),X(17.4)],[X(12.4),X(27.2)],[X(4.6),X(27.2)]],false,PART_L,1.3,null);
  for(var g=0;g<3;g++) box(doc, X(4.6), X(24.0+g*1.5), X(7.2-g*0.9), X(0.8), BODY_D, 0, 0.4);

  // ===== 内部细节 =====
  // 长能量条（x36-56, y9.5-13）
  box(doc, X(35.5), X(9.2), X(21), X(4.2), ICE_D, 1.4, 1.2);
  box(doc, X(36.2), X(9.9), X(19.6), X(2.8), ICE, 0, 1.0);
  box(doc, X(36.9), X(10.2), X(18.2), X(1.0), ICE_L, 0, 0.5);

  // 竖排能量格（x30-48, y14.5-19.5）
  for(var i=0;i<5;i++){
    box(doc, X(30.5+i*3.6), X(14.6), X(2.4), X(4.6), ICE_D, 0.9, 0.7);
    box(doc, X(31.0+i*3.6), X(15.1), X(1.5), X(3.6), ICE, 0, 0.5);
  }

  // 弹匣（x20-24, y14-22）
  box(doc, X(20), X(13.6), X(4.8), X(8.4), DARK, 1.8, 1.4);
  for(var r=0;r<3;r++) box(doc, X(20.7), X(15.0+r*2.4), X(3.4), X(1.1), PART, 0, 0.4);

  // 枪口（x59-65, y6-13 区域前端）
  box(doc, X(59.5), X(6.6), X(5.5), X(6.4), DARK, 2.0, 1.6);
  box(doc, X(60.6), X(9.4), X(3.4), X(2.4), ICE, 1.2, 0.8);

  // 扳机护圈（x15-20）
  poly(doc,[[X(14.8),X(17)],[X(14.8),X(20.4)],[X(17.6),X(21.6)],[X(20.4),X(20.4)],[X(20.4),X(17)]],false,LINE,1.9,null);

  // 后部细节：尾部斜面高光
  poly(doc,[[X(0.8),X(7)],[X(0.8),X(15.6)],[X(3.4),X(15.6)],[X(3.4),X(7)]],false,WHITE,1.0,null);

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
