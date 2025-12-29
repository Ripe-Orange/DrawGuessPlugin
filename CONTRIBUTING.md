# 贡献指南

感谢您考虑为 DrawGuessPlugin 项目做出贡献！本指南将帮助您了解如何参与项目开发。

## 开发环境设置

### 前置条件

- .NET SDK 8.0 或更高版本
- BepInEx 开发环境

### 步骤

1. 克隆仓库到本地
2. 安装依赖
3. 打开项目进行开发
4. 运行测试确保代码正常工作

## 贡献流程

### 1. 报告问题

如果您发现了 bug 或有新功能建议，请在 GitHub 上创建一个 Issue。在创建 Issue 时，请：

- 使用清晰、描述性的标题
- 提供详细的问题描述
- 包括复现步骤（如果是bug）
- 提供预期行为和实际行为
- 描述您的环境（操作系统、.NET版本等）
- 附上日志（包括`BepInEx/LogOutput.log`文件、`Acureus/Draw_Guess/Player.log`文件、`Acureus/Draw_Guess/Player-prev.log`文件）
- 附上相关截图（如果适用）

### 2. 修复bug或实现新功能

1. 从 `develop` 分支创建一个新的功能分支
   ```bash
   git checkout -b feature/your-feature-name
   ```

2. 实现您的功能或修复
3. 确保代码符合项目的代码风格
4. 运行测试确保代码正常工作
5. 提交您的更改

### 3. 提交Pull Request

1. 将您的分支推送到GitHub
   ```bash
   git push origin feature/your-feature-name
   ```

2. 在GitHub上创建一个Pull Request，将您的功能分支合并到 `develop` 分支
3. 提供详细的PR描述，说明您的更改
4. 关联相关的Issue（如果适用）
5. 等待维护者审查

## 代码风格

- 使用C#标准代码风格
- 保持代码简洁明了
- 添加必要的注释，特别是复杂逻辑
- 遵循现有代码的命名约定
- 删除不必要的日志，只保留关键日志

## 测试

- 确保您的更改不会破坏现有功能
- 尽可能添加新的测试用例（如果有测试框架）

## 版本控制

项目使用Git Flow工作流：

- `main` 分支：稳定版本，仅用于发布
- `develop` 分支：开发分支，所有功能分支从这里创建
- `feature/*` 分支：新功能开发
- `hotfix/*` 分支：紧急bug修复

## 许可证

通过贡献代码，您同意您的贡献将根据项目的许可证（MIT）发布。