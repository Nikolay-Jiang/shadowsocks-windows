<img src="shadowsocks-csharp/Resources/ssw128.png" alt="[logo]" width="48"/> Shadowsocks for Windows
=======================

[![Build Status](https://github.com/Nikolay-Jiang/shadowsocks-windows/actions/workflows/build.yml/badge.svg?branch=v4)](https://github.com/Nikolay-Jiang/shadowsocks-windows/actions/workflows/build.yml)

A Windows client for Shadowsocks, including Shadowsocks 2022 (SIP022) and SIP023 user identities.

## Features

- System proxy, PAC mode and global mode
- GeoSite-based PAC rules and user-defined rules
- SOCKS5 and HTTP proxy
- UDP relay
- SIP003 plugins
- `2022-blake3-aes-128-gcm` and `2022-blake3-aes-256-gcm` over TCP and UDP
- Opt-in High Availability server switching

## Download

Download the latest release from [Releases](https://github.com/Nikolay-Jiang/shadowsocks-windows/releases).

## Requirements

Windows 10 version 2004 or later, x64. The release is self-contained; no separate .NET runtime is required.

## Quick start

1. Start `Shadowsocks.exe` and find the icon in the notification tray.
2. Add servers from **Servers -> Edit Servers...**.
3. Select a server, then enable **Enable System Proxy**.
4. For manual proxy setup, use SOCKS5 or HTTP at `127.0.0.1:1080`. The local port can be changed in **Servers -> Edit Servers...**.

For Store and WinHTTP applications, run the following as administrator:

```
netsh winhttp import proxy source=ie
```

## Shadowsocks 2022

The password field is a base64 pre-shared key:

- Single user: `BASE64_SERVER_PSK`
- SIP023 user identity: `BASE64_SERVER_PSK:BASE64_USER_PSK`

Use **Generate** next to the password field to create a key of the correct length. After typing `:`, press **Generate** again to create the user key.

## High Availability

Enable **Include in High Availability** in the server editor for every server that should participate. The option is off by default.

- The current server is kept while it is healthy.
- A failed connection switches to another participating server.
- Failed servers cool down for 15 seconds, doubling up to 5 minutes.
- TCP latency (EWMA) is used to choose between healthy servers; the client only rebalances to a clearly faster server once per minute.
- UDP sessions stay on the server they started with.

Load balancing is currently hidden.

## PAC

PAC rules are generated from [v2fly/domain-list-community](https://github.com/v2fly/domain-list-community). The default is whitelist mode with Chinese domains connected directly. Use `user-rule.txt` for custom rules; do not edit `pac.txt` if you want changes to survive updates.

## Plugins

Set the plugin path and options in the server editor. SIP003 plugins are supported. Forward Proxy is not used while a plugin is enabled.

## Hotkeys

Open **Help -> Edit Hotkeys...** to record shortcuts. Hotkeys can be registered at startup. If you run multiple instances, use different key combinations.

## Build

Visual Studio 2022 and the .NET 10 SDK are required.

```bash
dotnet build shadowsocks-windows.sln -c Release
dotnet publish shadowsocks-csharp/shadowsocks-csharp.csproj \
  -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true \
  -p:EnableCompressionInSingleFile=true
```

## License

[GPLv3](https://github.com/Nikolay-Jiang/shadowsocks-windows/blob/v4/LICENSE.txt)

## Third-party components

- Newtonsoft.Json (MIT)
- ZXing.Net (Apache 2.0)
- Privoxy (GPLv2)
- Sysproxy (https://github.com/Noisyfox/sysproxy)
