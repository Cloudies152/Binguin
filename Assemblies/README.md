# Assemblies 目录

**这里的 `Binguin.dll` 是编译产物，不进 git。**

## 为什么

作者 2026-10-10 决定：**编译输出留在本地就好**，不往仓库里塞二进制文件。
好处是仓库里不再每次编译都产生一个 364 KB 的二进制 diff，评审和合并都干净。

## 那 clone 下来怎么玩

需要**自己编译一次**（三种方式随便挑一种）：

```powershell
# ① Visual Studio / Rider：双击  Source\Binguin\Binguin.Race.sln  →  生成解决方案
# ② 命令行
cd Source\Binguin
dotnet build
# ③ 不用装任何东西
cd Source
.\build.ps1
```

编译完成后，这个目录里会自动出现 `Binguin.dll`（csproj 的 `CopyToCustomDirectory`
会把产物铺到这里），**不需要手动复制**。

## 想要「下载即可玩」的整合包

自己去 GitHub 的 **Actions** 页面下载编译好的 Artifact，
或等作者发 **Release** 时附带的打包版本 —— 那些不是 git 仓库内容。

## 别手动往这里放 dll

`.gitignore` 已经挡掉了 `Assemblies/Binguin.dll` 和 `*.pdb`，
所以放了也不会被提交（但会误导别人以为仓库里有）。
