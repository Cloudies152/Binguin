#target illustrator
// v20 —— 严格按豆包给的规范实现
// 色板：描边 #1A1A1A / 深灰 #3A424A / 浅灰 #E8ECF0 / 结构蓝 #4888B2 / 能量蓝 #54C8F0 / 内发光 #86E0FF / 阴影 #282E34
// 线宽：外轮廓 3pt，内部 1.5pt；高光白 2~4px 无透明度；阴影 #282E34 只在大的块右下 2~3px
// 比例：尾仓 18% / 机匣 42% / 枪管 30%；机匣上壳:下壳 = 1.6:1
function C(r,g,b){ var c=new RGBColor(); c.red=r; c.green=g; c.blue=b; return c; }
var H=128;

var K1 = C(26,26,26);      // #1A1A1A 描边
var DG = C(58,66,74);      // #3A424A 深灰壳体
var LG = C(232,236,240);   // #E8ECF0 浅灰壳体
var SB = C(72,136,178);    // #4888B2 结构蓝
var EB = C(84,200,240);    // #54C8F0 能量蓝
var EB2= C(134,224,255);   // #86E0FF 内发光
var SH = C(40,46,52);      // #282E34 阴影
var WH = C(255,255,255);   // 高光白

var LW_OUT = 3.0;   // 外轮廓
var LW_IN  = 1.5;   // 内部线

function box(doc,x,y,w,h,fill,lw,r){
  var t=H-y;
  var p=(r&&r>0)?doc.pathItems.roundedRectangle(t,x,w,h,r,r):doc.pathItems.rectangle(t,x,w,h);
  p.filled=true; p.fillColor=fill;
  if(lw!==0){p.stroked=true;p.strokeColor=K1;p.strokeWidth=(lw||LW_IN);}else{p.stroked=false;}
  return p;
}
function poly(doc,pts,closed,color,w,fill){
  var a=[];for(var i=0;i<pts.length;i++)a.push([pts[i][0],H-pts[i][1]]);
  var p=doc.pathItems.add();p.setEntirePath(a);p.closed=closed;
  if(fill){p.filled=true;p.fillColor=fill;}else{p.filled=false;}
  if(w>0){p.stroked=true;p.strokeColor=color;p.strokeWidth=w;
    p.strokeCap=StrokeCap.BUTTENDCAP;p.strokeJoin=StrokeJoin.ROUNDENDJOIN;}
  return p;
}
// 高光条（白，无透明度）
function hi(doc,x,y,w,h){ box(doc,x,y,w,h,WH,0,0.5); }
// 右下阴影
function sh(doc,x,y,w,h){ box(doc,x,y,w,h,SH,0,0.5); }
// 螺丝凹槽
function screw(doc,x,y,s){
  box(doc,x,y,s,s,DG,LW_IN,1.2);
  box(doc,x+1,y+1,s-2,s-2,SH,0,0.8);
}

function main(){
  var doc=app.documents.add(DocumentColorSpace.RGB,256,128);
  var Y0=44, Y1=86;     // 机匣上下沿
  var UH=26, LH=16;     // 上壳 26 / 下壳 16 ≈1.6

  // ===== 1) 尾仓储能仓（x8-51, 18%）=====
  box(doc, 8, Y0, 43, 46, DG, LW_OUT, 2);
  // 小切角
  poly(doc,[[8,Y0],[8,Y0+46],[14,Y0+46],[8,Y0+40]],true,K1,0,SH);
  // 横向分割线（暗示储能舱分层）
  box(doc, 10, Y0+22, 39, LW_IN, DG, LW_IN, 0);
  hi(doc, 11, Y0+2.5, 37, 3);
  sh(doc, 10, Y0+42, 39, 2.5);

  // ===== 2) 机匣（x51-152, 42%）=====
  box(doc, 51, Y0, 101, UH+LH, DG, LW_OUT, 2.5);
  // 上壳体（浅灰镶板）
  box(doc, 54, Y0+2, 95, UH-4, LG, LW_IN, 1.5);
  hi(doc, 56, Y0+3.5, 91, 3);
  // 上壳与下壳分界
  box(doc, 53, Y0+UH, 97, LW_IN, DG, LW_IN, 0);

  // 能量槽（内嵌，缩在上壳里）
  box(doc, 60, Y0+9, 78, 12, DG, LW_IN, 1.2);
  box(doc, 61.5, Y0+10.5, 75, 9, EB, 0, 1);
  box(doc, 64.5, Y0+13, 69, 4, EB2, 0, 0.8);      // 内发光（缩小居中）

  // 侧面螺丝凹槽（3 个）
  screw(doc, 56, Y0+24, 5);
  screw(doc, 66, Y0+24, 5);
  screw(doc, 138, Y0+24, 5);

  // ===== 3) 瞄具（扁平，贴合机匣顶，高≤6px）=====
  box(doc, 96, Y0-6, 30, 6, DG, LW_IN, 1.2);
  box(doc, 99, Y0-4.6, 24, 3.2, EB, 0, 0.6);

  // ===== 4) 主握把（垂直实心，复刻 DP12 语言）=====
  box(doc, 74, Y1, 20, 30, SB, LW_OUT, 3);
  hi(doc, 76, Y1+2, 5, 24);
  sh(doc, 74+16, Y1+4, 3, 24);

  // ===== 5) 护木（x152-196）=====
  box(doc, 152, Y0+6, 44, 30, LG, LW_OUT, 2);
  hi(doc, 154, Y0+8, 40, 3);
  // 4 个指示灯收进凹槽
  for(var i=0;i<4;i++){
    box(doc, 157+i*10, Y0+16, 7, 9, DG, LW_IN, 1);
    box(doc, 158.2+i*10, Y0+17.2, 4.6, 6.6, EB, 0, 0.6);
  }
  // 导轨短线
  for(var r2=0;r2<4;r2++) box(doc, 158+r2*10, Y0+9.5, 6, 1.5, K1, 0, 0);

  // ===== 6) 前握把（机匣前端下方）=====
  box(doc, 160, Y1-2, 16, 24, SB, LW_OUT, 3);
  hi(doc, 162, Y1+1, 4, 18);

  // ===== 7) 能量枪管（逐渐收窄 + 根部环 + 末端 1 个能量环）=====
  poly(doc,[[196,Y0+10],[230,Y0+14],[230,Y0+26],[196,Y0+28]],true,K1,LW_OUT,DG);
  hi(doc, 199, Y0+12, 28, 2.4);
  box(doc, 196, Y0+8, 4, 22, DG, LW_IN, 1);       // 根部环
  box(doc, 228, Y0+13, 6, 15, DG, LW_IN, 1);      // 末端环
  box(doc, 229, Y0+16, 4, 9, EB, 0, 0.6);

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
