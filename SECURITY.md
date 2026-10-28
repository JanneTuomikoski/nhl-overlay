# Security

## Reporting a vulnerability

Please do not open a public issue for a security problem. Use GitHub's private vulnerability
reporting instead: the **Security** tab of this repository, then **Report a vulnerability**.
You should hear back within a week.

## Supported versions

Only the latest release is supported. Fixes ship as a new release. The exe bundles the .NET
runtime and there is no automatic update, so runtime security fixes also arrive only with a
new release.

## What the app does

- Talks only to `api-web.nhle.com`, `assets.nhle.com` and, when Liiga is enabled, `liiga.fi`,
  over HTTPS, without accounts, credentials or API keys.
- Stores settings, state, logs and a logo cache under `%AppData%\NHLOverlay` and
  `%LocalAppData%\NHLOverlay`, or next to the exe in portable mode.
- Runs as the current user without elevation. For the gaming profile it reads only the name
  of the foreground process; it does not hook, inject into or read the memory of other
  processes.
- Registers itself in the per-user Run key only when "Launch with Windows" is enabled.
