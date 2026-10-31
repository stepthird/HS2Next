HS2Next
=======

HS2Next is a scene and character editor for Honey Select 2 content. It reads the game data, cards, coordinates and
mods of your own Honey Select 2 installation where they are; it contains no game data and never writes into the game
folder.

README_ja.txt is this text in Japanese; README_zh-Hans.txt in Simplified Chinese (machine translation).


Requirements
------------
- Windows 10 or 11, 64-bit.
- A graphics card with Direct3D 12. 8 GB of video memory is recommended: the largest maps use about 2.8 GB on top of
  the app.
- Your own Honey Select 2 installation (with its mods folder if you use mods).
- Free disk space for the cache: about 0.3 GB at once, growing up to about 10 GB as repaired mods are opened.


First start
-----------
1. Extract the zip anywhere and start HS2Next.exe. There is no installer and no administrator rights are needed.
2. Windows SmartScreen may show "Windows protected your PC" with an unknown publisher, because the program is not
   code-signed. Choose "More info", then "Run anyway".
3. The first start after extracting can take up to about 10 seconds while the antivirus checks the new files. Later
   starts take about 3 seconds.
4. The first-run guide asks for the language, the Honey Select 2 folder (it suggests the folders it finds and checks
   the one you choose), extra mods folders and the cache folder, then builds the content index. The first index takes
   from a few seconds to a few minutes, depending on the mods and the disk; later starts reuse it. You may skip the guide and open it again from File > Setup guide.


Camera keys
-----------
The camera keys are on the numeric keypad (numpad 0 looks through the selected camera, Ctrl + numpad 0 adds one). Without a
numeric keypad use the Camera tab, the inspector or the right-click menu of the scene tree, or change the keys in
File > Options. The last page of the guide repeats this.


Where your files go
-------------------
- HS2Next keeps its settings, your scenes and characters and its cache in one folder per Windows user:
  %USERPROFILE%\AppData\LocalLow\stp3\HS2Next (with Config, UserData and Cache inside).
- Portable mode: create an empty file named portable.txt next to HS2Next.exe and start it again. The three folders
  are then made next to HS2Next.exe, so that folder must be writable.
- If an earlier HS2Next kept a UserData folder next to its exe, the guide offers to copy it once. The old folder is
  not changed. File > Options > Folders can copy from any earlier folder later.
- The settings file is Config\settings.local.json. settings.example.json in this package lists every setting with a
  one-line explanation. File > Options changes the common ones.


Automation bridge
-----------------
HS2Next listens on 127.0.0.1:17322 (this computer only) so that scripts can drive it. In this package every request
needs the token in UserData\Bridge\token (a new token at each start), sent in the header X-HS2Next-Token. To turn the
bridge off, use File > Options > Automation or set "bridge": {"enabled": false} in the settings file.


Graphics problems
-----------------
HS2Next starts with Direct3D 12. If the window stays black or the graphics driver resets, start it with Direct3D 11:
make a shortcut to HS2Next.exe and add -force-d3d11 at the end of its target.


Uninstalling
------------
Delete the HS2Next folder. To remove your settings, scenes, characters and cache as well, delete
%USERPROFILE%\AppData\LocalLow\stp3\HS2Next. HS2Next writes nothing into the game folder and makes no registry entries
of its own; the engine remembers the window size under HKEY_CURRENT_USER\Software\stp3\HS2Next.


Licence
-------
stp3 made HS2Next with AI coding tools: the tools wrote most of the code, and stp3 set the design, directed the work and
checked it.
HS2Next's own code is dedicated to the public domain under CC0 1.0 (LICENSE.txt). The third-party software and fonts it
contains are listed with their licences in THIRD_PARTY.txt. No game data of Honey Select 2 is included.
The package also contains runtime components of Unity, Microsoft and the Mono project (the engine, DirectX 12,
DirectStorage, the .NET class libraries) under their own terms, which THIRD_PARTY.txt names; by using HS2Next you
accept those terms for them. CC0 covers only HS2Next's own code.
