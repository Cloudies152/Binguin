冰鹅族美术资源说明（占位，依据 HAR 官方 Wiki：Graphic-Paths / Body-Addons 页）
================================================================================

本 MOD 目前仅含 About 预览图（脚本 Tools/Make-BinguinPlaceholderTextures.ps1 生成）：
种族继承 Human 的基础外观 + HAR 配色通道（企鹅白肤色）。
鸟喙附加件已按用户要求移除（2026-08-17）。仍可正常加载游玩。要做出完整的
「企鹅娘」外观，按以下约定补图：

1) 身体贴图（可选，不补则用人类体型 + 白色肤色）
   HAR 默认身体路径为 Things/Pawn/Humanlike/Bodies/（文件名由 BodyTypeDef 决定）。
   自定义路径在种族 XML 的 <alienRace><graphicPaths> 里声明：
     <graphicPaths>
       <body>Things/Pawn/Body/Binguin/</body>
       <bodyMasks>Things/Pawn/Body/Binguin/Masks/</bodyMasks>   <!-- 可选，颜色蒙版 -->
       <head>Things/Pawn/Head/Binguin/</head>
       <headMasks>Things/Pawn/Head/Binguin/Masks/</headMasks>   <!-- 可选 -->
     </graphicPaths>
   蒙版规则：红色 = 通道第一色，绿色 = 第二色，黑色 = 不上色。

2) 鸟喙附加件（已移除；如需恢复见《会话交接文档》§4.4 的约定）
   附加件贴图用 Graphic_Multi 加载器，需要方向贴图（缺 _west 时自动镜像 _east）：
     Textures/Things/Pawn/Addons/Binguin/beak_north.png
     Textures/Things/Pawn/Addons/Binguin/beak_south.png
     Textures/Things/Pawn/Addons/Binguin/beak_east.png
     Textures/Things/Pawn/Addons/Binguin/beak_west.png   （可选）
   参考画布 256x256、内容居中；drawSize=1 时内容约占 0.25~0.28 格。
   ★ 彩色贴图（橙色喙）不写 colorChannel——写了会被通道颜色蒙版盖掉；
   若想用肤色蒙版（如白色喙/耳朵），把贴图做成黑/灰蒙版并加 <colorChannel>skin</colorChannel>。

3) 图标（已完成占位版）
   About/Preview.png   MOD 列表预览图（512x512，脚本生成；可换正式图）

推荐工作流：
  - 参照 Steam 创意工坊上成熟 HAR2 种族 MOD 的贴图目录结构；
  - 身体/头贴图尺寸与环世界人类贴图一致（约 128x128 每方向）；
  - 做完贴图后用 HugsLib 日志确认没有贴图加载错误。
