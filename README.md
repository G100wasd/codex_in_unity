# Codex for Unity — Unity 2022 Compatible Edition

Unity 2022.3 LTS 兼容版的 Codex Unity Editor 插件，提供 Codex App Server、自定义 OpenAI-compatible API、审批卡与本地 Unity MCP 工具桥接。

## 兼容性

- 已在 Unity `2022.3.62f2c1` 编译验证。
- 依赖 Unity 官方包 `com.unity.nuget.newtonsoft-json@3.2.2`。
- 本包替代 Unity 6 版的 `System.Text.Json` 实现；不要与 Unity 6 版同时安装，因为两者包名相同。

## 安装

1. 解压该发布包。
2. 在 Unity 中打开 `Window > Package Manager`。
3. 点击 `+`，选择 `Add package from disk...`。
4. 选择本目录中的 `package.json`。

安装完成后，从 `Codex > Open Codex` 打开插件。

## 要求

- Unity 2022.3 或更高版本。
- 本机 Codex 登录模式需要已安装 Codex CLI/App Server。
- 自定义 API 模式需要 OpenAI-compatible `chat/completions` 端点；若需要操作 Unity，模型与端点还必须支持 Function Calling (`tools` / `tool_calls`)。

## 安全说明

Unity MCP 服务只监听本机回环地址。文件修改、MCP 调用和 Unity API 修改会按插件设置显示审批卡。
