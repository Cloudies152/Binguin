# Assemblies 目录

**`Binguin.dll` 在 git 里**（compat：别人 clone 下来就能直接玩，不用自己编译）。

## 什么时候提交这个 DLL

作者 2026-10-10 的说明：

> **「不用每次都上传」** —— 意思是**别每次编译都跟着提交**，
> **不是"不要上传"**。

所以规矩是：

| 情况 | 要不要提交 DLL |
|---|---|
| 改了 C# 代码、编译过了 | ✅ **要**（否则别人拉到的是旧行为） |
| 只是改了 XML / 贴图 / 文案 | ❌ 不用（DLL 没变，提交它只是噪音） |
| 反复编译调试、还没定稿 | ❌ 不用（等定稿一起提交） |
| 准备发版 / 让人测试 | ✅ **一定要**（否则对方 clone 下来没 DLL，mod 加载不了） |

> ⚠️ **千万不要把它加进 `.gitignore`**。
> 曾经加过一次，结果是：别人 clone 下来**没有 DLL ⇒ 所有 C# 功能都没有**，
> 表现成一堆莫名其妙的症状（比如「没有装配按钮」），很难查。
> 已回滚。

## 编译

```powershell
# 从仓库根目录
dotnet build Source/Binguin/Binguin.Race.sln -c Release
```

产物先在 `Source/Binguin/bin/Release/net48/`，再自动复制到这里。
详见 [../Source/BUILDING.md](../Source/BUILDING.md)。

## 别手放这些东西

`.gitignore` 挡掉了 `Assemblies/*.pdb`。
游戏 dll / HAR dll **也不要**放这里（引用都是 `Private=false`，不会跟着复制）。
