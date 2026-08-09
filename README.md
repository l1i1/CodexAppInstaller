# OpenAI Codex 桌面应用一键安装程序（国内网络优化版，纯 C#）

Windows 上绕过网络限制，一键安装 OpenAI Codex 桌面应用（MSIX），自动 SHA256 校验、机器范围注册、创建桌面快捷方式。

## 文件

| 文件 | 说明 |
|---|---|
| `CodexAppInstaller.exe` | 最终分发物（全 C# 自包含，双击即用，自动提权） |
| `CodexAppInstaller.cs` | 完整源码 |
| `build-exe.bat` | 重新编译脚本（本机 .NET Framework csc + PowerShell SDK） |

## 用法

```bat
CodexAppInstaller.exe            :: 一键安装（下载 → 校验 → 安装 → 聚合 API 配置 → 快捷方式）
CodexAppInstaller.exe -SkipApi   :: 跳过聚合 API 配置
CodexAppInstaller.exe -ApiBaseUrl https://n.tokeness.io/v1 -ApiKey sk-xxxxxx
CodexAppInstaller.exe -ApiModel gpt-5.6-sol
CodexAppInstaller.exe -SkipChecksum / -SkipShortcut
CodexAppInstaller.exe -h
```

## 聚合 API 配置（Codex 桌面端 / CLI / IDE 三端共用）

Codex 桌面应用与 CLI、IDE 扩展**共用同一份 `~/.codex/config.toml`**（官方文档：agents in the app inherit the same configuration as the IDE and CLI extension），配置一次三端生效。

交互默认 Y，回车采用默认端点 `https://n.tokeness.io/v1` 与模型 `gpt-5.6-sol`。写入：

- `~/.codex/config.toml`（原配置备份 `.bak`）：
  ```toml
  model = "gpt-5.6-sol"
  model_provider = "custom"
  preferred_auth_method = "apikey"

  [model_providers.custom]
  name = "custom"
  base_url = "https://n.tokeness.io/v1"
  wire_api = "responses"          # Codex 仅支持 Responses 协议
  requires_openai_auth = true

  [windows]
  sandbox = "elevated"
  ```
- `~/.codex/auth.json`：`{"OPENAI_API_KEY": "sk-xxxxxx"}`（Key 不落 config.toml；桌面端从桌面启动不读 shell 环境变量，auth.json 最可靠）

配置后需**完全退出并重启** Codex 桌面应用生效。

## 工作原理

1. **下载源**（镜像仓库 [Wangnov/codex-app-mirror](https://github.com/Wangnov/codex-app-mirror)，tag 动态获取）：
   - `v4.gh-proxy.org` → `gh-proxy.org` 双前缀回退
   - 资产名从 GitHub API 按架构（x64 / arm64）动态匹配（如 `OpenAI.Codex_26.803.5235.0_x64__2p2nqsd0c76g0.Msix`）
2. **SHA256 校验**：比对同 Release 的 `SHA256SUMS.txt`
3. **下载缓存**：`%LOCALAPPDATA%\CodexAppInstaller\cache`（跨重启持久，哈希匹配直接复用）
4. **安装**：`Add-AppxProvisionedPackage`（机器范围）→ 补用户级注册立即生效 → 兜底 `Get-AppxProvisionedPackage` 检测
5. **桌面快捷方式**：`Get-StartApps` 动态获取 AUMID，`Codex.lnk` 指向 `shell:AppsFolder\<AUMID>`

## 说明

- Codex 桌面应用为 Microsoft Store 签名 MSIX（官方包，未重打包），镜像仅转存
- 需 Windows 10/11 64 位、管理员权限
- 登录账号与日常使用仍需可访问 OpenAI 的网络环境（代理服务）
