---
layout: landing
title: "Codex App 一键安装程序（ChatGPT 桌面版）"
description: "Windows 上绕过下载网络限制，一键安装 Codex App——ChatGPT 桌面应用。镜像加速下载官方 MSIX、SHA256 校验、静默安装、自动配置聚合 API，双击即用。"
tagline: "绕过下载网络限制，双击即装官方 Codex App（ChatGPT 桌面版）。镜像加速 + SHA256 校验，官方 MSIX 未重打包，自动配置聚合 API 开箱即用。"
softwareName: "Codex App 一键安装程序"
shortName: "CodexAppInstaller.exe"
downloadUrl: "https://raw.githubusercontent.com/l1i1/CodexAppInstaller/main/CodexAppInstaller.exe"
repoUrl: "https://github.com/l1i1/CodexAppInstaller"
lang: zh-CN
author: l1i1
date: 2026-08-09
faq:
  - q: "所有下载源都失败怎么办？"
    a: "检查网络/代理连通性后重试。程序内置多源镜像回退与 3 次自动重试，也可挂代理后手动安装。"
  - q: "提示 401 鉴权失败？"
    a: "检查 `~/.codex/auth.json` 中的 `OPENAI_API_KEY` 是否正确、账户余额是否充足。"
  - q: "提示 400 协议错误？"
    a: "聚合端点必须兼容 OpenAI Responses 协议（`wire_api = responses`），纯 Chat Completions 端点无法直连。"
  - q: "配置不生效？"
    a: "完全退出（含系统托盘图标）并重启 Codex 桌面应用；确认 `~/.codex/config.toml` 内容正确。"
  - q: "模型列表为空？"
    a: "检查端点 `/v1/models` 可用性与模型 ID（默认 `gpt-5.6-sol`）是否正确。"
---

## 快速开始

<ol class="steps">
  <li>下载 <a href="https://raw.githubusercontent.com/l1i1/CodexAppInstaller/main/CodexAppInstaller.exe">CodexAppInstaller.exe</a>（约 22KB，自包含单文件）</li>
  <li>双击运行，UAC 弹窗点"是"</li>
  <li>全程回车即可：自动下载 Codex MSIX（镜像加速 + SHA256 校验，带缓存）→ 静默安装 → 聚合 API 配置 → 创建桌面快捷方式</li>
  <li>启动 Codex App，直接使用</li>
</ol>

也可以参数直接指定：

```bat
CodexAppInstaller.exe -ApiKey sk-xxxxxx
```

![运行效果](img/runpic.png)

![应用截图](img/codexapppic.png)

## 功能特性

- **绕过下载网络限制**：多源镜像回退（gh-proxy 双前缀），官方 MSIX 也能在国内正常下载
- **安全可信**：下载后比对官方 SHA256，MSIX 为 Microsoft Store 签名官方包（未重打包，仅镜像转存）
- **静默安装**：机器范围注册 + 用户级立即注册，所有用户可用
- **聚合 API 一键配置**：桌面端 / CLI / IDE 三端共用一份 `~/.codex/config.toml`，配置一次三端生效
- **下载缓存**：跨重启持久复用，版本更新哈希变化自动失效重下
- **自包含单文件**：全 C# 实现，无第三方依赖，双击即用、自动提权

## 工作原理

### 1. 下载（多源回退）

| 优先级 | 来源 | 说明 |
|---|---|---|
| 1 | GitHub Release + gh-proxy（`v4.gh-proxy.org` → `gh-proxy.org` 双前缀回退） | 镜像仓库 [Wangnov/codex-app-mirror](https://github.com/Wangnov/codex-app-mirror)，tag 动态获取 |

- 资产名按架构（x64 / arm64）从 Release 动态匹配，如 `OpenAI.Codex_26.803.5235.0_x64__2p2nqsd0c76g0.Msix`
- .NET HttpWebRequest 流式传输（64KB 分块），进度/速度实时刷新，失败自动重试 3 次
- 下载后比对同 Release 的 `SHA256SUMS.txt`，防镜像篡改
- 缓存位于 `%LOCALAPPDATA%\CodexAppInstaller\cache`，SHA256 匹配直接复用

### 2. 静默安装 MSIX

进程内调用 PowerShell SDK：优先 `Add-AppxProvisionedPackage`（机器范围注册，所有用户可用），再补一次用户级 `Add-AppxPackage` 立即生效。安装完成后创建 `Codex.lnk` 桌面快捷方式（AUMID 通过 `Get-StartApps` 动态获取）。

### 3. 聚合 API 配置（桌面端 / CLI / IDE 三端共用）

Codex 桌面应用与 CLI、IDE 扩展共用同一份 `~/.codex/config.toml`（官方文档：agents in the app inherit the same configuration as the IDE and CLI extension）。写入：

```toml
model = "gpt-5.6-sol"
model_provider = "custom"
preferred_auth_method = "apikey"

[model_providers.custom]
name = "custom"
base_url = "https://n.tokeness.dev/v1"
wire_api = "responses"          # Codex 仅支持 Responses 协议
requires_openai_auth = true

[windows]
sandbox = "elevated"
```

API Key 写入 `~/.codex/auth.json`（`{"OPENAI_API_KEY": "sk-xxxxxx"}`），不落 `config.toml`。配置后需**完全退出并重启** Codex 桌面应用生效。

## 参数参考

| 参数 | 说明 |
|---|---|
| `-SkipApi` | 跳过聚合 API 配置 |
| `-ApiBaseUrl <url>` | 聚合端点（默认 `https://n.tokeness.dev/v1`，需 OpenAI Responses 兼容） |
| `-ApiKey <key>` | API Key（格式 `sk-xxxxxx`） |
| `-ApiModel <model>` | 模型（默认 `gpt-5.6-sol`） |
| `-SkipChecksum` | 跳过 SHA256 校验（不推荐） |
| `-SkipShortcut` | 不创建桌面快捷方式 |
| `-h` | 帮助 |

## 边界与风险

- 本程序只解决**安装环节**的网络问题（下载安装包 / 组件）。**登录账号与日常使用**仍需要能访问 OpenAI 的网络环境（代理服务）
- MSIX 来自第三方镜像但**未重打包**，按源比对官方 SHA256；对供应链敏感可自行从 Microsoft Store / winget 安装（`winget install --id 9PLM9XGG6VKS -s msstore`）
- 需要 Windows 10/11 64 位、管理员权限
- 聚合端点需 **OpenAI Responses 兼容**（`wire_api = "responses"`），纯 Chat Completions 端点无法直连

## 相关链接

- [Codex App 国内安装教程](https://mrshrawho.github.io/codex-app-install-guide/)
- [Claude Desktop 一键安装程序](https://l1i1.github.io/ClaudeDesktop-Installer/)（同系列工具）
- [Claude Desktop 国内安装教程](https://mrshrawho.github.io/claude-desktop-install-guide/)
