# DiceVaders 星座刷新工具

> 在星座选择界面加一个「刷新」按钮 —— 不用再反复重开游戏刷开局。
>
> 适用游戏：**DiceVaders**（Steam AppID `3917700`）

---

## 功能

开局会随机给你 1~3 个**星座**，每个星座改变这局游戏的玩法（单位稀有度、额外效果、特殊神器等）。
原版想换掉不满意的星座，只能反复重开游戏。装上这个 mod 之后：

| 按钮 | 位置 | 作用 |
|---|---|---|
| **刷新** | 每张星座卡片**正下方** | **只刷新这一张**，其他两张不动 |
| **刷新全部** | 界面右下角 | 全部重新随机 |

- 名字、星座连线图、详情卡片**三者同步刷新**
- 按钮外观是克隆游戏原版按钮做的，不是外挂式悬浮窗
- 按钮只在**星座选择界面**出现，进游戏后自动隐藏

---

## 安装

### 第一步：先给游戏装 BepInEx

> ⚠️ **必须是 BepInEx 6（IL2CPP 版），不是 5.x。** DiceVaders 是 IL2CPP 打包的，装错版本不会生效。

1. 下载 **BepInEx 6 Windows x64 (IL2CPP)**：
   - 发布页：<https://github.com/BepInEx/BepInEx/releases>（找 `bleeding-edge` 预发布）
   - 或构建站：<https://builds.bepinex.dev/projects/bepinex_be>
   - 本文档验证过的版本：**6.0.0-be.788**

2. 解压出的文件**全部**放进游戏根目录（能看到 `DiceVaders.exe` 的那个文件夹），通常位于：

   ```
   C:\Program Files (x86)\Steam\steamapps\common\DiceVaders
   ```

   放完后目录里应该多出 `winhttp.dll`、`doorstop_config.ini`、`BepInEx` 文件夹。

3. **启动一次游戏再退出** —— 让 BepInEx 生成运行环境

   首次启动会慢一些（它在准备 IL2CPP 运行库）。进到主菜单就可以退了。

   **这之后 `BepInEx` 文件夹里才会出现 `interop` 和 `plugins` 两个目录。**

### 第二步：安装本 mod

1. 下载本仓库 **Releases** 里的压缩包（或 `release` 目录）

2. **把压缩包解压到游戏根目录**，覆盖即可。压缩包内部已经排好了目录结构：

   ```
   DiceVaders\                          ← 游戏根目录
   └── BepInEx\
       └── plugins\
           └── DiceVaders.ConstellationTool.dll     ← 就这一个文件
   ```

   所以你也可以手动把 `DiceVaders.ConstellationTool.dll` 丢进：

   ```
   <游戏根目录>\BepInEx\plugins\
   ```

3. **启动游戏** → 进到星座选择界面就能看到按钮

### 确认装好了

打开日志文件：

```
<游戏根目录>\BepInEx\LogOutput.log
```

搜索 `Constellation Tool`，能看到这行就说明加载成功：

```
[Info :DiceVaders Constellation Tool] ===== Constellation Tool v3.1.0 (刷新全部 + 每张卡片独立刷新) =====
```

---

## 配置

首次启动游戏后自动生成：

```
<游戏根目录>\BepInEx\config\dicevaders.constellationtool.cfg
```

用记事本打开即可修改，**改完重启游戏生效**。

### 常用项

```ini
[1-按钮]

# 是否显示每张卡片下方的「刷新」按钮（false = 只有右下角的「刷新全部」）
EnableSingleReroll = true

# 每张卡片下方「刷新」按钮的宽度
SingleButtonWidth = 170

# 右下角主按钮宽度
Width = 350

# 按钮在哪个角：右+底=右下角（默认）/ 右+顶=右上角 / 左+底=左下角
AnchorRight = true
AnchorBottom = true
RightOffset = 60
BottomOffset = 120

[3-安全]

# 单次停留在星座界面的最大「刷新全部」次数
MaxRerollPerSession = 20
```

> **注意**：主按钮的**高度会自动按原版长宽比跟随宽度**，不单独设置 ——
> 原版按钮的上下边框是独立子对象、按原高度定位，压扁高度会把边框挤出可见区域。

---

## 常见问题

**Q：装完没有按钮？**

按顺序查：

1. `BepInEx\LogOutput.log` 里有没有 `Constellation Tool` —— 没有就是插件没被加载
2. BepInEx 是不是装成了 **5.x**？必须是 **6.x IL2CPP 版**
3. 有没有**先启动过一次游戏**再放插件？`BepInEx\interop` 目录必须存在
4. 文件名必须是 `DiceVaders.ConstellationTool.dll`，且放在 `BepInEx\plugins\`（不是子目录里）

**Q：卡片下面的「刷新」按钮位置偏了 / 压到卡片上了？**

游戏这 3 张卡片是自由布局的，按钮位置靠运行时读取卡片坐标计算。
如果偏了，把 `LogOutput.log` 发出来（里面会打印每个卡片的屏幕坐标），我按你的分辨率调。

**Q：会和别的 mod 冲突吗？**

它只读取/修改「本局星座」这一项数据，不碰存档、不改 UI 布局，与常规 mod 不冲突。

**Q：会不会坏存档？**

- 本 mod **只改"本局开局时的星座"**，不碰你的进度存档
- 「刷新」按钮**不改动数据结构**（只替换一个 ID），所以没有累积风险
- 「刷新全部」走游戏自己的生成流程，会追加数据，因此**限制了单次最多 20 次**
- **建议**：只在开局选星座时用，选完点「继续」

**Q：游戏更新后还能用吗？**

IL2CPP 游戏每次本体更新都可能改变内部结构，导致插件失效（按钮消失或报错）。
失效了要么等作者更新，要么按下面的「从源码构建」自己修。

---

## 卸载

删掉这个文件即可：

```
<游戏根目录>\BepInEx\plugins\DiceVaders.ConstellationTool.dll
```

（可选）再删掉 `BepInEx\config\dicevaders.constellationtool.cfg`。

如果想连 BepInEx 一起卸载，删除游戏根目录下的 `winhttp.dll`、`doorstop_config.ini` 和 `BepInEx` 文件夹。

---

## 从源码构建

**前置**：装了 [.NET SDK 6.0+](https://dotnet.microsoft.com/download)，且游戏已装好 BepInEx（需要 `BepInEx\interop` 里的程序集）。

```bash
git clone https://github.com/HaibaraZAiX/DiceVaders-ConstellationTool.git
cd DiceVaders-ConstellationTool

# 默认按 Steam 标准路径找游戏
dotnet build src/DiceVaders.ConstellationTool.csproj -c Release

# 游戏装在别处就覆盖 GameDir：
dotnet build src/DiceVaders.ConstellationTool.csproj -c Release -p:GameDir="D:\你的路径\DiceVaders"
```

产物在 `src/bin/Release/DiceVaders.ConstellationTool.dll`。

---

## 技术说明（给想改的人）

- **引擎**：Unity 6000.3.x / IL2CPP / 元数据 v39
- **插件框架**：BepInEx 6 + Il2CppInterop
- **「刷新全部」**：调用游戏自己的 `ConstellationController.CreateConstellations()` 协程
- **「刷新」单个**：
  1. 按游戏自己的规则算候选池 = 全部星座 ∩ 本局可获得 ∩ 不在当前列表
  2. `ArtifactFactory.CreateArtifactModel()` 建新实例
  3. 注册进 `EncounterModel.ModelItemDict`
  4. **只替换 `EncounterModel.Constellations[目标位]` 这一个 ID**
  5. 调 `UpdateConstellationView()` 让游戏自己重建全部槽位
  6. 单独补星座连线图（它不在自动刷新流程里）
- **按钮**：克隆游戏自己的「揭晓！」按钮容器再改文字 ——
  游戏按钮是**多层子对象叠加**做的，自己拼会导致外观不一致
- **每张卡片的「刷新」按钮**：运行时读卡片坐标再贴上去，
  因为 3 张卡片是自由布局的，屏幕顺序 ≠ 列表索引顺序

源码注释里记录了完整的调试过程（包括走过的弯路），可以当作改这个游戏的参考。

---

## 许可

MIT License —— 随便用、随便改、随便分发，保留版权声明即可。

---

## 免责声明

本 mod 为个人学习逆向工程的产物，与游戏开发商无关。
仅供单机离线使用，请勿用于任何联机或商业用途。
