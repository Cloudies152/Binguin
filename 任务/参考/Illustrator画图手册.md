# 🅰️ Illustrator 2022 画图手册（脚本化）

> 用 ExtendScript 驱动 Illustrator 画贴图，再导出 PNG。本项目的武器/炮塔/物品图标都走这条路。
> 痛点全部来自实战，照抄即可。

---

## 1. 环境与调用方式

```powershell
$app = New-Object -ComObject Illustrator.Application
$app.UserInteractionLevel = -1        # 必须：抑制弹窗，否则脚本会卡在对话框
$app.DoJavaScriptFile('W:\...\xxx.jsx', [Type]::Missing, [Type]::Missing)
```

- **直接双击 .jsx 或给 Illustrator 传参数不会执行脚本**，必须用 COM 的 `DoJavaScriptFile`
- 脚本第一行写 `#target illustrator`
- 路径一律用正斜杠 `/`（`new File("W:/...")`）

---

## 2. 坐标系统（最容易踩的坑）

- Illustrator 的 **y 轴朝上**，原点在画布左下角
- 我们画图习惯"屏幕坐标"（y 朝下），所以要翻转：

```javascript
var H = 128;                        // 画布高
var topAI = H - y;                  // 屏幕 y(左上角) -> Illustrator top
doc.pathItems.rectangle(topAI, x, w, h);   // 注意参数顺序：top, left, width, height
```

> 我第一版激光加特林画出来上下颠倒，就是这里没翻转。

---

## 3. 常用 API 速查

| 目的 | 写法 |
|---|---|
| 矩形 | `doc.pathItems.rectangle(top, left, width, height)` |
| 圆角矩形 | `doc.pathItems.roundedRectangle(top, left, w, h, rx, ry)` |
| 椭圆 | `doc.pathItems.ellipse(top, left, w, h)`（不是圆心！） |
| 任意路径 | `var p = doc.pathItems.add(); p.setEntirePath([[x1,y1],[x2,y2],...])` |
| 填充/描边 | `p.filled = true; p.fillColor = C(r,g,b); p.stroked = true; p.strokeColor = ...; p.strokeWidth = n` |
| 封闭路径 | `p.closed = true` |
| 线帽 | `p.strokeCap = StrokeCap.ROUNDENDCAP`（或 `BUTTENDCAP` / `PROJECTINGENDCAP`） |
| 拐角 | `p.strokeJoin = StrokeJoin.ROUNDENDJOIN`（或 `BEVELENDJOIN` / `MITERENDJOIN`） |
| 圆点 | `doc.pathItems.ellipse(cy+r, cx-r, r*2, r*2)` |
| 复制 | `p.duplicate()` |
| 移动 | `p.translate(dx, dy)` |
| 旋转 | `p.rotate(deg)`（顺时针为正） |

> ⚠️ 枚举名是 **`ROUNDENDJOIN`**，不是 `ROUNDJOIN`（写错会报 `Error 1320: Invalid enumeration value`）。

---

## 4. 图层顺序 = 创建顺序

后创建的对象在**上层**。画"中间那根管压住上下两根"时，就要**最后**创建中间那根。

推荐顺序：
1. 底层挂件（弹链/支架）
2. 主体大形
3. 结构分块
4. 高光/白条
5. 细节
6. 描边强调（可选，最后再描一遍）

---

## 5. 导出 PNG

```javascript
doc.artboards[0].artboardRect = [0, 128, 256, 0];   // ★ 必须显式设画板，否则导出尺寸不对

var o = new ExportOptionsPNG24();
o.artBoardClipping = true;    // ★ 按画板裁切
o.antiAliasing = true;
o.transparency = true;        // 保留透明背景

doc.exportFile(new File("W:/out.png"), ExportType.PNG24, o);          // 1x
o.horizontalScale = 200; o.verticalScale = 200;                        // 2x 预览
doc.exportFile(new File("W:/out_2x.png"), ExportType.PNG24, o);
```

- `artboardRect` 必须设，否则是按内容包围盒导出，尺寸会飘
- 文件名**不要带扩展名**也可以（会自动补 `.png`），但带上更稳

---

## 6. 颜色

```javascript
function C(r,g,b){ var c = new RGBColor(); c.red=r; c.green=g; c.blue=b; return c; }
```

| 用途 | 色值 |
|---|---|
| 描边 | `#101216` |
| 机身（武器） | `#404C59` |
| 部件（护木/握把） | `#67849B` |
| 高光 | `#E0E5EA` |
| 冰蓝能量 | `#7FD8F0` / 暗 `#4696B4` |
| 炮塔机身（小） | `#706040`；枪管 `#808080`；机械件 `#606060` |
| 炮塔机身（大） | `#4A4A4A`；橄榄带 `#506050` |

---

## 7. 画法要点（可读性优先）

1. **先定剪影**：把整把枪涂黑，能认出是什么武器才算过
2. **明度分档**：一个材质最多 3 档（亮/中/暗），不要渐变
3. **描边统一**：整套图线宽一致（本套 1.6–2.6）
4. **细节预算**：64px 图标留 4–6 个次要细节，128px 可以多一点
5. **单一焦点**：冰蓝电池/枪口环这类强调色只出现 1–2 处
6. **留白**：四周至少 4px 透明边

---

## 8. 常见错误对照

| 报错/现象 | 原因 | 解决 |
|---|---|---|
| `Error 1320: Invalid enumeration value` | 枚举名写错（`ROUNDJOIN`） | 用 `ROUNDENDJOIN` |
| 导出空白 | 没设 `artboardRect` | 显式设画板 |
| 导出尺寸不对 | `artBoardClipping=false` | 设 `true` |
| 图上下颠倒 | y 轴未翻转 | `top = H - y` |
| 脚本不执行 | 用命令行传参 | 用 COM `DoJavaScriptFile` |
| 卡在对话框 | 没设 `UserInteractionLevel = -1` | 加上 |
| 对象被后画的盖住 | 图层顺序 | 调整创建顺序 |

---

## 9. 工作流

1. 写/改 `Tools/ai_*.jsx`
2. PowerShell 调 COM 执行
3. 看导出的 `*_2x.png` 预览（放大会清楚）
4. 和同系列并排对比（`Tools/*_compare.png`）
5. 满意后拷进 `Textures/...` → `Deploy-BinguinToGame.ps1`

---

## 10. 现有脚本

| 脚本 | 内容 |
|---|---|
| `Tools/ai_m110.jsx` | M110 结构线稿 |
| `Tools/ai_m870.jsx` | M870 结构线稿 |
| `Tools/ai_turrets.jsx` | 炮塔顶部线稿 |
| `Tools/ai_items.jsx` | 物品线稿（罐头/冰岩/仿生眼） |
| `Tools/ai_laser.jsx` | 激光加特林（成品级，含坐标翻转） |
| `Tools/ai_styleA.jsx` / `ai_dp12.jsx` | 早期风格试验 |
