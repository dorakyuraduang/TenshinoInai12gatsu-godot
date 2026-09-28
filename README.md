# 天使不在的十二月 · Godot 移植

基于 Godot 4.5 .NET / C# 的《天使のいない12月》（Tenshi no Inai 12-gatsu）非官方移植与运行时实现。包含资源读取、脚本执行、画面与文字显示、音频、存档及视频播放功能。

本项目不代表原作权利人发布或认可，不附带原版游戏资源。目前仍可能存在兼容性问题，不保证所有资源版本、路线和设备均可正常运行。

## 环境与运行

使用 Godot **4.5.1 .NET 版**导入 `project.godot`，构建 C# 项目并运行。桌面目标框架为 .NET 8，Android 为 .NET 9。可在项目目录执行 `dotnet build Tenshi.csproj` 检查编译。

视频插件预编译库仅提供 **Windows x64 / Android ARM64**。Android 导出需要对应 Godot 导出模板、Android SDK/JDK 和 .NET Android 构建环境，签名与图标请自行配置。

### 外部资源

请从有权使用的游戏副本中自行准备以下文件，保留名称，无须解包或转码：

```text
tenshi_dvd.a
sys.a
egbg.a
char.a
voice.a
music.a
se.a
ed.a
openning.v
```

桌面端固定读取项目目录同级的 `天使不在的12月` 文件夹：

```text
父目录/
├─ TenshinoInai12gatsu-godot/
│  └─ project.godot
└─ 天使不在的12月/
   ├─ tenshi_dvd.a
   └─ ...
```

Android 读取内部共享存储的 `tenshi` 文件夹，主用户通常为 `/storage/emulated/0/tenshi`。按启动提示授予存储权限，再点击“重新检查”。Android 11 及以上使用“管理所有文件”权限，实际路径由系统返回。

仓库不附带 `fonts/simhei.ttf`。代码有字体回退逻辑，但显示效果取决于设备字体；若自行打包字体，应选用允许相应分发用途的字体并保留许可。

## 开源协议与版权声明

### 原创代码：MIT

除第三方组件、已有独立许可或另行注明的内容外，本项目原创代码及文档以 [MIT License](LICENSE) 发布。允许使用、复制、修改、合并、发布、分发、再许可和销售软件副本；分发时须保留版权声明和许可文本。软件按原样提供，不提供担保。具体以 `LICENSE` 原文为准。

**代码开源不等于原作游戏开源。** MIT 许可不授予对原作剧情、剧本、角色、美术、音乐、语音、视频、商标、汉化文本或第三方字体的任何权利。这些内容仍归各自权利人所有；使用或分发需要相应授权。本仓库不提供这些资源及其下载渠道。

“非官方”或“学习用途”的说明不能替代授权。请勿提交原作资源或无权分发的汉化包、字体和图标。

### 第三方组件

第三方内容继续适用各自许可证，不因根目录 MIT 许可而改变。

| 组件 | 许可及出处 |
| --- | --- |
| GARbro PX 解码参考代码 | MIT，保留 morkt 版权与许可，见 [第三方声明](THIRD_PARTY_NOTICES.md) |
| Original Video / WMV Video 衍生插件 | [MIT](addons/original_video/LICENSE) |
| FFmpeg 7.1.5 动态库 | [LGPL-2.1-or-later](addons/original_video/licenses/FFmpeg-LGPL-2.1-or-later.txt)，当前构建未启用 GPL / nonfree 组件 |
| godot-cpp | [MIT](addons/original_video/licenses/godot-cpp-MIT.txt) |
| MinGW libwinpthread | [随附版权与许可](addons/original_video/licenses/libwinpthread-COPYING.txt) |

本软件使用 FFmpeg 项目的库，对应源码见 [FFmpeg 源码包](third_party/original-video-ffmpeg-7.1.5-source.zip)，配置与重建方法见 [BUILDING.md](addons/original_video/BUILDING.md)。分发包含这些库的插件或安装包时，应提供相应许可、版权说明、对应源码及构建资料，并遵守 LGPL 关于修改、替换及重新链接等适用要求。仅附本项目 MIT 文本并不足够。参考 [FFmpeg 官方许可说明](https://www.ffmpeg.org/legal.html)。完整组件信息见 [插件第三方声明](addons/original_video/THIRD_PARTY_NOTICES.md)。

## 项目结构

- `Scripts/`：C# 运行时、资源解码、界面及回归检查入口。
- `Main.tscn` / `project.godot`：主场景与项目配置。
- `addons/original_video/`：视频插件源码、动态库及许可。
- `tools/`：视频插件和 FFmpeg 构建脚本。
- `third_party/`：FFmpeg 对应源码包。

## 反馈与贡献

欢迎提交问题和补丁。请注明系统、Godot 版本、复现步骤及错误日志，避免上传原作资源、完整剧本及个人存档信息。贡献代码须拥有相应授权，引用第三方代码应保留来源和许可证。
