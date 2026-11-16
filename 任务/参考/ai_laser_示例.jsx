#target illustrator
// 激光机枪 v4 —— 单管设计，参考 Vortex_Beater 的剪影与配色
// 256x128，屏幕坐标（y 向下）
function C(r,g,b){ var c=new RGBColor(); c.red=r; c.green=g; c.blue=b; return c; }
var H = 128;

var BLACK = C(12,14,18);
var NAVY  = C(34,45,58);      // 主体深海军蓝
var NAVY2 = C(40,52,66);      // 次级块
var GRAY  = C(58,70,84);      // 灰件
var GRAY2 = C(88,100,114);
var MINT  = C(95,227,208);    // 薄荷青（参照图强调色）
var MINT2 = C(140,245,230);
var MUZ   = C(16,20,26);

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

  // ===== 枪托（后部斜切）=====
  poly(doc,[[18,64],[46,58],[56,58],[56,96],[30,100]],true,BLACK,2.4,NAVY);
  box(doc,24,70,20,6,MINT,0,0);              // 托上能量条

  // ===== 机匣 =====
  box(doc,54,54,68,44,NAVY,2.6,4);
  box(doc,58,58,60,7,NAVY2,0,2);             // 顶面
  // 侧面凹槽 + 竖排能量格
  box(doc,62,66,22,26,NAVY2,2.0,2);
  for(var i=0;i<3;i++) box(doc,65+i*6.5,70,4,18,MINT,1.2,1);

  // ===== 顶部电池（横向）=====
  box(doc,76,32,44,20,NAVY2,2.4,3);
  box(doc,80,36,36,8,MINT,1.6,2);
  box(doc,80,46,36,3,GRAY,0,1);

  // ===== 长枪管 + 护罩 =====
  box(doc,122,56,102,34,GRAY,2.6,4);
  box(doc,126,60,94,10,NAVY,0,2);            // 上沿
  box(doc,130,72,86,12,MINT,1.6,2);          // 长能量条
  box(doc,130,74.5,86,7,MINT2,0,0);          // 高光
  // 护罩散热缝
  for(var j=0;j<4;j++) box(doc,140+j*18,86,10,3,NAVY,0,1);
  // 枪口
  box(doc,224,58,16,30,MUZ,2.4,3);
  circ(doc,236,73,6,MINT,1.8);

  // ===== 握把 =====
  poly(doc,[[90,98],[110,98],[106,124],[86,124]],true,BLACK,2.4,NAVY2);

  // ===== 弹匣（下方）=====
  box(doc,122,90,22,32,NAVY2,2.4,3);
  box(doc,126,96,14,4,MINT,1.2,1);
  box(doc,126,104,14,4,MINT,1.2,1);

  // ===== 扳机护圈 =====
  poly(doc,[[110,96],[110,106],[118,110],[126,106],[126,96]],false,BLACK,2.2,null);

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
