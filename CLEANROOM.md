# Clean-room method

HS2Next is a new implementation. It contains no code of Honey Select 2, of its plugins or of the mods whose data it
reads, and it ships none of their assets. This page states how the code was written.

- **Behaviour only.** Decompiled code of the game and of plugins was read only to learn what they do. Nothing was
  copied, translated line by line or transliterated.
- **Written specifications.** What was learned is written down as behaviour: prose, data facts and measured numbers,
  each statement with the evidence it rests on. The specifications describe results and rules, not code; they are not
  part of this repository.
- **Implementation from the specifications.** The code is written from those specifications and from measurements,
  not from decompiled sources. Comments describe behaviour and name no method of the game or of a plugin.
- **Black-box fits.** Shaders and other numeric behaviour (for example the shape sliders) were fitted by probing:
  controlled inputs, outputs measured in the running game, comparison with the replacement. No compiled shader code is
  read (`Assets/HS2Next/Shaders/README.md`).
- **The evidence is in the repository.** The receiving stubs are generated from the bundles' TypeTrees by
  `Tools/gen_compat_stubs.py`. The probe jobs of the shader fits (material property names and values, no game data) are
  in `Tests/FitCases/`, and the fit tools that run them in `Assets/HS2Next/Editor/Fit/`. The shape-slider tables come
  from measurements of the running game (`Tools/shape_measure.py`, `Tools/shape_fit.py`, records in
  `Tools/survey/shape/`).
- **Data facts only.** The repository keeps what the data needs to load: type and field names derived from the
  bundles' TypeTrees (the receiving stubs), shader and property names, bone names, list columns, enum numbers and
  similar names (`THIRD_PARTY.md`, "Names used for interoperability").
- **No Illusion asset is shipped.** No model, texture, animation, bundle, list file or card of the game or of mods is
  part of the repository or of a build.
- **Users import what they own.** HS2Next reads the user's own installed game and mods at run time; characters,
  coordinates and poses come from files the user has.
- **Comparisons by measurement.** Tests compare HS2Next with the running game through recorded measurements (numbers
  and captures); no game code runs inside HS2Next.
