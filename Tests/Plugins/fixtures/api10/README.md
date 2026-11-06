# Api 1.0.0 reference assemblies

`HS2Next.Api.dll` and `HS2Next.Api.Unity.dll` exactly as the 1.1.0 player build (commit `800600c`) shipped them in
`HS2Next_Data/Managed/`: Api 1.0.0, assembly version 1.0.0.0. They are plain copies, never rebuilt, edited or replaced.

| File | Bytes | SHA-256 |
|---|---|---|
| `HS2Next.Api.dll` | 33,792 | `a7395860a3a2ff087b57ae46be524559029c00ebc5746eb8bcb9d54914df2e31` |
| `HS2Next.Api.Unity.dll` | 7,168 | `ef457d87d98d8e7698e65fc95b1b4a2d4cf165f49f63699350c8d81f566f5461` |

The compatibility checks build the 1.0 plugins (the Sequencer sample and the test plugins under `Tests/Plugins`) against
these two files and load them in the current app: a plugin built against Api 1.0 keeps loading on every later 1.x host.
`Tools/plugins.ps1 -ApiDir Tests/Plugins/fixtures/api10` and `Tools/plugin_player_check.py --api-dir
Tests/Plugins/fixtures/api10` build against them. The binary string scan of that player build found no path, address or
token in either file.
