using System.Reflection;
using System.Resources;
using System.Runtime.InteropServices;

// 程序集元数据。
//
// 为什么要有这个文件：以前 bin\ssh_tunnel_ly.exe 的版本信息完全是空的
// （FileVersion/ProductVersion = 0.0.0.0，公司/产品/说明/版权全无），
// 这种"三无"二进制正是杀毒软件启发式评分（360 的 QVM / 主动防御、Defender 的
// 云端信誉）重点照顾的对象——2026-10-09 360 主动防御就把它判成了 Trojan.Generic。
// 有了完整、稳定、人类可读的版本信息后，云端信誉与启发式打分都会明显好转。
//
// 版本号改动时请同时更新 README 的更新记录。

[assembly: AssemblyTitle("ssh_tunnel_ly")]
[assembly: AssemblyDescription("SSH 本地/远程端口转发工具（ssh -L / -R），单文件免安装")]
[assembly: AssemblyProduct("ssh_tunnel_ly")]
[assembly: AssemblyCompany("luoyu124578")]
[assembly: AssemblyCopyright("Copyright (C) 2026 luoyu124578")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]
[assembly: AssemblyInformationalVersion("1.0.0")]
[assembly: NeutralResourcesLanguage("zh-CN")]
[assembly: ComVisible(false)]
[assembly: Guid("7b5c3b4e-9f4d-4e3a-9c2b-1a6e5d4c3b2a")]
