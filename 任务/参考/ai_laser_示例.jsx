#target illustrator
// 激光加特林 256x128 —— 屏幕坐标（y 向下），内部自动转换为 Illustrator 坐标（y 向上）
function C(r,g,b){ var c=new RGBColor(); c.red=r; c.green=g; c.blue=b; return c; }

var BLACK   = C(16,18,22);
var GRAYBLUE= C(64,76,89);
var BLUE    = C(103,132,155);
var WHITE   = C(224,229,234);
var CYAN    = C(127,216,240);
var CYAN_D  = C(70,150,180);
var T_TOP   = C(178,190,202);
var T_BOT   = C(32,40,50);

var H = 128;   // 画布高，用于翻转 y

// 屏幕坐标：x,y 为左上角
function box(doc, x, y, w, h, fill, lw, radius){
  var topAI = H - y;                     // 屏幕顶部 y -> Illustrator 顶部
  var p;
  if (radius && radius > 0) p = doc.pathItems.roundedRectangle(topAI, x, w, h, radius, radius);
  else                      p = doc.pathItems.rectangle(topAI, x, w, h);
  p.filled = true; p.fillColor = fill;
  p.stroked = (lw !== 0);
  if (lw !== 0) { p.strokeColor = BLACK; p.strokeWidth = (lw === undefined ? 2.2 : lw); }
  return p;
}

function circ(doc, cx, cy, r, fill, lw){
  var p = doc.pathItems.ellipse(H - cy + r, cx - r, r*2, r*2);
  p.filled = true; p.fillColor = fill;
  if (lw !== 0) { p.stroked = true; p.strokeColor = BLACK; p.strokeWidth = (lw === undefined ? 2.2 : lw); }
  return p;
}

function seg(doc, x1, y1, x2, y2, color, w){
  var p = doc.pathItems.add();
  p.setEntirePath([[x1, H - y1], [x2, H - y2]]);
  p.closed = false; p.filled = false; p.stroked = true;
  p.strokeColor = color; p.strokeWidth = w;
  p.strokeCap = StrokeCap.ROUNDENDCAP; p.strokeJoin = StrokeJoin.ROUNDENDJOIN;
  return p;
}

function poly(doc, pts, closed, color, w, fill){
  var a = [];
  for (var i = 0; i < pts.length; i++) a.push([pts[i][0], H - pts[i][1]]);
  var p = doc.pathItems.add();
  p.setEntirePath(a);
  p.closed = closed;
  if (fill) { p.filled = true; p.fillColor = fill; } else { p.filled = false; }
  p.stroked = true; p.strokeColor = color; p.strokeWidth = w;
  p.strokeCap = StrokeCap.ROUNDENDCAP; p.strokeJoin = StrokeJoin.ROUNDENDJOIN;
  return p;
}

function main(){
  var doc = app.documents.add(DocumentColorSpace.RGB, 256, 128);

  // ===== 尾部散热片 =====
  box(doc, 62, 50, 26, 28, GRAYBLUE, 2.2, 3);
  for (var i = 0; i < 3; i++) box(doc, 65, 54 + i*7, 11, 4, BLUE, 1.4, 1);

  // ===== 六管：上亮 / 中中 / 下暗 =====
  box(doc, 146, 36, 60, 15, T_TOP, 2.6, 2.8);
  seg(doc, 149, 38.6, 203, 38.6, C(214,224,234), 1.6);
  box(doc, 146, 77, 60, 15, T_BOT, 2.6, 2.8);
  seg(doc, 149, 79.6, 203, 79.6, C(96,108,122), 1.6);
  box(doc, 146, 53, 62, 20, BLUE, 2.6, 2.8);            // 中管压上层
  seg(doc, 149, 56.6, 205, 56.6, C(150,168,182), 1.8);

  // ===== 机匣 =====
  box(doc, 84, 46, 62, 34, GRAYBLUE, 2.6, 3.5);
  box(doc, 87, 48, 56, 6, WHITE, 0, 1.5);                // 顶面白条
  box(doc, 116, 56, 26, 20, BLUE, 2.0, 2);               // 侧面分块

  // ===== 能量电池 =====
  box(doc, 92, 56, 18, 20, CYAN_D, 2.2, 2);
  box(doc, 95, 59, 12, 14, CYAN, 1.6, 2);
  seg(doc, 97, 63, 105, 63, C(228,251,255), 1.4);

  // ===== 提把 =====
  box(doc, 98, 26, 40, 7, GRAYBLUE, 2.2, 2.5);
  box(doc, 102, 33, 7, 13, GRAYBLUE, 2.0, 1.5);
  box(doc, 127, 33, 7, 13, GRAYBLUE, 2.0, 1.5);

  // ===== 握把 + 扳机护圈 =====
  box(doc, 108, 80, 20, 32, BLUE, 2.2, 3);
  poly(doc, [[122,80],[122,92],[130,96],[138,92],[138,80]], false, BLACK, 2.2, null);

  // ===== 枪口环 + 冰蓝辉光 =====
  box(doc, 206, 32, 13, 64, GRAYBLUE, 2.6, 3.6);
  circ(doc, 212.5, 64, 9.5, CYAN, 1.8);
  circ(doc, 212.5, 64, 3.6, C(232,252,255), 0);

  doc.artboards[0].artboardRect = [0, 128, 256, 0];

  var o = new ExportOptionsPNG24();
  o.artBoardClipping = true; o.antiAliasing = true; o.transparency = true;
  doc.exportFile(new File("W:/dsh/zengbing/Tools/laser_ai.png"), ExportType.PNG24, o);

  var o2 = new ExportOptionsPNG24();
  o2.artBoardClipping = true; o2.antiAliasing = true; o2.transparency = true;
  o2.horizontalScale = 200; o2.verticalScale = 200;
  doc.exportFile(new File("W:/dsh/zengbing/Tools/laser_ai_2x.png"), ExportType.PNG24, o2);

  doc.close(SaveOptions.DONOTSAVECHANGES);
  return "ok";
}
main();
