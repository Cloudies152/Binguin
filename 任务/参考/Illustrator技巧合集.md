# 📚 Illustrator 技巧合集（学习笔记）

> 来源：Adobe 官方脚本指南（docsforadobe）、Adobe 社区、以及本项目实测。
> 标注 **✅ 实测可用** / **❌ 实测无效** / **❓ 未验证**。

---

## 1. 布尔运算（Pathfinder）✅

ExtendScript **没有** Pathfinder 的对象 API，只能走菜单命令，而且需要先成组：

```javascript
doc.selection = null;
a.selected = true; b.selected = true; c.selected = true;
app.executeMenuCommand("group");                  // ★ 必须先成组
app.executeMenuCommand("Live Pathfinder Subtract"); // 减（注意是 Live 版）
app.executeMenuCommand("expandStyle");            // ★ 展开成普通路径
app.executeMenuCommand("ungroup");
```

其他可用命令名：
`Live Pathfinder Add` / `Intersect` / `Exclude` / `Minus Back` / `Divide` / `Trim` / `Merge` / `Crop`

> 实测：不 `group` 直接 Subtract **无效**；不 `expandStyle` 结果留在效果里，导出可能是空的。

---

## 2. 渐变 ✅（但有坑）

```javascript
var g = doc.gradients.add();
g.type = GradientType.LINEAR;          // 或 RADIAL
g.gradientStops[0].color = C(40,50,62);
g.gradientStops[1].color = C(96,112,128);

var gc = new GradientColor();
gc.gradient = g;
gc.origin      = [x, yTop];            // ★ 用 origin/destination 定方向
gc.destination = [x, yBottom];
path.fillColor = gc;
```

> ⚠️ **`GradientColor.angle` 是坏的**（官方文档明确写了自 2008 年起的已知 bug）。
> 想竖向渐变就靠 origin/destination 的坐标差，别指望 angle。

---

## 3. 剪切蒙版 ❌（`clipped` 不生效）

官方文档有 `GroupItem.clipped`，但实测：

```javascript
var grp = doc.groupItems.add();
target.moveToEnd(grp);   // 底层：被裁对象
mask.moveToEnd(grp);     // 顶层：蒙版形状
grp.clipped = true;      // ❌ 实测没有裁切效果
```

**替代方案（推荐）**：
1. 菜单命令：选中两者 → `app.executeMenuCommand("make clipping mask")`（❓待验证）
2. 或者**不用蒙版**：把高光画成内缩的子形状（我们目前的做法），效果够用

---

## 4. 混合模式与透明度 ✅

```javascript
item.opacity = 45;                        // 0-100
item.blendingMode = BlendModes.MULTIPLY;  // 正片叠底，做阴影/高光叠加很好用
```

可用模式：`NORMAL MULTIPLY SCREEN OVERLAY SOFTLIGHT HARDLIGHT COLORDODGE COLORMULTIPLY DARKEN LIGHTEN`

> 用法：在暗色部件上叠一层白色、`opacity 20`、`SCREEN` → 得到自然的提亮，比硬贴亮色块自然。

---

## 5. 效果与外观 ❓

- `app.executeMenuCommand("expandStyle")` —— 展开外观/效果（布尔后必须）
- 投影/内阴影/AI 里那种柔和效果属于"效果"，脚本可加但参数复杂，**暂时不用**
- 用渐变 + 混合模式模拟柔和过渡，比加效果稳

---

## 6. 符号 / 画刷 ❓

```javascript
var sym = doc.symbols.add(pageItem);          // 定义符号
var si  = doc.symbolItems.add(sym);           // 放置实例
```
- 用途：螺栓、铆钉、栅格统一复用
- 但**小尺寸下这些细节会变噪点**（本项目已踩过：2px 螺栓像脏点）→ 慎用

---

## 7. 测量与定位（很实用）✅

```javascript
item.geometricBounds   // [left, top, right, bottom]
item.visibleBounds     // 含描边
item.width / item.height / item.left / item.top
item.translate(dx, dy) // 相对移动
item.zOrder(ZOrderMethod.BRINGTOFRONT)
```

---

## 8. 设计配方（把技术用对地方）

### 8.1 柔和体积（替代硬分段）
1. 基础形状填充**渐变**（暗→亮，origin/destination 沿短边方向）
2. 顶部叠一条 **SCREEN + 20~30% 白** 的窄条（贴边）
3. 底部叠一条 **MULTIPLY + 20% 深色** 的窄条
→ 得到接近 AI 绘画的柔和明暗

### 8.2 真镂空（散热孔/扳机护圈）
1. 画主体
2. 画"洞"的形状（同位置）
3. group → Live Pathfinder Subtract → expandStyle → ungroup
→ 洞是真的透明，背景色能透出来

### 8.3 描边策略
- 外轮廓统一 `1.8–2.6px`，颜色 `#12161C`
- **不要**给每个零件都描边（会变成"积木感"）；只描主要分块
- 细节（槽、缝）用**无描边**的深色块表示

### 8.4 明度分配（重要）
- 整枪 3 个明度层：亮（高光/白条）、中（主体）、暗（枪管/阴影）
- 一个部件最多 3 档，多了就糊
- 强调色（冰蓝）**只出现 1~2 处**，用于"能量"焦点

---

## 9. 还没验证、下次要试的

| 项目 | 目的 |
|---|---|
| `make clipping mask` 菜单命令 | 高光贴合轮廓 |
| `app.executeMenuCommand("Make Blend")` | 两个形状间自动渐变（做圆柱 highlight） |
| 渐变 3 个以上色标 | 更自然的过渡 |
| `BlendModes.SCREEN + opacity` 叠层高光 | 柔和提亮 |
| SVG 参考图反向学习 | 打开成熟图标 dump 结构 |

---

## 10. 结论（上一版为什么更丑）

v7 用了渐变，但：
1. 渐变方向由 `angle` 决定（**这个 API 是坏的**）→ 实际方向不对，体积感乱
2. 每个部件都描边 + 分段过多 → 视觉噪音
3. 高光带太宽 → 像贴了条胶带

**下一版（v8）按 §8 配方重做：origin/destination 控方向、SCREEN/MULTIPLY 叠层、只描主轮廓、强调色限量。**
