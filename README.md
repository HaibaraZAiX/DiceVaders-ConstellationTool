# DiceVaders 星座重掷工具

> 在多元宇宙的**星座选择界面**加一个「重掷全部」按钮 —— 不用再反复重开游戏刷开局。
>
> 适用游戏：**DiceVaders**（Steam AppID `3917700`）

---

## 这个 mod 解决什么问题

DiceVaders 开局会随机给你 1~3 个**星座**，每个星座会改变这局游戏的玩法（单位稀有度、额外效果、特殊神器等）。
想刷到满意的组合，原版只能**反复重开游戏** —— 每次都要看完开场动画、走到星座界面。

装上这个 mod 之后，星座界面右侧会多出一个「**重掷全部**」按钮：

- 点一下 → 重新随机全部星座
- 星座的名字、连线图案、详情卡片**三者同步刷新**
- 可以一直点，直到刷到满意的组合

---

## 安装

### 前置：先给游戏装 BepInEx

> ⚠️ **必须是 BepInEx 6（IL2CPP 版），不是 5.x。** DiceVaders 是 IL2CPP 打包的，装错版本不会生效。

1. 到 BepInEx 的发布页下载 **BepInEx 6 的 Windows x64 (IL2CPP)** 版本
   - 发布地址：<https://github.com/BepInEx/BepInEx/releases>（找 `bleeding-edge` 预发布版）
   - 也可以用官方构建站：<https://builds.bepinex.dev/projects/bepinex_be>
   - 本文档验证过的版本：**6.0.0-be.788**

2. 解压后，把里面的文件**全部**放到游戏根目录

   游戏根目录就是能看到 `DiceVaders.exe` 的那个文件夹，通常在：

   ```
   C:\Program Files (x86)\Steam\steamapps\common\DiceVaders
   ```

   放完之后目录里应该多出 `winhttp.dll`、`doorstop_config.ini`、`BepInEx` 文件夹。

3. **启动一次游戏再关闭** —— 让 BepInEx 生成必要的运行环境

   首次启动会稍慢（它在准备 IL2CPP 的运行库）。启动到主菜单就可以退出了。

   这时 `BepInEx` 文件夹里会出现 `interop` 和 `plugins` 两个目录。

### 安装本 mod

1. 下载本仓库 `release` 目录里的 **`DiceVaders.ConstellationTool.dll`**

2. 把它放进：

   ```
   <游戏根目录>\BepInEx\plugins\
   ```

   放完之后路径应该长这样：

   ```
   ...\DiceVaders\BepInEx\plugins\DiceVaders.ConstellationTool.dll
   ```

3. **启动游戏** —— 进到星座选择界面就能看到按钮了

### 确认装好了

看这个日志文件：

```
<游戏根目录>\BepInEx\LogOutput.log
```

搜索 `Constellation Tool`，能看到类似这样一行就说明加载成功：

```
[Info :DiceVaders Constellation Tool] ===== Constellation Tool v2.9.0 =====
```

---

## 使用

1. 开一局新游戏，走到**星座选择界面**
2. 界面上会出现：
   - 右下角一个「**重掷全部**」按钮
3. 点它 → 星座重新随机
4. 满意了就点界面下方的「**继续**」

**如果按钮位置或大小不合适**，改配置文件（见下一节），不用重新装。

### 关于「揭晓」

游戏原版每个星座下面有个「**揭晓！**」按钮，点开才显示详情卡片。
本 mod 会在重掷后**自动帮你揭晓**，所以正常情况下你不需要手动点。

---

## 配置

配置文件在（首次启动游戏后自动生成）：

```
<游戏根目录>\BepInEx\config\dicevaders.constellationtool.cfg
```

用记事本打开就能改。**改完重启游戏生效。**

### 按钮位置与大小

```ini
[1-按钮]

# 靠右还是靠左（true = 靠右）
AnchorRight = true

# 靠底还是靠顶（true = 靠底）→ 和上面组合决定在哪个角
#   右 + 底 = 右下角（默认）
#   右 + 顶 = 右上角
#   左 + 底 = 左下角
AnchorBottom = true

# 距屏幕右边缘的像素（靠右时生效）
RightOffset = 60

# 距屏幕底部的像素（靠底时生效）
BottomOffset = 120

# 距屏幕顶部的像素（靠顶时生效）
TopOffset = 430

# 距屏幕左边缘的像素（靠左时生效）
LeftOffset = 120

# 按钮宽度（高度会按游戏原版按钮的比例自动跟随）
Width = 350
```

### 安全限制

```ini
[3-安全]

# 单次停留在星座界面的最大重掷次数（防止连点出意外）
MaxRerollPerSession = 20
```

---

## 常见问题

**Q：装完没有按钮？**

按顺序检查：

1. `BepInEx\LogOutput.log` 里有没有 `Constellation Tool` 字样 —— 没有的话说明插件没被加载
2. 是不是装成了 BepInEx **5.x**？必须是 **6.x IL2CPP 版**
3. 有没有**先启动过一次游戏**再放插件？`BepInEx\interop` 目录必须存在
4. `BepInEx\plugins\` 里的文件名必须是 `DiceVaders.ConstellationTool.dll`

**Q：按钮在局内也出现过 / 挡住别的界面？**

v1.5 起已经修好了 —— 按钮只在**星座选择界面**显示，进游戏后自动隐藏。
如果你用的是旧版，换新版。

**Q：点重掷后星座名字变了，但图案/详情没跟着变？**

v1.7 起已修复（数据长度与界面槽位数的对齐问题）。
如果还遇到，把 `BepInEx\LogOutput.log` 发出来。

**Q：会不会坏存档？**

- 本 mod **只改"本局开局时的星座"**，不碰你的进度存档
- 但有**已知风险**：如果在**已经进入游戏后**反复重掷，可能让本局数据异常
  （游戏会弹「任务引擎崩溃…需要删除本局」）
- 因此 mod **主动拦截**了局内的重掷操作，并且限制了单次停留的重掷次数
- **建议**：只在**开局选星座时**用，选完就点继续

**Q：能不能只重掷其中某一个星座？**

**做不到，这是游戏本身的限制。**

反汇编游戏代码确认：游戏只提供「重新生成一整组星座」这一个接口，
**没有任何接口能"只重生成第 N 个"** —— 星座和界面槽位的绑定关系藏在游戏的任务引擎内部。

所以本 mod 只做了「全部重掷」。这不是实现不到位，是游戏没给这条路。

**Q：游戏更新后还能用吗？**

Unity 的 IL2CPP 游戏，**每次游戏本体更新都可能改变内部结构**，导致插件失效（表现为按钮消失或报错）。
如果失效了，等作者更新，或者按下面的「从源码构建」自己修。

---

## 卸载

1. 删掉 `<游戏根目录>\BepInEx\plugins\DiceVaders.ConstellationTool.dll`
2. （可选）删掉 `BepInEx\config\dicevaders.constellationtool.cfg`

如果想连 BepInEx 一起卸掉，删除游戏根目录下的 `winhttp.dll`、`doorstop_config.ini` 和 `BepInEx` 文件夹即可。

---

## 从源码构建

**前置**：装了 [.NET SDK 6.0+](https://dotnet.microsoft.com/download)，并且游戏已装好 BepInEx（需要 `BepInEx\interop` 里的程序集）。

```bash
git clone <本仓库地址>
cd DiceVaders-ConstellationTool

# 默认按 Steam 标准路径找游戏；装在别处就覆盖 GameDir：
dotnet build src/DiceVaders.ConstellationTool.csproj -c Release

# 如果游戏不在默认位置：
dotnet build src/DiceVaders.ConstellationTool.csproj -c Release -p:GameDir="D:\你的路径\DiceVaders"
```

编译产物在 `src/bin/Release/DiceVaders.ConstellationTool.dll`，复制到 `BepInEx\plugins\` 即可。

---

## 技术说明（给想改的人）

- **引擎**：Unity 6000.3.x / IL2CPP / 元数据 v39
- **插件框架**：BepInEx 6 + Il2CppInterop
- **插入点**：`StarVaders.ConstellationController.CreateConstellations()`（协程）
- **按钮**：克隆游戏自己的「揭晓！」按钮容器（`RevealObject`）再改文字，
  因为游戏按钮是**四层子对象叠加**做的，自己拼会导致外观不一致
- **详情卡片**：靠调用 `Constellation.OnPressReveal()` 装配
- **数据对齐**：`EncounterModel.Constellations` 的长度必须等于界面未锁定槽位数，
  否则游戏的刷新方法会抛越界异常

源码里的注释记录了完整的调试过程（包括走过的弯路），可以直接当作改这个游戏的参考。

---

## 许可

MIT License —— 随便用、随便改、随便分发，保留版权声明即可。

---

## 免责声明

本 mod 为个人学习逆向工程的产物，与游戏开发商无关。
仅供单机离线使用，**请勿用于任何联机或商业用途**。
