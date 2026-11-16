#target illustrator
// 技术验证 2：修布尔与剪切蒙版
function C(r,g,b){ var c=new RGBColor(); c.red=r; c.green=g; c.blue=b; return c; }

function main(){
  var doc = app.documents.add(DocumentColorSpace.RGB, 200, 200);
  var log = [];

  // --- 1. 布尔：先 group 再 Subtract ---
  var base = doc.pathItems.rectangle(180, 20, 80, 80);
  base.filled = true; base.fillColor = C(64,76,89); base.stroked = true; base.strokeWidth = 2;
  var h1 = doc.pathItems.rectangle(170, 35, 22, 12);
  h1.filled = true; h1.fillColor = C(64,76,89); h1.stroked = true; h1.strokeWidth = 2;
  var h2 = doc.pathItems.rectangle(150, 35, 22, 12);
  h2.filled = true; h2.fillColor = C(64,76,89); h2.stroked = true; h2.strokeWidth = 2;

  doc.selection = null;
  base.selected = true; h1.selected = true; h2.selected = true;
  app.executeMenuCommand("group");                 // ★ 先成组
  app.executeMenuCommand("Live Pathfinder Subtract");
  app.executeMenuCommand("expandStyle");
  app.executeMenuCommand("ungroup");
  log.push("paths after boolean=" + doc.pathItems.length);

  // --- 2. 剪切蒙版 ---
  var target = doc.pathItems.roundedRectangle(180, 110, 70, 70, 12, 12);
  target.filled = true; target.fillColor = C(103,132,155); target.stroked = false;

  var hl = doc.pathItems.ellipse(120, 108, 100, 45);
  hl.filled = true; hl.fillColor = C(245,250,255); hl.stroked = false;
  hl.opacity = 50;

  var grp = doc.groupItems.add();
  target.moveToEnd(grp);      // 先放入（底层）
  hl.moveToEnd(grp);          // 后放入（顶层 = 蒙版）
  grp.clipped = true;
  log.push("group items=" + grp.pageItems.length + " clipped=" + grp.clipped);

  doc.artboards[0].artboardRect = [0, 200, 200, 0];
  var o = new ExportOptionsPNG24();
  o.artBoardClipping = true; o.antiAliasing = true; o.transparency = true;
  doc.exportFile(new File("W:/dsh/zengbing/Tools/tech_test.png"), ExportType.PNG24, o);
  doc.close(SaveOptions.DONOTSAVECHANGES);
  return log.join(" | ");
}
main();
