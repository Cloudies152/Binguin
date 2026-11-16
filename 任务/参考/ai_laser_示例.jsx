#target illustrator
// 激光机枪 v5 —— 单管；配色回归本项目武器规范；细节加强
// 256x128，屏幕坐标（y 向下）
function C(r,g,b){ var c=new RGBColor(); c.red=r; c.green=g; c.blue=b; return c; }
var H = 128;

var BLACK  = C(16,18,22);      // #101216 描边
var BODY   = C(64,76,89);      // #404C59 机身
var PART   = C(103,132,155);   // #67849B 部件
var DARK   = C(22,27,34);      // 枪管/深色
var WHITE  = C(224,229,234);   // #E0E5EA 高光
var CYAN   = C(127,216,240);   // #7FD8F0 能量
var CYAN_D = C(58,132,160);

function box(doc,x,y,w,h,fill,lw,r){
  var t=H-y;
  var p=(r&&r>0)?doc.pathItems.roundedRectangle(t,x,w,h,r,r):doc.pathItems.rectangle(t,x,w,h);
  p.filled=true; p.fillColor=fill;
  if(lw!==0){p.stroked=true;p.strokeColor=BLACK;p.strokeWidth=(lw||2.2);}else{p.stroked=false;}
  return p;
}
function circ(doc,cx,cy,r,fill,lw){
  var p=doc.pathItems.ellipse(H-cy+r,cx-r,r*2,r*2);
  p.filled=true; p.fillColor=fill;
  if(lw!==0){p.stroked=true;p.strokeColor=BLACK;p.strokeWidth=(lw||2.2);}else{p.stroked=false;}
  return p;
}
function poly(doc,pts,closed,color,w,fill){
  var a=[];for(var i=0;i<pts.length;i++)a.push([pts[i][0],H-pts[i][1]]);
  var p=doc.pathItems.add();p.setEntirePath(a);p.closed=closed;
  if(fill){p.filled=true;p.fillColor=fill;}else{p.filled=false;}
  p.stroked=true;p.strokeColor=color;p.strokeWidth=w;
  p.strokeCap=StrokeCap.ROUNDENDCAP;p.strokeJoin=StrokeJoin.ROUNDENDJOIN;return p;
}

function main(){
  var doc=app.documents.add(DocumentColorSpace.RGB,256,128);

  // ===== 枪托 =====
  poly(doc,[[16,62],[46,56],[58,56],[58,96],[28,100]],true,BLACK,2.4,BODY);
  box(doc,20,62,26,5,WHITE,0,1.5);            // 托顶高光
  box(doc,16,64,7,32,DARK,2.0,1.5);           // 托底板
  box(doc,28,76,24,7,PART,1.8,1.5);           // 贴腮块

  // ===== 机匣 =====
  box(doc,56,52,70,46,BODY,2.6,4);
  box(doc,59,56,64,6,WHITE,0,1.5);            // 顶部高光条
  box(doc,62,64,24,28,PART,2.0,2);            // 侧面板
  for(var i=0;i<3;i++) box(doc,65.5+i*6.5,68,4,20,CYAN,1.2,1);   // 能量格
  box(doc,84,64,5,10,DARK,1.4,1);             // 抛壳口
  circ(doc,84,84,2.0,DARK,1.2);               // 螺栓
  circ(doc,84,92,2.0,DARK,1.2);

  // ===== 顶部电池 + 导轨 =====
  box(doc,74,30,46,20,BODY,2.4,3);
  box(doc,78,34,38,8,CYAN_D,1.6,2);
  box(doc,80,36,34,4,CYAN,0,1.5);
  for(var j=0;j<7;j++) box(doc,78+j*6,25,4,5,DARK,1.2,0.8);      // 导轨齿

  // ===== 长护罩 / 枪管 =====
  box(doc,124,52,98,44,DARK,2.6,4);
  box(doc,128,56,90,10,BODY,1.8,2);           // 上盖
  box(doc,128,58,90,6,PART,0,1.5);
  box(doc,128,70,90,18,CYAN_D,1.6,2);         // 能量槽底座
  box(doc,130,73,86,12,CYAN,1.4,2);
  box(doc,132,75.5,82,6,WHITE,0,0);           // 能量槽高光
  for(var k=0;k<4;k++) box(doc,138+k*20,88,12,3,DARK,0,1);       // 底部散热缝
  // 加强箍
  box(doc,150,52,6,44,PART,1.6,1);
  box(doc,190,52,6,44,PART,1.6,1);

  // ===== 枪口制退器 =====
  box(doc,222,54,18,40,DARK,2.4,3);
  for(var m=0;m<3;m++) box(doc,225,58+m*11,12,4,BLACK,0,1);      // 制退口
  circ(doc,236,74,5,CYAN,1.6);

  // ===== 握把（带防滑槽）=====
  poly(doc,[[92,98],[112,98],[106,124],[86,124]],true,BLACK,2.4,PART);
  for(var g=0;g<3;g++) box(doc,93+g*1.0,106+g*5,16,2.5,DARK,0,1);

  // ===== 弹匣 =====
  box(doc,124,88,24,34,DARK,2.4,3);
  box(doc,128,94,16,3,PART,0,1);
  box(doc,128,102,16,3,PART,0,1);
  box(doc,128,110,16,3,PART,0,1);

  // ===== 扳机 + 护圈 =====
  poly(doc,[[112,96],[112,107],[121,111],[130,107],[130,96]],false,BLACK,2.2,null);
  box(doc,116,98,3,9,DARK,1.0,1);

  // ===== 拉机柄 =====
  box(doc,60,60,10,4,PART,1.4,1);
  box(doc,70,58,4,8,DARK,1.2,1);

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
