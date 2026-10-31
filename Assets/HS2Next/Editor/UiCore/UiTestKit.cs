// Shared helpers of the UI core tests (plain Mono through Tools/mono_tests.ps1 -Suite ui).
// The fixtures folder is Assets/HS2Next/Resources/UI (keymap presets, strings.tsv). Only the public surface of the core
// is used. The usings sit inside the namespace so that `TestRunner` binds to the save-format runner (the Unity Editor
// assembly also sees a global `TestRunner` namespace).
using System;
using System.IO;
using System.Text;

namespace HS2Next.UI.Core.Tests
{
    using HS2Next.Save.Tests;
    using HS2Next.UI.Core;

    public static class UiKit
    {
        public static string ResourcesUi(TestRunner t) => t.FixturesDir ?? "";

        public static string ReadText(TestRunner t, string relative) =>
            File.ReadAllText(Path.Combine(ResourcesUi(t), relative), new UTF8Encoding(false));

        public static Keymap Preset(TestRunner t, string name) => Keymap.FromJson(ReadText(t, Path.Combine("Keymaps", name + ".json")));

        public static L10nTable Strings(TestRunner t) => L10nTable.Parse(ReadText(t, Path.Combine("L10n", "strings.tsv")));
    }
}
