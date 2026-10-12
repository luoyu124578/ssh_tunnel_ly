# ssh_tunnel_ly

Windows 图形界面的 **SSH 端口转发**小工具（`ssh -L` / `ssh -R` 的等价实现），
**单文件、免安装、运行时不在同级目录释放任何文件**。

## 这是什么

用 SSH 做端口转发，等价于命令行：

```
ssh -N -L 127.0.0.1:13080:127.0.0.1:3080 root@192.168.3.161
ssh -N -R 127.0.0.1:19080:127.0.0.1:18999 root@192.168.3.161
```

程序把这件事做成界面：多条隧道同时运行、断线自动重连、密码/私钥两种认证、
配置与密码加密保存在用户目录、可最小化到托盘、可随 Windows 登录自启。

## 运行要求

- Windows 7 SP1 及以上（含 Windows 10 / 11），**需要 .NET Framework 4.8**（Win10 1809 起系统自带）。
- 只有 `bin\ssh_tunnel_ly.exe` 一个文件，双击即可运行；不会在自身目录写任何东西。

## 主要功能

- **本地转发 `-L`**：在本机监听端口，把流量经 SSH 服务器送到目标地址。
- **远程转发 `-R`**：在 SSH 服务器上监听端口，把流量送回本机（需要服务端允许，
  默认只监听服务器回环；对外开放需要服务端 `sshd` 配置 `GatewayPorts yes`）。
- 监听地址留空 = 本机/服务器回环；填 `0.0.0.0` = 监听所有 IPv4 地址（本机转发会逐个地址绑定）。
- 目标地址留空 = 回环；地址与端口都会校验，密码支持 DPAPI 加密保存。
- 每条隧道可单独勾选"开机自启"；程序可最小化到托盘、随 Windows 登录启动（`--min` 静默）。
- 日志面板可复制/清空，可选写日志文件（超过 1 MB 自动轮转）。

## 配置文件位置

```
%APPDATA%\ssh_tunnel_ly\config.json      配置（密码用 DPAPI 加密，只有当前 Windows 用户能解）
%APPDATA%\ssh_tunnel_ly\logs\            可选日志
```

## 关于杀毒软件误报

本程序会监听本地端口并建立 SSH 出站连接，这类"无签名小体积 exe + 建监听 + 外连"的行为
容易被安全软件的主动防御/信誉引擎提示。程序已做这些加固：

- 完整的程序集元数据（公司/产品/版本/描述/版权），版本资源语言为**中文（简体，中国）**；
- **确定性构建**：同样的源码构建出字节一致的 exe，哈希可复现，便于加白后长期有效；
- 不加壳、不压缩、不释放附属文件（PE 只有 `.text` / `.rsrc` / `.reloc` 三个节区，文件尾无附加数据）。

如果被拦截，建议把 `ssh_tunnel_ly.exe` 加入杀软信任区，或通过弹窗里的"误报反馈"提交。

## 源码与构建

源码在本仓库的 `src\` 与 `build\` 目录中。本 Release 的 exe 由仓库内的
`.github\workflows\release.yml` 在 GitHub Actions 的 Windows runner 上构建：
先从 nuget.org 拉取 Roslyn 编译器、ILRepack、SSH.NET 到 `build\tools\`，再执行 `build.ps1`。

本地复现同一构建：

```
git clone https://github.com/luoyu124578/ssh_tunnel_ly.git
cd ssh_tunnel_ly
powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1
```

## 已知限制

- 只支持 `-L` / `-R`，没有实现动态转发（SOCKS `-D`）。
- 算法集合来自 SSH.NET 2024.1.0：不含 `chacha20-poly1305` 与 AES-GCM，协商后使用 `aes*-ctr`。
- 不做主机指纹持久化确认（行为类似 `ssh -o StrictHostKeyChecking=no`）。
- 每条隧道各自建立一条独立 SSH 连接（无连接池）。
