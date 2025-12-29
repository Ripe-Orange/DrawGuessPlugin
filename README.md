# 你画我猜插件

一个用于Draw & Guess游戏的BepInEx插件。

[![GitHub release (latest by date)](https://img.shields.io/github/v/release/Ripe-Orange/DrawGuessPlugin?style=flat-square&color=64b5f6)](https://github.com/Ripe-Orange/DrawGuessPlugin/releases)
[![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20macOS%20%7C%20Linux-lightgrey?style=flat-square)](https://github.com/Ripe-Orange/DrawGuessPlugin)
[![License](https://img.shields.io/github/license/Ripe-Orange/DrawGuessPlugin?style=flat-square&color=orange)](LICENSE)
[![BepInEx Version](https://img.shields.io/badge/BepInEx-5.x-green?style=flat-square)](https://github.com/BepInEx/BepInEx)
[![PRs Welcome](https://img.shields.io/badge/PRs-welcome-brightgreen.svg?style=flat-square)](https://github.com/Ripe-Orange/DrawGuessPlugin)

[项目地址](https://github.com/Ripe-Orange/DrawGuessPlugin) · [报告问题](https://github.com/Ripe-Orange/DrawGuessPlugin/issues) · [提交改进](https://github.com/Ripe-Orange/DrawGuessPlugin/pulls)
## 功能特性

- 🎨 **压力感应线条**：支持数位板压力输入，根据压力大小动态调整线条粗细
- 🔄 **跨平台支持**：兼容Windows、macOS和Linux
- 🌐 **多游戏模式支持**：支持茶绘、接龙、竞猜等模式。
- 📱 **优化的性能**：使用动态线段大小和点距离，平衡性能与画质
- 🎯 **自相交处理**：智能处理线条自相交情况，确保线条完整性

## 安装说明

### 前置条件

- Draw & Guess 游戏
- BepInEx 5.x

### 安装步骤

1. 确保已安装 BepInEx 5.x 到 Draw & Guess 游戏目录
2. 下载最新版本的 `DrawGuessPlugin.dll`
3. 将 `DrawGuessPlugin.dll` 复制到游戏目录下的 `BepInEx/plugins` 文件夹
4. 启动游戏，插件将自动加载

## 技术规格

- **目标框架**：.NET Standard 2.1
- **开发语言**：C#
- **依赖项**：
    - BepInEx 5.x
    - Harmony
    - Unity


## 构建项目

### 开发环境

- .NET SDK 8.0
- Visual Studio 2022/2026 或 JetBrains Rider

### 构建步骤

1. 克隆项目仓库
2. 安装依赖项
3. 运行 `dotnet build` 命令构建项目
4. 在 `bin/Debug/netstandard2.1` 目录下找到构建产物

## 贡献指南

欢迎提交 Issue 和 Pull Request！请先阅读[贡献指南](CONTRIBUTING.md)。

## 行为准则

请遵守[行为准则](CODE_OF_CONDUCT.md)。

## 安全政策

查看[安全政策](SECURITY.md)。

## 问题反馈

如果您发现了 bug 或有新功能建议，请在 GitHub 上创建一个 Issue。在创建 Issue 时，请：

- 使用清晰、描述性的标题
- 提供详细的问题描述
- 包括复现步骤（如果是 bug）
- 提供预期行为和实际行为
- 附上日志（包括`BepInEx/LogOutput.log`文件、`Acureus/Draw_Guess/Player.log`文件、`Acureus/Draw_Guess/Player-prev.log`文件）
- 附上相关截图（如果适用）

## 许可证

MIT License

## 致谢

- [BepInEx](https://github.com/BepInEx/BepInEx)
- [HarmonyLib](https://github.com/BepInEx/Harmony)
- [Unity](https://unity3d.com/)
- [Draw & Guess](https://drawandguess.com/)

**享受绘画的乐趣！** 🎨