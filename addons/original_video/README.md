# Original Video 1.0

基于 WMV Video 0.6.0 改造的 Godot 4.5 GDExtension。包含 Windows x64 与 Android ARM64 的 debug/release 原生库；Windows 与 Android 使用相同的播放接口。

## 使用

将整个 addons/original_video 文件夹复制到 Godot 工程，再打开工程使 GDExtension 注册 OriginalVideoPlayer。无须安装 ffmpeg.exe 或 Java 插件。编辑器插件入口只提供元信息；实际节点由 original_video.gdextension 注册。

GDScript 示例：

~~~gdscript
var movie = ClassDB.instantiate("OriginalVideoPlayer")
add_child(movie)
movie.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
movie.source = "/storage/emulated/0/tenshi/openning.v"
movie.playback_finished.connect(func(): movie.stop())
movie.playback_error.connect(func(message): push_error(message))
movie.play()
~~~

Android 绝对路径必须已经获得读取权限。插件不弹授权窗口、不改写资源文件；Tenshi 的启动页负责存储授权和目录检查。支持 Godot FileAccess 可读的 res://、user:// 和外部绝对路径。

- ASF/WMV：WMV1/2/3、VC-1；WMA 系列音频。
- MPEG Program Stream：MPEG-1/2 视频、MP1/MP2 音频；按文件内容识别，支持原版 openning.v，无须改后缀。
- 读取原始文件，在内存解码，不生成转码文件、不解包素材。
- 独立音视频解码线程、有限队列、48 kHz 双声道输出；视频按音频时钟选择帧，跟不上时跳过过期帧。
- Android 进入后台暂停，返回前台恢复；手动暂停不会被后台恢复覆盖。

接口：play()、play_from_position(seconds)、pause()、set_paused(bool)、stop()、set_stream_position(seconds)。属性：source、autoplay、loop、volume_db、audio_bus。信号：playback_started、playback_paused、playback_stopped、playback_finished、playback_error(message)。

诊断：get_stream_position() 返回主时钟；get_video_position() 返回最后实际提交纹理帧的 PTS（未提交为 -1）；get_video_size()、has_audio()、get_audio_underrun_count()。自然结束保留尾帧，stop() 清除纹理和时钟。

当前预编译包仅提供 Windows x64 和 Android ARM64，未提供 iOS、Linux、Android 32 位或 Android x86 库。本仓库不附带真机验收记录，请在目标设备上自行验证。
