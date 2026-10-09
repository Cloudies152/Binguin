# 冰鹅族 Binguin Race — 环世界 1.6 种族 MOD 设计与实现

> ## 🙋 是来帮忙的？
>
> **先看 → [CONTRIBUTING.md](CONTRIBUTING.md)（协作者指南）**
>
> 里面按 **美术 / 程序 / 翻译** 三块列了**能直接上手的任务**：
> - 怎么把 Mod 跑起来（clone 即玩，已自带编译好的 DLL）
> - 每张贴图要放哪、多大、什么名字
> - 两个程序员任务：**重置钓竿穿刺技能** + **重置制作钓竿 UI**
> - 交东西的方式（GitHub 网页改 / PR / 直接发压缩包）
> - ⛔ **会弄坏 Mod 的 5 个坑**（不看会白干）
>
> 贴图缺口清单（自动生成，**别手抄**）：[贴图需求总表.md](贴图需求总表.md)
>
> ⚠️ 本 README 是**设计与实现文档**（给改代码的人看）。只想帮忙做点事的话，看协作者指南就够。
>
> ---

一个基于 **Humanoid Alien Races 2.0（外星人框架）** 的环世界（RimWorld）1.6 种族 MOD：
企鹅娘种族「冰鹅族」，中立派系「冰鹅」，据点名为「XX观察站」（XX 取自屈原诗歌中的名词），
并带有开局三天内上门询问「共存意向」的外交事件。

---

## 1. 种族设定：冰鹅族（企鹅娘）

- **外观**：体型走原版 Human 默认（不覆盖 `baseBodySize`，即 **1.0**）；
  **肤色恒为纯白**（`Race_Binguin.xml` 的 `colorChannels/skin` first+second 都写 `(1,1,1)`，
  再由 `Source/Compat_BinguinSkinAndFace.cs` 打 `Pawn_StoryTracker.get_SkinColor` 补丁**锁死**，
  任何肤色基因都改不动）；性别比 9:1 女:男（`maleGenderProbability=0.1`）。
  > 视觉身高另有一套：自绘身体贴图按原版 512 画布对齐，`Tools/Make-BinguinBodyTextures.py`
  > 的 `SCALE = 1.04`（比原版高 4%，脚底线不变）——**那是贴图缩放，不是实体型**，别和上面混。
- **耐性**：舒适温度 -45℃ ~ 25℃，极耐寒、略怕热——寒原观测者的设定。
- **食性**：杂食（企鹅娘是"带企鹅特征的人类"，不必像真企鹅那样只吃肉）。
- **生物特性**：`bloodDef=Filth_Blood`（1.5 起必须显式指定才能流血/治伤）、`renderTree=Humanlike`、
  `gestationPeriodDays=18`（与人类一致的妊娠期）、`humanRecipeImport=true`（继承全部人类手术配方）。
- **特长**：定居者/领袖/外交官模板偏重 `Social / Intellectual / Research`（观测、记录、交涉的职业气质）。

## 2. 派系设定：冰鹅

- 中立定居型派系，科技等级 **Industrial**（观测站科技），开局必生成据点（1~3 个）。
- 领袖头衔「首席观察员」，据点显示名「观察站」。
- 成员：共 **13** 个 PawnKindDef（Defs/PawnKinds_Binguin.xml）——定居者 / 外交官 / 商人 / coser×2（⑨服·大酱服）/ 商队领袖 / 民兵 / 士兵 / **工兵** / 哨骑 / 领袖护卫 / 领袖 / 巡护员；
  全部通过 `<race>Binguin</race>` 指定为冰鹅族，含 1.4+ 必填字段 `initialResistanceRange`；武器用专属 weaponTags 精确指定（见 `Defs/PawnKinds_Binguin.xml`）。

## 3. 安装与编译

### 安装
1. 前置：Steam 创意工坊订阅 **Harmony**（官方页工坊 id 2009463077，0Harmony 2.x / HarmonyLib）与
   **Humanoid Alien Races 2.0**（工坊 id 839005762）。
2. 将整个 `Binguin_Race_Mod` 文件夹复制到 `RimWorld/Mods/` 下。
3. 游戏内「Mods」列表启用本 MOD（排在 HAR2 之后，`loadAfter` 已声明）。

### 编译 C#
```powershell
cd Binguin_Race_Mod\Source
.\build.ps1 -GameDir "你的 RimWorld 安装目录"
```
> 使用 Windows 自带的 .NET Framework csc.exe，无需 VS。产物为 `Assemblies\Binguin.dll`。
> 仓库里**已带编译好的 DLL**，clone 下来直接玩即可；只有改了 `Source/*.cs` 才需要重新编译。

## 4. 兼容性说明（全部 DLC）

| DLC | 说明 |
|---|---|
| Royalty | 种族继承 Human，可正常授予头衔；如需更多定制可查 HAR 的 Royalty-Compatibility 页 |
| Ideology | 无冲突；造型台可自定义头发等外观（★ 早期的鸟喙附加件 `bodyAddons` 已按用户要求移除） |
| Biotech | 已配置 `gestationPeriodDays`、`humanRecipeImport`、`maleGenderProbability`；可用 `raceGenes` 加种族基因 |
| Anomaly | 无冲突；事件使用原生好感 API，不会被异常体事件干扰（活铁 Bioferrite = 用户口中的「异铁」） |
| Odyssey | 钓鱼/鱼/水域/真空抗性等内容的载体：进阶与高级钓鱼学、鱼池、蟹笼、打窝用具全部放在
   `AdvancedFishing/` 并由 **LoadFolders.xml** 按 Odyssey 门控；威灵套真空抗性、企鹅飞踢音效等
   用 `MayRequire="Ludeon.RimWorld.Odyssey"` 就地门控，未装 DLC 不加载、不报错 |
