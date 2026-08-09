// CodexAppInstaller — OpenAI Codex 桌面应用（MSIX）一键安装（国内网络优化版，纯 C#）
// 功能：
//   1. 从镜像仓库（GitHub + gh-proxy 双前缀）下载官方 Store 签名 MSIX，SHA256 校验 + 下载缓存
//   2. Add-AppxProvisionedPackage（机器范围）→ 用户级立即注册（复用 Claude 版修复逻辑）
//   3. 桌面快捷方式（Get-StartApps 动态 AUMID）
// 编译：build-exe.bat（本机 .NET Framework csc + PowerShell SDK，无第三方依赖）
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Collections.Generic;
using System.Management.Automation;

namespace CodexAppInstaller
{
    internal static class Program
    {
        private static bool SkipChecksum, SkipShortcut, SkipApi;
        private static string ApiBaseUrl = "", ApiKey = "", ApiModel = "";
        private const string MirrorRepo = "Wangnov/codex-app-mirror";
        private const string DefaultApiBaseUrl = "https://n.tokeness.io/v1";
        private const string DefaultModel = "gpt-5.6-sol";
        private static readonly string[] GhProxyPrefixes = {
            "https://v4.gh-proxy.org/https://github.com",
            "https://gh-proxy.org/https://github.com"
        };

        private static string CacheDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexAppInstaller", "cache"); }
        }

        private class AppxInfo
        {
            public string Name, Version, PackageFamilyName, InstallLocation, PackageFullName;
        }

        [STAThread]
        private static int Main(string[] args)
        {
            try { Console.Title = "Codex App Installer"; } catch { }
            foreach (string a in args)
            {
                string arg = a; string val = "";
                int eq = a.IndexOf('=');
                if (eq > 0) { arg = a.Substring(0, eq); val = a.Substring(eq + 1); }
                if (arg == "-h" || arg == "-?" || arg == "--help") { PrintUsage(); Pause(); return 0; }
                if (arg == "-SkipChecksum") SkipChecksum = true;
                else if (arg == "-SkipShortcut") SkipShortcut = true;
                else if (arg == "-SkipApi") SkipApi = true;
                else if (arg == "-ApiBaseUrl") ApiBaseUrl = (eq > 0 ? val : NextArg(args, a)).Trim();
                else if (arg == "-ApiKey") ApiKey = (eq > 0 ? val : NextArg(args, a)).Trim();
                else if (arg == "-ApiModel") ApiModel = (eq > 0 ? val : NextArg(args, a)).Trim();
            }

            if (!IsAdministrator())
            {
                Console.WriteLine("请求管理员权限，请在 UAC 弹窗中点击“是”...");
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo();
                    psi.UseShellExecute = true;
                    psi.Verb = "runas";
                    psi.FileName = Process.GetCurrentProcess().MainModule.FileName;
                    StringBuilder sb = new StringBuilder();
                    foreach (string a in args) sb.Append(" \"").Append(a.Replace("\"", "\\\"")).Append("\"");
                    psi.Arguments = sb.ToString();
                    Process.Start(psi);
                    return 0;
                }
                catch (Exception ex) { Console.WriteLine("提权失败: " + ex.Message); Pause(); return 3; }
            }

            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }

            try
            {
                Banner();
                string arch = GetArch();
                Ok("系统架构: " + arch);
                string msix = GetMsix(arch);
                AppxInfo appx = InstallMsix(msix);
                if (!SkipApi) ConfigureApi();
                if (!SkipShortcut) CreateDesktopShortcut(appx);
                Step("验证安装结果");
                AppxInfo final = GetCodexAppx();
                if (final != null)
                {
                    Ok("Codex Desktop: " + final.Name + " " + final.Version);
                    Ok("PackageFamilyName: " + final.PackageFamilyName);
                }
                Console.WriteLine("\n===============================================");
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine(" 安装完成。");
                Console.ResetColor();
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine(" 注意: 登录账号与日常使用仍需可访问 OpenAI 的网络环境（代理服务）。");
                Console.ResetColor();
                Console.WriteLine("===============================================");
            }
            catch (Exception ex)
            {
                Fail("执行失败: " + ex.Message);
                Console.WriteLine(ex.StackTrace);
                Pause();
                return 1;
            }
            Pause();
            return 0;
        }

        // ================= 工具 =================
        private static void Step(string msg) { Console.WriteLine("\n==> " + msg); }
        private static string NextArg(string[] args, string current)
        {
            for (int i = 0; i < args.Length; i++)
                if (args[i] == current && i + 1 < args.Length) return args[i + 1];
            return "";
        }
        private static void Ok(string msg) { Console.WriteLine("  [OK] " + msg); }
        private static void Warn(string msg) { Console.WriteLine("  [!!] " + msg); }
        private static void Fail(string msg) { Console.WriteLine("  [XX] " + msg); }
        private static void Banner()
        {
            Console.WriteLine("===============================================");
            Console.WriteLine(" OpenAI Codex 桌面应用一键安装程序（国内网络优化版）");
            Console.WriteLine("===============================================");
        }
        private static void Pause()
        {
            try
            {
                if (Console.IsInputRedirected) return;
                Console.WriteLine("\n按任意键退出...");
                Console.ReadKey(true);
            }
            catch { }
        }
        private static bool IsAdministrator()
        {
            try
            {
                using (WindowsIdentity id = WindowsIdentity.GetCurrent())
                    return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }
        private static string GetArch()
        {
            string envArch = Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE");
            if (envArch != null && envArch.IndexOf("ARM64", StringComparison.OrdinalIgnoreCase) >= 0) return "arm64";
            if (envArch != null && envArch.IndexOf("AMD64") >= 0) return "x64";
            throw new Exception("不支持的架构: " + envArch + "（Codex 桌面应用仅支持 x64/arm64）");
        }

        // ================= 下载（流式 + 实时进度） =================
        private static bool DownloadFile(string url, string dest, string desc, long minBytes)
        {
            Console.WriteLine("  下载中: " + desc + " (" + url + ")");
            Stopwatch sw = Stopwatch.StartNew();
            bool ok = false;
            for (int attempt = 1; attempt <= 3 && !ok; attempt++)
            {
                HttpWebRequest req = null; HttpWebResponse resp = null; Stream stream = null; FileStream fs = null;
                if (File.Exists(dest)) File.Delete(dest);
                try
                {
                    req = (HttpWebRequest)WebRequest.Create(url);
                    req.Method = "GET";
                    req.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64)";
                    req.Timeout = 30000; req.ReadWriteTimeout = 60000; req.AllowAutoRedirect = true;
                    resp = (HttpWebResponse)req.GetResponse();
                    long total = resp.ContentLength;
                    stream = resp.GetResponseStream();
                    fs = File.Create(dest);
                    byte[] buf = new byte[65536];
                    long done = 0, lastBytes = 0;
                    DateTime lastTick = DateTime.Now;
                    Stopwatch progSw = Stopwatch.StartNew();
                    int n;
                    while ((n = stream.Read(buf, 0, buf.Length)) > 0)
                    {
                        fs.Write(buf, 0, n);
                        done += n;
                        if (progSw.ElapsedMilliseconds >= 200)
                        {
                            double dt = (DateTime.Now - lastTick).TotalSeconds;
                            double speed = Math.Max(done - lastBytes, 0) / Math.Max(dt, 0.001) / 1048576.0;
                            lastBytes = done; lastTick = DateTime.Now;
                            Console.Write(string.Format("\r  进度 {0,3:N0}% | {1,7:N1} / {2,7:N1} MB | {3,5:N2} MB/s ", Math.Min(100, done * 100.0 / total), done / 1048576.0, total / 1048576.0, speed));
                            progSw.Restart();
                        }
                    }
                    Console.WriteLine();
                    fs.Close(); stream.Close(); resp.Close();
                    ok = true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine();
                    Fail(string.Format("第 {0} 次下载失败: {1}", attempt, ex.Message));
                    try { if (fs != null) fs.Close(); } catch { }
                    try { if (stream != null) stream.Close(); } catch { }
                    try { if (resp != null) resp.Close(); } catch { }
                    if (attempt < 3) Thread.Sleep(2000);
                }
            }
            sw.Stop();
            if (!ok) { if (File.Exists(dest)) File.Delete(dest); return false; }
            if (!File.Exists(dest)) { Fail("下载失败: 文件不存在"); return false; }
            long size = new FileInfo(dest).Length;
            if (size < minBytes) { Fail("下载异常: 文件过小 (" + size + " bytes)"); File.Delete(dest); return false; }
            double avg = (size / 1048576.0) / Math.Max(sw.Elapsed.TotalSeconds, 0.001);
            Ok(string.Format("已下载 {0} ({1:N1} MB, 平均 {2:N2} MB/s)", dest, size / 1048576.0, avg));
            return true;
        }

        private static string GetWebString(string url)
        {
            using (WebClient wc = new WebClient())
            {
                wc.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
                return wc.DownloadString(url);
            }
        }

        // ================= 镜像仓库 =================
        private static string GetGitHubLatestTag()
        {
            try
            {
                string json = GetWebString("https://api.github.com/repos/" + MirrorRepo + "/releases/latest");
                Match m = Regex.Match(json, "\"tag_name\"\\s*:\\s*\"([^\"]+)\"");
                return m.Success ? m.Groups[1].Value : null;
            }
            catch { return null; }
        }

        // 从 release 资产中取当前架构的 MSIX 文件名（如 OpenAI.Codex_26.803.5235.0_x64__2p2nqsd0c76g0.Msix）
        private static string GetMsixAssetName(string tag, string arch)
        {
            try
            {
                string json = GetWebString("https://api.github.com/repos/" + MirrorRepo + "/releases/latest");
                Regex re = new Regex("\"name\"\\s*:\\s*\"(OpenAI\\.Codex_[^\"]*_" + arch + "__[^\"]*\\.Msix)\"", RegexOptions.IgnoreCase);
                Match m = re.Match(json);
                return m.Success ? m.Groups[1].Value : null;
            }
            catch { return null; }
        }

        private static string GetChecksum(string msixName, string tag, string ghPrefix)
        {
            try
            {
                string sumUrl = ghPrefix + "/" + MirrorRepo + "/releases/download/" + tag + "/SHA256SUMS.txt";
                string tmp = Path.Combine(Path.GetTempPath(), "Codex-SHA256SUMS.txt");
                if (DownloadFile(sumUrl, tmp, "SHA256SUMS.txt", 16))
                {
                    foreach (string line in File.ReadAllLines(tmp))
                    {
                        string[] p = line.Trim().Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                        if (p.Length >= 2 && p[1].Equals(msixName, StringComparison.OrdinalIgnoreCase))
                        { try { File.Delete(tmp); } catch { } return p[0].ToLower(); }
                    }
                    try { File.Delete(tmp); } catch { }
                }
            }
            catch { }
            return null;
        }

        private static string Sha256File(string path)
        {
            using (FileStream fs = File.OpenRead(path))
            using (SHA256Managed sha = new SHA256Managed())
                return BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "").ToLower();
        }

        private static string GetMsix(string arch)
        {
            Directory.CreateDirectory(CacheDir);
            Step("获取镜像仓库最新版本信息");
            string tag = GetGitHubLatestTag();
            if (tag == null) throw new Exception("无法获取镜像仓库最新版本");
            Ok("镜像最新 tag: " + tag);
            string msixName = GetMsixAssetName(tag, arch);
            if (string.IsNullOrEmpty(msixName))
                throw new Exception("未在 Release 中找到 " + arch + " 架构的 MSIX 资产");
            Ok("MSIX: " + msixName);

            string cacheFile = Path.Combine(CacheDir, msixName);
            // 缓存命中
            if (!SkipChecksum && File.Exists(cacheFile))
            {
                string cachedSha = Sha256File(cacheFile);
                foreach (string prefix in GhProxyPrefixes)
                {
                    string expected = GetChecksum(msixName, tag, prefix);
                    if (!string.IsNullOrEmpty(expected) && cachedSha == expected)
                    {
                        Ok("缓存命中，跳过下载: " + cacheFile + " (SHA256 匹配)");
                        return cacheFile;
                    }
                }
                Warn("缓存校验不匹配（可能是旧版本），重新下载");
            }

            foreach (string prefix in GhProxyPrefixes)
            {
                string url = prefix + "/" + MirrorRepo + "/releases/download/" + tag + "/" + msixName;
                Step("尝试下载源: " + prefix);
                if (File.Exists(cacheFile)) File.Delete(cacheFile);
                if (!DownloadFile(url, cacheFile, "Codex MSIX (" + arch + ")", 1048576)) continue;
                if (!SkipChecksum)
                {
                    string expected = GetChecksum(msixName, tag, prefix);
                    if (!string.IsNullOrEmpty(expected))
                    {
                        string actual = Sha256File(cacheFile);
                        if (actual != expected)
                        {
                            Fail("SHA256 校验失败: 期望 " + expected + " / 实际 " + actual);
                            File.Delete(cacheFile);
                            continue;
                        }
                        Ok("SHA256 校验通过: " + actual);
                    }
                    else Warn("未获取到期望校验和，跳过校验");
                }
                Ok("已缓存: " + cacheFile);
                return cacheFile;
            }
            throw new Exception("所有下载源均失败，安装中止。");
        }

        // ================= 安装（PowerShell SDK 进程内） =================
        private static List<AppxInfo> GetAppxPackages(string nameFilter)
        {
            List<AppxInfo> list = new List<AppxInfo>();
            using (PowerShell ps = PowerShell.Create())
            {
                ps.AddCommand("Get-AppxPackage");
                if (!string.IsNullOrEmpty(nameFilter)) ps.AddParameter("Name", nameFilter);
                foreach (PSObject r in ps.Invoke())
                {
                    list.Add(new AppxInfo
                    {
                        Name = GetProp(r, "Name"),
                        Version = GetProp(r, "Version"),
                        PackageFamilyName = GetProp(r, "PackageFamilyName"),
                        InstallLocation = GetProp(r, "InstallLocation"),
                        PackageFullName = GetProp(r, "PackageFullName")
                    });
                }
            }
            return list;
        }

        private static AppxInfo GetCodexAppx()
        {
            foreach (AppxInfo i in GetAppxPackages("*Codex*"))
                if (i.Name.IndexOf("ClaudeCode", StringComparison.OrdinalIgnoreCase) < 0) return i;
            return null;
        }

        private static string GetProp(PSObject obj, string name)
        {
            PSPropertyInfo pr = obj.Properties[name];
            return (pr != null && pr.Value != null) ? pr.Value.ToString() : "";
        }

        private static string CollectErrors(PowerShell ps)
        {
            StringBuilder sb = new StringBuilder();
            foreach (ErrorRecord e in ps.Streams.Error) sb.Append(e.ToString()).Append("; ");
            return sb.ToString();
        }

        private static AppxInfo InstallMsix(string msixPath)
        {
            Step("安装 MSIX 包");
            AppxInfo existing = GetCodexAppx();
            if (existing != null)
            {
                Ok("已检测到 Codex Desktop（版本 " + existing.Version + "），跳过安装（如需重装请先卸载）");
                return existing;
            }
            string err;
            if (TryProvision(msixPath, out err))
            {
                Ok("机器范围注册成功（所有用户可用）");
                // 机器范围注册不注册给当前用户，补用户级注册立即生效
                try
                {
                    if (!AddAppx(msixPath, out err)) Warn("用户级立即注册失败（重新登录后自动部署，可忽略）: " + err);
                    else Ok("用户级立即注册成功（当前会话可用）");
                }
                catch (Exception ex) { Warn("用户级立即注册异常（可忽略）: " + ex.Message); }
            }
            else
            {
                Warn("机器范围注册失败（" + (err.Length > 0 ? err : "未知错误") + "），回退用户级安装 ...");
                if (!AddAppx(msixPath, out err)) throw new Exception("Add-AppxPackage 失败: " + err);
                Ok("用户级安装成功");
            }
            AppxInfo a = GetCodexAppx();
            if (a == null) a = GetProvisionedAppx();
            if (a == null)
                throw new Exception("安装已完成，但未能检测到 Codex 包（可能需重新登录后生效）。请重新登录后检查。");
            return a;
        }

        private static bool TryProvision(string msixPath, out string err)
        {
            err = "";
            using (PowerShell ps = PowerShell.Create())
            {
                ps.AddCommand("Add-AppxProvisionedPackage")
                  .AddParameter("Online", true)
                  .AddParameter("PackagePath", msixPath)
                  .AddParameter("SkipLicense", true)
                  .AddParameter("Regions", "all");
                ps.Invoke();
                if (ps.HadErrors) { err = CollectErrors(ps); return false; }
            }
            return true;
        }

        private static bool AddAppx(string msixPath, out string err)
        {
            err = "";
            using (PowerShell ps = PowerShell.Create())
            {
                ps.AddCommand("Add-AppxPackage").AddParameter("Path", msixPath);
                ps.Invoke();
                if (ps.HadErrors) { err = CollectErrors(ps); return false; }
            }
            return true;
        }

        private static AppxInfo GetProvisionedAppx()
        {
            try
            {
                using (PowerShell ps = PowerShell.Create())
                {
                    ps.AddCommand("Get-AppxProvisionedPackage").AddParameter("Online", true);
                    foreach (PSObject r in ps.Invoke())
                    {
                        string pn = GetProp(r, "PackageName");
                        if (!string.IsNullOrEmpty(pn) && pn.IndexOf("Codex", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            return new AppxInfo
                            {
                                Name = "OpenAI.Codex",
                                Version = ExtractVersionFromPackageName(pn),
                                PackageFamilyName = GetProp(r, "PackageFamilyName"),
                                PackageFullName = pn,
                                InstallLocation = GetProp(r, "InstallLocation")
                            };
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        private static string ExtractVersionFromPackageName(string packageName)
        {
            Match m = Regex.Match(packageName, "_((\\d+\\.){3}\\d+)_");
            return m.Success ? m.Groups[1].Value : "";
        }

        // ================= 桌面快捷方式 =================
        private static string GetAumid(AppxInfo appx)
        {
            try
            {
                using (PowerShell ps = PowerShell.Create())
                {
                    ps.AddCommand("Get-StartApps");
                    foreach (PSObject r in ps.Invoke())
                    {
                        string appId = GetProp(r, "AppID");
                        if (!string.IsNullOrEmpty(appId) && appId.IndexOf(appx.PackageFamilyName, StringComparison.OrdinalIgnoreCase) >= 0)
                            return appId;
                    }
                }
            }
            catch { }
            return appx.PackageFamilyName + "!App";
        }

        private static void CreateDesktopShortcut(AppxInfo appx)
        {
            try
            {
                string aumid = GetAumid(appx);
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                string lnk = Path.Combine(desktop, "Codex.lnk");
                string script =
                    "$ws = New-Object -ComObject WScript.Shell;" +
                    "$sc = $ws.CreateShortcut('" + lnk.Replace("'", "''") + "');" +
                    "$sc.TargetPath = 'shell:AppsFolder\\" + aumid + "';" +
                    "$sc.Description = 'OpenAI Codex';" +
                    "$sc.Save()";
                using (PowerShell ps = PowerShell.Create())
                {
                    ps.AddScript(script);
                    ps.Invoke();
                    if (ps.HadErrors) throw new Exception(CollectErrors(ps));
                }
                Ok("已创建桌面快捷方式: " + lnk);
            }
            catch (Exception ex) { Warn("创建桌面快捷方式失败: " + ex.Message); }
        }

        // ================= 聚合 API 配置（Codex 桌面端/CLI/IDE 共用 ~/.codex/config.toml） =================
        private static string SafeReadLine()
        {
            try { return Console.ReadLine() ?? ""; }
            catch { return ""; }
        }

        private static void ConfigureApi()
        {
            string baseUrl = ApiBaseUrl != null ? ApiBaseUrl.Trim() : "";
            if (string.IsNullOrEmpty(baseUrl)) baseUrl = DefaultApiBaseUrl;
            string apiKey = ApiKey != null ? ApiKey.Trim() : "";
            string model = ApiModel != null ? ApiModel.Trim() : "";
            if (string.IsNullOrEmpty(apiKey))
            {
                Console.WriteLine();
                Console.Write("是否配置聚合 API（OpenAI Responses 兼容端点）？[Y/n] ");
                string ans = SafeReadLine();
                bool yes = string.IsNullOrWhiteSpace(ans) ||
                           ans.Trim().ToLower() == "y" || ans.Trim().ToLower() == "yes";
                if (yes)
                {
                    Console.Write("  聚合 Base URL [默认 " + DefaultApiBaseUrl + "]: ");
                    string b = SafeReadLine().Trim();
                    if (!string.IsNullOrEmpty(b)) baseUrl = b;
                    Console.Write("  API Key（格式 sk-xxxxxx）: ");
                    apiKey = SafeReadLine().Trim();
                }
            }
            if (string.IsNullOrEmpty(model)) model = DefaultModel;
            if (string.IsNullOrEmpty(apiKey))
            {
                if (!SkipApi) Warn("未提供 API Key，跳过聚合 API 配置（可加 -ApiKey <key> 或稍后重跑）");
                return;
            }
            if (!apiKey.StartsWith("sk-"))
                Warn("提示: API Key 通常以 sk- 开头，请确认格式（示例 sk-xxxxxx）");
            Step("配置聚合 API: " + baseUrl + "，模型: " + model);
            WriteConfigToml(baseUrl, model);
            WriteAuthJson(apiKey);
            Ok("聚合 API 配置完成。请完全退出并重启 Codex 桌面应用生效。");
        }

        // ~/.codex/config.toml（三端共用；备份原文件后重写本程序管理的键）
        private static void WriteConfigToml(string baseUrl, string model)
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string dir = Path.Combine(home, ".codex");
            string path = Path.Combine(dir, "config.toml");
            Directory.CreateDirectory(dir);
            if (File.Exists(path))
            {
                try { File.Copy(path, path + ".bak", true); Ok("已备份原配置: config.toml.bak"); }
                catch { }
            }
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("# Generated by CodexAppInstaller");
            sb.AppendLine("model = \"" + EscapeToml(model) + "\"");
            sb.AppendLine("model_provider = \"custom\"");
            sb.AppendLine("preferred_auth_method = \"apikey\"");
            sb.AppendLine();
            sb.AppendLine("[model_providers.custom]");
            sb.AppendLine("name = \"custom\"");
            sb.AppendLine("base_url = \"" + EscapeToml(baseUrl) + "\"");
            sb.AppendLine("wire_api = \"responses\"");
            sb.AppendLine("requires_openai_auth = true");
            sb.AppendLine();
            sb.AppendLine("[windows]");
            sb.AppendLine("sandbox = \"elevated\"");
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            Ok("已写入: " + path);
        }

        // ~/.codex/auth.json 存 API Key（桌面端从桌面启动不读 shell 环境变量，auth.json 最可靠）
        private static void WriteAuthJson(string apiKey)
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string dir = Path.Combine(home, ".codex");
            string path = Path.Combine(dir, "auth.json");
            Directory.CreateDirectory(dir);
            string json = "{\n  \"OPENAI_API_KEY\": \"" + EscapeJson(apiKey) + "\"\n}\n";
            File.WriteAllText(path, json, Encoding.UTF8);
            Ok("已写入 API Key: " + path);
        }

        private static string EscapeToml(string s)
        {
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private static string EscapeJson(string s)
        {
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private static void PrintUsage()
        {
            Console.WriteLine("OpenAI Codex 桌面应用一键安装程序（国内网络优化版，纯 .NET 实现）");
            Console.WriteLine();
            Console.WriteLine("用法: CodexAppInstaller.exe [参数...]");
            Console.WriteLine();
            Console.WriteLine("参数:");
            Console.WriteLine("  -SkipChecksum    跳过 SHA256 校验（不推荐）");
            Console.WriteLine("  -SkipShortcut    不创建桌面快捷方式");
            Console.WriteLine("  -SkipApi         跳过聚合 API 配置");
            Console.WriteLine("  -ApiBaseUrl <url> 聚合端点（默认 " + DefaultApiBaseUrl + "，需 OpenAI Responses 兼容）");
            Console.WriteLine("  -ApiKey <key>    API Key（格式 sk-xxxxxx）");
            Console.WriteLine("  -ApiModel <model> 模型（默认 " + DefaultModel + "）");
        }
    }
}
