# Codex for Unity

在 Unity Editor 内提供 Codex 对话、Codex App Server 集成、自定义 OpenAI-compatible API 接入，以及本地 Unity MCP 工具桥接。

从 Unity 菜单 `Codex > Open Codex` 打开插件。

## 发布版本与兼容性

| 发布包 | Unity 版本 | JSON 实现 | 状态 |
| --- | --- | --- | --- |
| `CodexUnityPackage-0.1.0-unity6000.zip` | Unity 6 / 6000.0+ | `System.Text.Json` | 主版本，完整功能开发与验证版本。 |
| `CodexUnityPackage-0.1.0-unity2022.zip` | Unity 2022.3+ | `com.unity.nuget.newtonsoft-json@3.2.2` | 兼容版；已在 Unity `2022.3.62f2c1` 编译通过，但**尚未完成完整功能回归测试**。部分聊天、MCP、审批或恢复功能仍可能存在问题。 |

两个版本使用相同的 UPM 包名，**同一个 Unity 项目只能安装其中一个**。请根据编辑器版本下载对应发布包；不要将两个版本同时导入。

## 安装

1. 解压对应版本的发布压缩包。
2. 在 Unity 中打开 `Window > Package Manager`。
3. 点击左上角 `+`，选择 `Add package from disk...`。
4. 选择解压目录中的 `package.json`。
5. 导入完成后，点击 `Codex > Open Codex`。

## 登录方式

### 本机 Codex 登录

插件启动本机 Codex App Server，并复用官方 Codex 登录状态。它不会读取或解析你的凭证文件。

此模式使用项目根目录作为工作区边界，可读取、创建和恢复该项目关联的 Codex 聊天。

### 自定义 API Key 登录

可配置 API Key、模型名称和 OpenAI-compatible 模型链接。该模式通过 `chat/completions` 直接请求第三方模型。

要让模型实际调用 Unity 工具，模型和服务端必须支持 OpenAI Function Calling：`tools`、`tool_calls` 与工具结果消息。API Key 模式的聊天池保存在插件本地，不与 Codex 桌面端共享。

## 主要功能

- 按当前 Unity 项目组织聊天池。
- 模型、思考强度、全局提示词与审批策略设置。
- 文件修改、MCP 调用、Unity API 操作的独立审批卡。
- 本地 Unity MCP Bridge：场景、对象、组件、Prefab、资源、Console、构建、项目设置与诊断等 Editor 工具。
- 脚本编译/Domain Reload 后的 Codex App Server 任务恢复机制。
- 输入框行数、聊天背景色、工具可用性等界面配置。

## 安全说明

Unity MCP 服务仅监听 `127.0.0.1` 的随机端口，不会暴露到局域网。高风险工具默认需要审批；请谨慎开启“始终允许”或启用具有删除、写入、构建能力的工具。

## 已知限制

- Codex 桌面端与插件可能同时占用同一 Thread；遇到 active writer 时请在另一端结束该回合后重试。
- 自定义 API 登录能否完成 Unity Agent 操作，取决于模型端点的工具调用兼容性；仅会普通聊天的模型无法触发 Unity 审批卡或工具执行。
- Unity 2022 兼容版目前仅完成导入与 Editor 编译验证，请优先在副本项目中测试，并反馈异常日志。
