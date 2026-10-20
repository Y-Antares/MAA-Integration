# MAA 多配置启动器

一个 Windows 小工具：把 [MAA（MaaAssistantArknights）](https://github.com/MaaAssistantArknights/MaaAssistantArknights) 的多个**配置**串起来，按顺序一个个启动，跑完自动换下一个。

适合多账号（官服大号 / 官服小号 / 日服 / 台服…）轮流挂机的场景。



---

## 目录

- [功能](#功能)
- [方式一：直接下载使用](#方式一直接下载使用)
- [方式二：自己编译](#方式二自己编译)
- [运行时它会改什么](#运行时它会改什么)
- [常见问题](#常见问题)
- [发布流程（维护者）](#发布流程维护者)

---

## 功能

- 读取 `config\gui.new.json`，列出所有配置（配置名 + 账号 + 该配置的"启动后自动运行"状态，标出当前配置）
- 勾选要跑的配置，按列表顺序依次启动：`MAA.exe --config "配置名"`
- **依次推进**：MAA 是同一目录单实例，所以等上一个 MAA 进程真正退出才启动下一个
- **跑完自动退出**：需要时给"任务完成后操作"补上 `ExitSelf`（多值合并，保留你原有的"返回桌面 / 退出游戏"）
- **临时打开自动运行**：配置里关着"启动 MAA 后自动运行"时，本次运行临时打开（否则任务不会自己跑，也就不会自己退）
- **低优先级运行**：让 MAA 以 `BelowNormal` 优先级跑，把 CPU 让给浏览器 / 游戏 / 直播
- **恢复原样**：整轮结束（或停止、出错）后把改过的配置项还原；程序被强杀也有 journal 兜底，下次启动自动恢复
- 启动前检查：MAA 是否已在运行、目录是否可写（避免 MAA 弹"无法保存数据"）、运行中禁止关窗
- 记住 MAA 目录和勾选状态

---

## 方式一：直接下载使用

不需要装 .NET、不需要编译，下载 → 双击 → 用。

### 1. 下载哪个文件

打开仓库的 **Releases** 页面（右侧 `Releases` → 某个版本），有两个附件，按需选一个：

| 附件 | 是什么 | 需要装 .NET 吗 | 体积 |
|---|---|---|---|
| `MAA-ConfigLauncher.exe` | **自包含单文件**，运行时都打包在里面 | 不用，双击即用 | ~150 MB |
| `MAA-ConfigLauncher-fx-win-x64.zip` | **框架依赖四件套**（exe + dll + deps.json + runtimeconfig.json） | **要装 .NET 9 Desktop Runtime** | ~130 KB |

一般选第一个就行。第二个适合已经装了 .NET 9 桌面运行时、或者想看看文件结构的人。

### 2. （可选）校验下载是否完整

Release 里附了 `SHA256SUMS.txt`：

```powershell
Get-FileHash .\MAA-ConfigLauncher.exe -Algorithm SHA256
# 和 SHA256SUMS.txt 里对应的那一行对比，一致就说明没下坏
```

### 3. 放到一个可写目录

解压/移动到一个**普通文件夹**，例如 `D:\Tools\MAA-ConfigLauncher\`。

> ⚠️ 不要放在 `C:\Program Files`、`C:\Windows`、`System32` 这类系统保护目录——程序需要在 exe 旁边保存 `MAA-ConfigLauncher.settings.json`（记住你的 MAA 目录和勾选），放这些地方会写不进去。

用 zip 版的，把压缩包里的四个文件**解压到同一个文件夹**再运行，不要只拿 exe。

### 4. 首次运行

1. **完全退出 MAA**（托盘图标也要退出）；MAA 同一目录只允许一个实例，不退出的话切换配置不会生效
2. 双击 `MAA-ConfigLauncher.exe`
   - 如果 Windows 弹「已保护你的电脑 / 未知发布者」→ 点「更多信息」→「仍要运行」（程序没有代码签名，属正常现象）
3. 确认顶部「MAA 目录」指向你的 MAA 安装目录（含 `MAA.exe` 的那个文件夹）；不对就点「浏览…」重新选
4. 在列表里勾选要依次运行的配置（默认已勾好非时间戳的配置）
5. 按需调整下面三个开关：
   - **运行完自动退出 MAA**（默认开）
   - **临时打开"启动 MAA 后自动运行"**（配置里关着时要开，否则不会自己跑也不会自己退）
   - **以低优先级运行 MAA**（默认开，看直播/玩游戏时更跟手）
6. 点「开始运行」，看日志跑完；随时可以点「停止」（停止只会停止排队，不会杀掉正在跑的 MAA）

---

## 方式二：自己编译

### 环境要求

- Windows 10 / 11
- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)（`dotnet --version` 能看到 `9.x`）
- Git

### 1. 获取源码

```powershell
git clone https://github.com/Y-Antares/MAA-Integration.git
cd MAA-Integration
```

> 如果你 Fork 了或换了地址，把上面的 URL 换成自己的。

### 2. 编译（日常调试用）

```powershell
dotnet build src/MAAConfigLauncher/MAAConfigLauncher.csproj -c Release
```

产物在 `src\MAAConfigLauncher\bin\Release\net9.0-windows\`，是**框架依赖**的四个文件：

```
MAA-ConfigLauncher.exe
MAA-ConfigLauncher.dll
MAA-ConfigLauncher.deps.json
MAA-ConfigLauncher.runtimeconfig.json
```

直接双击 exe 就能跑（本机要有 .NET 9 桌面运行时）。

### 3. 发布成单文件（给普通用户用）

```powershell
dotnet publish src/MAAConfigLauncher/MAAConfigLauncher.csproj -c Release -r win-x64 `
  --self-contained true -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
  -o dist
```

得到 `dist\MAA-ConfigLauncher.exe` 一个文件，约 150 MB，**不需要目标机器装 .NET**。

### 4. 发布成四件套（体积小，需运行时）

```powershell
# 关键：不要带 -r / --self-contained / PublishSingleFile，否则又会被合并成单文件
dotnet publish src/MAAConfigLauncher/MAAConfigLauncher.csproj -c Release -o dist-fx

Compress-Archive -Path dist-fx\* -DestinationPath dist\MAA-ConfigLauncher-fx-win-x64.zip -Force
```

产物约 130 KB，用户需要装 [.NET 9 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/9.0)。

### 5. 换图标

图标源图是 `src\MAAConfigLauncher\icon.png`，编译时用的 `app.ico` 由脚本生成：

```powershell
# 1) 用新图覆盖 src\MAAConfigLauncher\icon.png（建议 256×256 或 512×512 的正方形 PNG）
# 2) 重新生成 8 个尺寸的 ico
.\tools\make-icon.ps1 -Png .\src\MAAConfigLauncher\icon.png -Out .\src\MAAConfigLauncher\app.ico
# 3) 重新编译
dotnet build src/MAAConfigLauncher/MAAConfigLauncher.csproj -c Release
```

`app.ico` 会被两处使用：编译时嵌入 exe（资源管理器/快捷方式的图标）、以嵌入资源形式在运行时设给窗口（标题栏/Alt+Tab）。换完如果资源管理器还显示旧图标，执行 `ie4uinit.exe -show` 或重启 `explorer.exe`。

### 编译时的常见问题

| 现象 | 原因 / 处理 |
|---|---|
| `error NU1301: 无法加载源 https://api.nuget.org/...` | 还原时连不上 NuGet：换网络、设代理，或配置国内镜像源（`dotnet nuget add source`） |
| `error NETSDK1100: 项目目标框架需要 Windows` | 在非 Windows 机器上编译；需要在 Windows 上构建 |
| 双击报"需要 .NET Desktop Runtime" | 你跑的是框架依赖版；装 .NET 9 Desktop Runtime，或改用第 3 步的单文件发布 |
| 图标没变 | 清图标缓存：`ie4uinit.exe -show`；任务栏固定的要取消固定再重新固定 |

---

## 运行时它会改什么

程序本身不改 MAA 逻辑，但为了"跑完自动退出 / 自动跑任务"，它会在运行期间**临时**修改 MAA 配置 `config\gui.new.json` 里的两个字段：

| 字段 | 运行期间 | 结束后 |
|---|---|---|
| `Gui.PostActions` | 追加 `ExitSelf`（多值合并，原有动作保留） | 还原成原值 |
| `Gui.StartUpSettings.RunDirectly` | 设为 `true`（仅当你勾了"临时打开自动运行"） | 还原成原值 |

- 只改这两个字段，其余内容、顺序、其他设置都原样保留。
- 整轮结束 / 点停止 / 出错时都会还原；程序被强杀时会留一份 `MAA-ConfigLauncher.pending-restore.json`，下次启动自动还原。
- 运行期间如果你在 MAA 界面里改"任务完成后操作"，退出时会被还原成运行前的值——改设置请挑不跑的时候。

---

## 常见问题

**Q：启动就提示"当前软件所在的位置无法保存数据"？**
这是 MAA 的自检：它会在自己目录里试写 `write_test.tmp`。常见原因是它被一个受限令牌（沙箱、AI 助手终端等）的进程拉起来的，而不是你从资源管理器双击。关掉后在资源管理器里直接双击即可；程序启动前也会做同样的可写性检查并提示你。

**Q：点开始后一直卡在第一个配置？**
MAA 没有自动跑任务，或任务报错时跳过了"完成后操作"。前者勾上"临时打开自动运行"，后者看 MAA 日志或在 MAA 设置里关掉"出错时跳过完成后操作"。

**Q：能不能同时跑多个配置？**
不行。MAA 在同一安装目录是单实例（互斥体按路径算），第二个实例只会唤醒第一个。要并行只能复制多份 MAA 安装目录。

**Q：设置文件在哪？**
`MAA-ConfigLauncher.settings.json`，就在 exe 旁边，记 MAA 目录和勾选状态。删掉即恢复默认。

---

## 发布流程（维护者）

推 tag 即自动编译并发布：

```bash
git tag v1.0.0
git push origin v1.0.0
```

`.github/workflows/release.yml` 会：

1. 用 `dotnet publish` 产出**自包含单文件** exe；
2. 再产出**框架依赖四件套**并打成 zip；
3. 生成 `SHA256SUMS.txt`；
4. 把这三个文件挂到对应 Release 上（同时在 Actions 页面留一份 artifact）。

发版前记得把 `src/MAAConfigLauncher/MAAConfigLauncher.csproj` 里的 `<Version>` 和 tag 对齐。

---

## 说明

- 本项目只负责"排队启动 / 等待退出 / 切换配置"，不修改 MAA 本体，也不碰游戏。
- 与 MAA 官方无关，仅为个人使用的小工具。

## License

MIT，见 [LICENSE](LICENSE)。
