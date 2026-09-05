<p align="center">
  <img src="icons/org.clearpower.ClearPower.svg" width="112" alt="ClearPower">
</p>
<h1 align="center">ClearPower</h1>
<p align="center">
  <strong>笔记本充电上限 + 实时功耗分解。</strong><br>
  Linux · macOS · Windows &nbsp;—&nbsp; 三个平台同一个弹窗。
</p>
<p align="center">
  <a href="https://github.com/Clearailhc/ClearPower/releases/latest"><img src="https://img.shields.io/github/v/release/Clearailhc/ClearPower?label=release&color=4FC386" alt="Release"></a>
  <a href="https://github.com/Clearailhc/ClearPower/actions/workflows/build.yml"><img src="https://img.shields.io/github/actions/workflow/status/Clearailhc/ClearPower/build.yml?label=build" alt="Build"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-Apache--2.0-blue" alt="License"></a>
  <img src="https://img.shields.io/badge/platforms-Linux%20%7C%20macOS%20%7C%20Windows-6FB4F2" alt="Platforms">
</p>
<p align="center">
  <a href="README.md">English</a> · <b>简体中文</b>
</p>

<p align="center">
  <img src="docs/popover.png" width="330" alt="GNOME 上的 ClearPower">&nbsp;&nbsp;
  <img src="docs/popover-windows.png" width="330" alt="Windows 上的 ClearPower">
</p>

ClearPower 让电池充到 80% 就停（上限可以自己定），并显示每一瓦电的去向：适配器/电池 → 整机 → CPU · GPU · SoC · 内存 · 屏幕 · 其他。每个数字**要么是传感器实测的，要么是实测值相减得来的**，所以分项加起来正好等于总数。给常年插着电的笔记本用，参考了 macOS 上的 [AlDente](https://apphousekitchen.com/)。

## 功能

- **充电上限** —— 点一下在 80 / 90 / 100% 之间切换，或在设置里选 50–100 的任意值。**补满**一次性充到 100%；**放电**主动放到上限（需要固件支持）；两个操作完成后都会自动恢复原来的上限。
- **功耗分解** —— 数据来自传感器：芯片读 Intel RAPL / Apple 能量计数器，整机读电池自己的电量计。不做任何建模，分项加起来等于总数。
- **屏幕功耗单独算** —— 校准一次（全白屏 + 亮度扫描）得到这块面板自己的功耗曲线，之后按屏幕实际内容缩放。同一块 OLED，满屏白底可能要 7 W，深色界面只要 0.4 W。
- **续航估计**按电量计在 10 分钟 / 30 分钟 / 1 小时窗口内的实际变化算，比系统的瞬时估计稳定。
- **电源模式、温度、风扇、以及耗电明显的应用**，都在同一个弹窗里。
- **占用小** —— 一个小进程；弹窗关着时降低采样频率，也不绘制图表。
- 中英文界面，浅色/深色主题。

## 安装

| 平台 | 下载 | 说明 |
|---|---|---|
| **Windows 11**（x64） | [`ClearPower-Setup-<v>-x64.exe`](https://github.com/Clearailhc/ClearPower/releases/latest) 或便携 zip | 按用户安装，不需要管理员，不下载运行时（单个 200 KB 的 exe）。ThinkPad 通过 Lenovo 驱动控制充电。→ [windows/README.md](windows/README.md) |
| **macOS**（Apple Silicon） | [`ClearPower-<v>-arm64.dmg`](https://github.com/Clearailhc/ClearPower/releases/latest) | macOS 14+。充电控制由一个小的特权助手负责，安装时提示一次管理员密码。→ [macos/README.md](macos/README.md) |
| **Linux**（GNOME 48–50） | [`clearpower_<v>_all.deb`](https://github.com/Clearailhc/ClearPower/releases/latest) 或 `./install.sh` | root 守护进程 + GNOME Shell 扩展。→ [docs/linux.md](docs/linux.md) |

装好后在 **设置 › 校准** 做一次校准，屏幕就会有自己的数字，而不是并进"其他"（Windows 上请先拔掉电源，那里只有用电池时才测得到整机功耗）。

每个版本三个平台的安装包一起发布，附 `SHA256SUMS`。

## 数字是怎么来的

| 量 | Linux | macOS | Windows |
|---|---|---|---|
| 整机 | 用电池时读电池电量计，接电时读 RAPL `psys` | 用电池时读电池电量计，接电时读 SMC `PSTR` | 用电池时读电池电量计；接电时为估算（≈） |
| CPU / GPU / 内存 | RAPL `core` / `uncore` / `dram` | IOReport `CPU Energy` / `GPU Energy` / `DRAM` | Energy Meter `PP0` / `PP1` / `DRAM` |
| SoC（互连、NPU、媒体引擎…） | `package` − CPU − GPU | 芯片上其余的部分 | `PKG` − PP0 − PP1 |
| 屏幕 | 校准曲线 × 亮度 × 画面内容 | 同左 | 同左 |
| 其他（SSD、Wi-Fi、USB…） | 整机 − 以上全部 | 同左 | 同左 |
| 充电阈值 | `charge_control_*_threshold`（sysfs） | SMC 键，由助手写入 | Lenovo Power Manager（EC） |

所有瓦数显示前都经过 5 秒平滑。各平台的细节和硬件支持表见各自的 README。

## 硬件支持一览

| | 完整功耗分解 | 充电上限 | 放电 |
|---|---|---|---|
| Linux | Intel RAPL | ThinkPad 及其他有内核阈值接口的品牌 | ThinkPad |
| macOS | Apple Silicon | 全部 Apple Silicon Mac | 支持 |
| Windows | Intel（Windows 11 Energy Meter 接口） | ThinkPad（Lenovo Power Manager 驱动） | – |

AMD 的 RAPL、其他品牌的充电接口、Windows 传感器驱动都还没做，欢迎 PR，见[参与贡献](#参与贡献)。

## 仓库结构

```
daemon/          Linux 后端（Python）：Snapshot 契约的参考实现
extension/       GNOME Shell 前端
macos/           Swift 包：核心逻辑、IOKit/SMC 后端、特权助手、SwiftUI 应用
windows/         C#/WPF：核心逻辑、Energy Meter / 电池 / Lenovo 后端、托盘应用、安装包
docs/            各平台说明、发布说明、截图
packaging/       systemd 单元、D-Bus 策略、polkit 动作、桌面项、deb 脚本
```

三个平台输出同一份 `Snapshot` 字典（同样的键、同样的单位，`-1` 表示未知），并用黄金测试对照 Python 守护进程逐值校验（`macos/scripts/gen-fixtures.py`，Swift 和 C# 共用同一套夹具）。新平台只需要写一个能填满这份字典的后端。

## 参与贡献

欢迎 issue 和 PR：AMD 的 RAPL、其他品牌的充电阈值接口（Linux 和 Windows）、翻译（每个平台一份字典，键相同）、其他桌面环境的前端。项目有两条规矩：

1. 显示的每个数字都是实测或由实测值推导的，不建模，估算的一律标 ≈；
2. 弹窗关着的时候不做多余的采样。

路线图：Windows 上 Lenovo 之外的充电控制，以及带签名的温度传感器驱动；macOS 公证与 SMAppService；Linux 上的 AMD RAPL。

## 许可证

[Apache-2.0](LICENSE)
