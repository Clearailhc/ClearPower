# 小红书

**标题**（20 字内）

笔记本一直插电？给电池设个 80% 上限

**图片**（按顺序，1 为封面）

1. ![封面](./1-cover.png)
2. ![为什么要设上限](./2-why.png)
3. ![上限、补满、放电](./3-charge.png)
4. ![功耗流向图](./4-flow.png)
5. ![屏幕功耗是校准出来的](./5-screen.png)
6. ![硬件支持与下载](./6-download.png)

**正文**

笔记本常年插电、电量一直 100%，是锂电池老得最快的用法。Mac 上有 AlDente 能设上限，Linux 和 Windows 一直没有顺手的。

ClearPower 是一个开源小工具，三个平台一套：

🔋 充电上限：点一下在 80 / 90 / 100 之间切换，设置里 50–100 任选。补满一次性充到 100%，完成后自动恢复原来的上限；放电主动放到目标值（ThinkPad / Apple Silicon）。

📊 功耗流向图：适配器/电池 → 整机 → CPU / GPU / SoC / 内存 / 屏幕 / 其他。每个数字要么是传感器实测的，要么是实测值相减得来的，不做建模，所以分项加起来正好等于总数。

💡 屏幕功耗单独算：全白屏扫一遍亮度，反推出这块面板自己的功耗曲线。同一块 OLED，满屏白底 7 W，深色界面 0.4 W。

⏱ 续航估计按电量计在 10 分钟 / 30 分钟 / 1 小时窗口内的实际变化算，比系统的瞬时估计稳定。

弹窗关着的时候降低采样频率，也不绘制图表。

支持范围看第 6 张图：完整功耗分解要 Intel 或 Apple Silicon，充电上限目前是 ThinkPad 和 Apple Silicon Mac。

Linux deb / macOS dmg / Windows 安装包和便携版都在 GitHub Releases，搜 Clearailhc/ClearPower，Apache-2.0。

#笔记本 #电池健康 #ThinkPad #MacBook #开源 #效率工具 #Linux #Windows
