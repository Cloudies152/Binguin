# 想帮忙？先看这里

本项目按**任务类型**分好了文档，请按你的专长挑一份：

| 你会什么 | 看哪份 |
|---|---|
| 🎨 会画画 | [任务/美术任务.md](任务/美术任务.md) |
| 💻 会 C# | [任务/程序任务.md](任务/程序任务.md) |
| 🌏 会外语 | [任务/翻译任务.md](任务/翻译任务.md) |
| 🧪 啥也不会，但想帮忙 | [任务/通用任务.md](任务/通用任务.md) |

**任务总入口（含"仓库里每个文件夹是什么"）→ [任务/README.md](任务/README.md)**

**完整协作者指南（安装、编译、交东西的方式、术语表）→ [任务/完整指南.md](任务/完整指南.md)**

---

## ⚠️ 动手前必读

**[任务/参考/铁规则.md](任务/参考/铁规则.md)** —— **5 条会直接弄坏 Mod 的坑**：

1. XML 里**绝对不能**出现本 Mod 的 C# 类型名
2. 不要用 `Harmony.PatchAll`
3. **不要"顺手补全"标了「借原版」的贴图**（那是故意的）
4. **不要改数值 / 玩法平衡**（改贴图和 UI 随便改）
5. 改之前先 `git pull`，别覆盖别人正在编辑的文件

---

## 看不懂哪个文件是哪个？

→ **[任务/参考/文件命名规则.md](任务/参考/文件命名规则.md)**

（为什么贴图叫 `_south` / `_north` / `_east`、为什么有两个 `Textures` 目录、
`Comp_` / `JobDriver_` / `Dialog_` 这些前缀是什么意思…都在里面）

---

## 💻 写 C# 的看这里：两种编译方式，随便挑

| 方式 | 怎么用 | 需要装什么 |
|---|---|---|
| **Visual Studio / Rider**（推荐） | 双击仓库根目录的 **`Binguin.Race.sln`** → 生成解决方案 | VS 2022 或 Rider |
| **命令行** | `dotnet build` | .NET SDK |
| **老办法** | `Source\build.ps1` | **什么都不用装**（靠系统自带 csc.exe） |

**三条路等价** —— 引用同一批 dll、输出同一个位置（`Assemblies\Binguin.dll`）。

> 🆕 **2026-10-10 新增**：`.sln` / `.csproj` / GitHub 自动编译。
> 以前只有 `build.ps1`（PowerShell），很多人不习惯，现在用 VS 打开就能编。
> 详细说明（含"游戏目录怎么指定"）→ [任务/参考/编译方式.md](任务/参考/编译方式.md)

### 编译完记得部署

`Assemblies\Binguin.dll` 要复制到游戏目录的对应位置才会生效：

```
...\RimWorld\Mods\Binguin_Race_Mod\Assemblies\Binguin.dll
```

VS 里可以加个生成后事件，或者用 `Tools\Deploy-BinguinToGame.ps1`。
也可以 `dotnet build -p:DeployToGame=true`（自动复制，见编译方式文档）。

---

## 交东西的方式

最省事：**GitHub 网页直接改**（贴图也能拖进去）

1. 打开要改的文件/目录 → 右上角铅笔图标（或 **Add file → Upload files**）
2. 改完在页面底部写一句说明
3. **Create a new branch and start a pull request**

不想碰 Git 也行：按文档里的**目录结构**整理好，压缩包发群里。
⚠️ **目录结构一定要对**，否则贴图挂不上。

---

仓库：https://github.com/Cloudies152/Binguin