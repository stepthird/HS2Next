# Third-party software

HS2Next's own code is dedicated to the public domain under CC0 1.0 (`LICENSE`). This page lists everything else: the fonts
copied into the repository, the libraries and Unity packages the build compiles in, the runtime components of the release
zip, and the names of other people's works that the code keeps so that existing data loads. The release zip carries this
page as `THIRD_PARTY.txt`.

## In the repository

### Noto Sans CJK (Sans2.004, region subsets JP and SC)

- Used for the Studio UI's text: `NotoSansJP-Regular.otf`, `NotoSansJP-Bold.otf`, `NotoSansSC-Regular.otf`,
  `NotoSansSC-Bold.otf` in `Assets/HS2Next/Fonts/NotoSansCJK/`, with `OFL.txt` and `SOURCE.md` (upstream URLs and
  SHA-256). The player ships them through the font assets of `Assets/HS2Next/Resources/UI/Fonts/`.
- Upstream: <https://github.com/notofonts/noto-cjk>, release `Sans2.004`, `Sans/SubsetOTF/JP` and `Sans/SubsetOTF/SC`.
- Licence: SIL Open Font License 1.1 (`Assets/HS2Next/Fonts/NotoSansCJK/OFL.txt`, the upstream `LICENSE`). The fonts are
  redistributed unmodified; the font software may be bundled with software but not sold by itself.

## Fetched by the Unity Package Manager and compiled into the player

These come from `Packages/manifest.json` and are not copied into the repository. Their assemblies and shaders are part of
every player build, so the release zip contains them.

### Newtonsoft.Json 13.0.2

- Used by the save format code (`Assets/HS2Next/Runtime/Save`), the bridge and plugins (the app provides it to plugins).
  The `HS2Next.Api` assembly does not use it.
- Delivered through the Unity package `com.unity.nuget.newtonsoft-json` 3.2.2 ("currently synced to version 13.0.2"):
  `HS2Next_Data/Managed/Newtonsoft.Json.dll` in the release zip.
- Licence: MIT. Licence texts: the package's `Third Party Notices.md` (Newtonsoft.Json, MIT, Copyright (c) 2007 James
  Newton-King; also the Unity packaging projects Json.Net.Unity3D, Newtonsoft.Json-for-Unity and com.newtonsoft.json, all
  MIT) and `LICENSE.md` (the package wrapper: Unity Companion License); upstream
  <https://github.com/JamesNK/Newtonsoft.Json/blob/master/LICENSE.md>.

```text
The MIT License (MIT)

Copyright (c) 2007 James Newton-King

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated
documentation files (the "Software"), to deal in the Software without restriction, including without limitation the
rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit
persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the
Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE
WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR
COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
```

### Unity packages

| Package | Version | In the player as |
| --- | --- | --- |
| `com.unity.render-pipelines.universal` (URP) | 17.6.0 | `Unity.RenderPipelines.Universal.*.dll` and the URP shaders |
| `com.unity.render-pipelines.core` | 17.6.0 | `Unity.RenderPipelines.Core.*.dll`, `Unity.RenderPipelines.GPUDriven.Runtime.dll`, `Unity.PathTracing.Runtime.dll`, `Unity.UnifiedRayTracing.Runtime.dll`, `Unity.SurfaceCache.Runtime.dll` |
| `com.unity.shadergraph` | 17.6.0 | `Unity.RenderPipelines.ShaderGraph.ShaderGraphLibrary.dll` |
| `com.unity.ugui` | 2.6.0 | `UnityEngine.UI.dll`, `Unity.TextMeshPro.dll` |
| `com.unity.collections` | 6.6.0 | `Unity.Collections*.dll` |
| `com.unity.profiling.core` | 1.0.3 | `Unity.Profiling.Core.dll` |
| `com.unity.burst` | 2.0.0 | `Unity.Burst.dll` |

Licence: each of these packages except `com.unity.burst` carries a `LICENSE.md` that licenses it "under the Unity Companion
License for Unity-dependent projects" (<https://unity3d.com/legal/licenses/unity_companion_license>). The `com.unity.burst`
package of this editor version has no licence file in its package folder; the Burst package's published licence reads:
copyright Unity Technologies, the source code of the package under the Unity Companion License, the package otherwise under
the Unity Package Distribution License. The release zip holds its managed part `Unity.Burst.dll` and no Burst-compiled
native library.

URP and the core package also carry third-party notices for code compiled into their shaders and assemblies. Their texts,
as the packages' notice files give them:

**FXAA 3.11** (URP's `Shaders/PostProcessing/FXAA3_11.hlsl`; the FXAA anti-aliasing option):

```text
Copyright (c) 2014-2015, NVIDIA CORPORATION. All rights reserved.

Redistribution and use in source and binary forms, with or without modification, are permitted provided that the
following conditions are met:
 * Redistributions of source code must retain the above copyright notice, this list of conditions and the following
   disclaimer.
 * Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following
   disclaimer in the documentation and/or other materials provided with the distribution.
 * Neither the name of NVIDIA CORPORATION nor the names of its contributors may be used to endorse or promote products
   derived from this software without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS ``AS IS'' AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT
SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR
CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR
PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR
TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE
POSSIBILITY OF SUCH DAMAGE.
```

**RadeonRays 4.1** (the core package's unified ray tracing; MIT License):

```text
Copyright (c) 2021 Advanced Micro Devices, Inc. All rights reserved.

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated
documentation files (the "Software"), to deal in the Software without restriction, including without limitation the
rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit
persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the
Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE
WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR
COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
```

**Bullet Physics SDK** (the core package's path tracing; zlib License):

```text
Bullet Continuous Collision Detection and Physics Library
http://bulletphysics.org

This software is provided 'as-is', without any express or implied warranty. In no event will the authors be held liable
for any damages arising from the use of this software. Permission is granted to anyone to use this software for any
purpose, including commercial applications, and to alter it and redistribute it freely, subject to the following
restrictions:

1. The origin of this software must not be misrepresented; you must not claim that you wrote the original software. If
   you use this software in a product, an acknowledgment in the product documentation would be appreciated but is not
   required.
2. Altered source versions must be plainly marked as such, and must not be misrepresented as being the original
   software.
3. This notice may not be removed or altered from any source distribution.
```

**Sobol sampler** (the core package's sampling; MIT License):

```text
Copyright (c) 2023 Leonhard Gruenschloss

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated
documentation files (the "Software"), to deal in the Software without restriction, including without limitation the
rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit
persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the
Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE
WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR
COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
```

## Runtime components of the release zip

A Unity Windows player consists of the engine runtime and the files Unity's build places next to it. HS2Next's release zip
carries them unmodified. They are not HS2Next's code and are not covered by its CC0 dedication: each stays under its owner's
terms, named below, and whoever uses the release accepts those terms for these files. The versions and copyright lines are
the ones the files themselves state (their version resources); none of these files comes with a licence file of its own.

| Component | Files in the zip | Version | Vendor (copyright line) | Terms |
| --- | --- | --- | --- | --- |
| Unity runtime | `HS2Next.exe` (the Unity launcher with HS2Next's icon), `UnityPlayer.dll`, `UnityCrashHandler64.exe`, the engine data in `HS2Next_Data/`, the `UnityEngine.*` and `Unity.*` engine assemblies in `HS2Next_Data/Managed/` | 6000.6.5 | Unity Technologies ("(c) 2005-2026 Unity Technologies. All rights reserved.") | Unity Editor Software Terms (the Unity Runtime) |
| Mono runtime and class libraries | `MonoBleedingEdge/` (`mono-2.0-bdwgc.dll`, `MonoPosixHelper.dll`, `etc/mono/`), `mscorlib.dll`, `netstandard.dll`, the Mono `System*.dll` and `Mono.Security.dll` in `HS2Next_Data/Managed/` | assemblies 4.6.57 | Mono development team ("(c) Various Mono authors") | MIT License |
| .NET libraries | `System.Collections.Immutable.dll` (8.0), `System.Reflection.Metadata.dll` (8.0), `System.Runtime.CompilerServices.Unsafe.dll` (6.1) in `HS2Next_Data/Managed/` | as listed | Microsoft Corporation | MIT License |
| DirectX 12 Agility SDK (core only) | `D3D12/D3D12Core.dll` | 1.618.1 | Microsoft Corporation | Microsoft Software License Terms, Microsoft DirectX (distributable code) |
| DirectStorage | `dstorage.dll`, `dstoragecore.dll` | 1.3.2506.2501 | Microsoft Corporation | Microsoft Software License Terms, Microsoft DirectStorage SDK (distributable code) |

- **Unity runtime.** Under Unity's Editor Software Terms the Unity Runtime may be distributed only as an integrated part of
  the application it is embedded in. It remains Unity's software under Unity's terms. Unity's notice, which the app also
  shows at the end of File > Options > Advanced:

  > HS2Next was made with Unity®. Unity is a trademark or registered trademark of Unity Technologies
  >
  > Copyright © 2005-2026 Unity Technologies. All rights reserved.

- **Mono.** Unity's fork of Mono states that the runtime and its class libraries are licensed under the MIT License. A few
  third-party parts of Mono's class-library sources carry other terms (for example SharpZipLib and RabbitMQ.Client); none of
  those assemblies is in the release zip. `System.ComponentModel.Composition.dll` carries Microsoft's copyright line as part
  of Mono's class libraries.
- **.NET libraries.** From the .NET runtime (dotnet/runtime), MIT License (the licence expression of their NuGet packages).
- **DirectX 12 Agility SDK and DirectStorage.** Microsoft licenses these binaries as distributable code. Whoever distributes
  them inside an application must add significant primary functionality to them, must have distributors and end users agree
  to terms that protect them and Microsoft at least as much as Microsoft's licence, indemnifies Microsoft against claims
  arising from the application, may not use Microsoft's trademarks in a way that suggests endorsement, and may never make the
  binaries subject to a licence that requires the disclosure of source code. Unity's build places both in every Windows
  player; HS2Next does not add them itself. By using the release you agree to Microsoft's terms for these files.

## Names used for interoperability

HS2Next reads asset bundles, cards and pose files that users already have in their own game install and ships none of them
(`CLEANROOM.md`). To read that data it keeps some names that belong to others. They are names and layouts only, kept so
that existing data loads; no code of their owners is included.

- **Receiving stubs** (`Assets/Compat/`, `Assets/Plugins/Compat/`): C# classes that carry only assembly, namespace and type
  names and the serialized field layout, so that Unity can deserialize the components stored in the bundles. They are
  generated by `Tools/gen_compat_stubs.py` from the TypeTrees inside the bundles of the user's install (scanned by
  `Tools/typetree_scan.py`); they have no methods and contain no code of the game, of the mods or of the libraries they
  name. The assembly names are the ones the bundles reference: the game's (`Assembly-CSharp`, `Assembly-UnityScript`),
  third-party Unity assets and libraries (for example Bakery, Aura 2, Obi, Mesh Baker, UniRx, Final IK, Unity's Standard
  Assets and its old Timeline and Networking modules) and mod assemblies (for example HooahComponents, ModBoneImplantor,
  TopBAnim). `Assets/Compat/compat_manifest.json` lists every class with the number of bundle objects that use it.
- **Compatible shaders** (`Assets/HS2Next/Shaders/`): so that the materials of mods and asset packs can be read, HS2Next has
  its own compatible shaders for their shaders, named `HS2Next/Compatible/…`. A compatible shader declares the original
  shader's property names, types and defaults, because the materials in the bundles refer to values by name, and HS2Next
  maps a material to it by the original shader name. The compatible shaders were written from scratch by measuring rendered
  output (controlled inputs rendered with the original, the compatible shader written and compared); no compiled shader
  code was read (`Assets/HS2Next/Shaders/README.md`). They are not the work of the original authors, who have not endorsed
  or supported HS2Next. The end of File > Options > Advanced lists the families below. The original names, including the mod and author names
  that are part of them, are kept as data names. The replacements of the game's own shaders and of Unity's built-in
  shaders keep names of the form `HS2Next/<name>`.

  | Family | Shaders | Compatible shaders (`HS2Next/Compatible/` + the name) |
  | --- | --- | --- |
  | Amplify Shader Editor samples | 1 | `ASESampleShaders/Translucency` |
  | FPS Pack | 2 | `Effects/FPS_Pack/AlphaBlended`, `Effects/FPS_Pack/GlowAdditiveNoFade` |
  | FireWing | 2 | `FireWing/Wing_Main`, `FireWing/Wing_Sub` |
  | GAPH | 1 | `GAPH Custom Shader/Integrated Particle/Integrated Particle (ValueControl)` |
  | GDC | 3 | `Disolve GDC`, `GDC/Clothes True Cutoff`, `GDC/Item Cutoff` |
  | GMCC | 1 | `Bunker/Rim` |
  | Hanmen | 12 | `Hanmen/Clothes Cutoff v0.9`, `Hanmen/Clothes True Alpha (v2 copy)`, `Hanmen/Clothes True Cutoff`, `Hanmen/Clothes True Cutoff (v2 copy)`, `Hanmen/Clothes True Pantyhose`, `Hanmen/Clothes True Replica Opaque`, `Hanmen/Clothes True Transparent`, `Hanmen/Item Alpha`, `Hanmen/Item Cutoff`, `Hanmen/Item Cutoff (pattern copy)`, `Hanmen/Item Cutoff (v0.9 copies)`, `Hanmen/Item Transparent` |
  | Hovl | 3 | `Hovl/Particles/Add_CenterGlow`, `Hovl/Particles/Blend_CenterGlow`, `Hovl/Particles/Blend_TwoSides` |
  | KriptoFX | 2 | `KriptoFX/ME/Distortion`, `KriptoFX/ME/Particle` |
  | Mochie | 1 | `Mochie/Standard` |
  | NatureManufacture | 1 | `Water Particles Foam` |
  | Quantum Theory | 1 | `Quantum Theory/Particles/HDR Alpha Blended` |
  | Standard DS | 2 | `Standard DS (Specular setup)(flipped back faces)`, `Standard DS (flipped back faces)` |
  | UBER | 2 | `UBER - Metallic Setup/ Core`, `UBER - Metallic Setup/2 Sided/ Core` |
  | Unify wiki mirror | 2 | `FX/MirrorReflection`, `FX/MirrorReflection_Transparent` |
  | Water assets | 1 | `Water` |
  | hooh | 19 | `Custom/SurfaceShader`, `RMAShaderCutout`, `RMAShaderCutoutAccessory`, `RMAShaderDither`, `RMAShaderOpaque`, `RMAShaderOpaqueCullOff`, `hooh/Accessory/RMA/Cutout`, `hooh/Accessory/RMA/Opqaue`, `hooh/ClotheReplicaAlpha`, `hooh/ClotheReplicaAlphaCullOff`, `hooh/ClotheReplicaAlphaCullOff (29-property copy)`, `hooh/ClotheReplicaOpaque`, `hooh/ClotheReplicaOpaque (GlossMetalMap copy)`, `hooh/Clothing/RMA/MaskedTransparent`, `hooh/Clothing/RMA/Opqaue`, `hooh/Clothing/RMA/Transparent`, `hooh/ColorableObjectReplica`, `hooh/ColorableObjectReplica (MetallicSmoothness1 copy)`, `hooh/StudioItemStandard` |
  | simple_two_side | 1 | `simple_two_side` |

- **Plugin data read for compatibility**: so that what users made with them stays usable, HS2Next reads, with its own
  code, the data that the community plugins below store in character cards, outfit cards and pose files, under the data
  ids those plugins use, and the mod packages (zipmod) of Sideloader. It contains none of their code, and none of it runs.
  HS2Next is not the work of their authors, who have not endorsed or supported it. The end of File > Options > Advanced
  lists them: ABMX, AdditionalAccessoryControls, DynamicBoneEditor, ExtendedSave, InvisibleBody, KCOX, KSOX,
  MaterialEditor, MoreAccessories, PoseTools, PushUp, Sideloader, UncensorSelector. ExtendedSave is listed for its
  container format of the extension data, PoseTools for the data it adds to pose files. The data of other plugins is kept
  as stored and written back unchanged, not read.
- **Other data names** in code and tests: bone and transform names, list columns and categories, material property names,
  enum numbers and zipmod GUIDs as the game data and the mods define them.
- These names identify the original works so that their data can be read. They do not imply that the authors of those
  works endorse or support HS2Next.
