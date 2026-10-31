// Checks of the Phase 2.4 integration package F, further methods of the partial
// BridgeCoreTest (run and reported with C1–C17; -only C18,C19):
//   C18  undo:false inside a batch: every member joins the batch's one history entry (also an unrecorded structural
//        member, which outside a batch applies at once and clears the history), so one undo / redo restores the
//        documents before / after the batch and the history before the batch survives
//   C19  the bridge token of the settings: -bridgeToken alone (random) or with a value, bridge.token true / false /
//        a string in the settings JSON, precedence, and the token the host is started with
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HS2Next.App;
using Newtonsoft.Json.Linq;

public static partial class BridgeCoreTest
{
    /// <summary>C18: batches with undo:false members (item.add, delete, chara.load) and a batch sent with undo:false are one entry each.</summary>
    static void C18(Ctx c)
    {
        c.Host();
        string x = W1AddItem(c, "item.add 399 (X, recorded)");
        string z = W1AddItem(c, "item.add 399 (Z, recorded)");
        if (x == null || z == null) return;
        bool chara = W1Has(c, "chara.load");

        FBatchOneEntry(c, "item.add undo:false between two recorded members", new object[]
        {
            new { cmd = "obj.transform", key = x, pos = new[] { 1, 0, 0 } },
            new Dictionary<string, object> { ["cmd"] = "item.add", ["no"] = 399, ["undo"] = false, ["as"] = "y" },
            new { cmd = "obj.transform", key = "$y.added.0.key", pos = new[] { 0, 0, 3 } },
        }, null);
        FBatchOneEntry(c, "delete undo:false between two recorded members", new object[]
        {
            new { cmd = "obj.transform", key = x, pos = new[] { 2, 0, 0 } },
            new Dictionary<string, object> { ["cmd"] = "delete", ["key"] = z, ["undo"] = false },
            new { cmd = "obj.transform", key = x, rot = new[] { 0, 30, 0 } },
        }, null);
        if (chara)
            FBatchOneEntry(c, "chara.load undo:false between two recorded members", new object[]
            {
                new { cmd = "obj.transform", key = x, pos = new[] { 3, 0, 0 } },
                new Dictionary<string, object> { ["cmd"] = "chara.load", ["path"] = c.FemaleRel, ["sex"] = "female", ["undo"] = false },
                new { cmd = "obj.transform", key = x, rot = new[] { 0, 60, 0 } },
            }, null);
        else c.Warn("chara.load is not registered: the chara.load member is not checked");
        FBatchOneEntry(c, "a batch sent with undo:false (its members inherit it)", new object[]
        {
            new { cmd = "item.add", no = 399 },
            new { cmd = "obj.transform", key = x, pos = new[] { 4, 0, 0 } },
        }, false);

        // outside a batch undo:false is unchanged: a structural command applies unrecorded and clears the history
        c.Ok(new { cmd = "item.add", no = 399, undo = false }, "item.add undo:false outside a batch");
        c.Check(W1UndoCount(c) == 0, "outside a batch it clears the history", W1UndoCount(c) + " entries");

        // scene.new inside a batch closes the batch's transaction: later members are on their own again (undo:false unrecorded)
        var s = c.Call(new
        {
            cmd = "batch",
            cmds = new object[]
            {
                new { cmd = "scene.new" },
                new Dictionary<string, object> { ["cmd"] = "item.add", ["no"] = 399, ["undo"] = false },
                new { cmd = "item.add", no = 399 },
            },
        });
        var h = c.Call(new { cmd = "history" });
        var sc = c.Call(new { cmd = "scene" });
        c.Check(s.Value<bool?>("ok") == true && (h["undo"] as JArray)?.Select(t => (string)t).SequenceEqual(new[] { "Add item" }) == true && (sc["objects"] as JArray)?.Count == 2,
            "after scene.new in a batch: the undo:false member unrecorded, the next one entry \"Add item\", 2 nodes", Ctx.Short(h.ToString(Newtonsoft.Json.Formatting.None)));
    }

    /// <summary>
    /// One batch: ok, exactly one new entry "Batch" on top of the history before it (which survives), undo restores the
    /// scene document of before the batch and redo the one after it (scene.doc {capture:false} sha256).
    /// </summary>
    static void FBatchOneEntry(Ctx c, string what, object[] cmds, bool? undo)
    {
        int n0 = W1UndoCount(c);
        string d0 = W1DocSha(c);
        var req = new Dictionary<string, object> { ["cmd"] = "batch", ["cmds"] = cmds };
        if (undo != null) req["undo"] = undo.Value;
        var b = c.Call(req);
        c.Check(b.Value<bool?>("ok") == true, what + ": ok", Ctx.Short(c.LastText));
        int n1 = W1UndoCount(c);
        string d1 = W1DocSha(c);
        c.Check(n0 > 0 && n1 == n0 + 1 && W1TopLabel(c) == "Batch", what + ": one entry \"Batch\" on top of the history before it", n0 + " -> " + n1 + " " + W1TopLabel(c));
        var u = c.Call(new { cmd = "undo" });
        c.Check(u.Value<bool?>("ok") == true && W1DocSha(c) == d0, what + ": undo restores the document of before the batch", Ctx.Short(c.LastText));
        var r = c.Call(new { cmd = "redo" });
        c.Check(r.Value<bool?>("ok") == true && W1DocSha(c) == d1 && W1UndoCount(c) == n1, what + ": redo restores the document after it", Ctx.Short(c.LastText));
    }

    /// <summary>C19: the bridge token of AppSettings and the token the host is started with.</summary>
    static void C19(Ctx c)
    {
        var alone = AppSettings.Defaults(new[] { "-bridgeToken" });
        c.Check(alone.Bridge.Token && alone.Bridge.TokenValue == null, "-bridgeToken alone: a token is required, a random one", alone.Bridge.Token + " / " + (alone.Bridge.TokenValue ?? "null"));
        var val = AppSettings.Defaults(new[] { "-bridgeToken", "s3cret-01", "-bridgePort", "17399" });
        c.Check(val.Bridge.Token && val.Bridge.TokenValue == "s3cret-01" && val.Bridge.Port == 17399, "-bridgeToken <value>: that token; the next flag still parses",
            val.Bridge.Token + " / " + (val.Bridge.TokenValue ?? "null") + " / port " + val.Bridge.Port);
        var flag = AppSettings.Defaults(new[] { "-bridgeToken", "-noBridge" });
        c.Check(flag.Bridge.Token && flag.Bridge.TokenValue == null && !flag.Bridge.Enabled, "a following flag is not a token value", flag.Bridge.Token + " / " + (flag.Bridge.TokenValue ?? "null") + " / enabled " + flag.Bridge.Enabled);
        var str = AppSettings.Defaults(Array.Empty<string>(), "{\"bridge\":{\"token\":\"from-json\"}}");
        c.Check(str.Bridge.Token && str.Bridge.TokenValue == "from-json", "bridge.token \"from-json\": required with that token", str.Bridge.Token + " / " + (str.Bridge.TokenValue ?? "null"));
        var on = AppSettings.Defaults(Array.Empty<string>(), "{\"bridge\":{\"token\":true}}");
        var off = AppSettings.Defaults(Array.Empty<string>(), "{\"bridge\":{\"token\":\"\"}}");
        c.Check(on.Bridge.Token && on.Bridge.TokenValue == null && !off.Bridge.Token && off.Bridge.TokenValue == null, "bridge.token true = random, \"\" = off");
        var over = AppSettings.Defaults(new[] { "-bridgeToken", "from-cli" }, "{\"bridge\":{\"token\":\"from-override\"}}");
        c.Check(over.Bridge.TokenValue == "from-override", "the launch override wins over the command line", over.Bridge.TokenValue ?? "null");
        bool threw = false;
        try { new AppSettings().ApplyJson("{\"bridge\":{\"token\":1}}", "C19"); }
        catch (FormatException) { threw = true; }
        c.Check(threw, "bridge.token of another JSON type is a settings error");

        // the host is started with the token
        foreach (var (what, required, value) in new[] { ("a fixed token", true, "s3cret-01"), ("a random token", true, (string)null), ("no token", false, (string)null) })
        {
            var hs = c.HostSettings();
            hs.Bridge.Token = required;
            hs.Bridge.TokenValue = value;
            var app = AppCore.ForTests(c.Services, hs, null, c.Services.NewSessionOptions());
            try
            {
                string t = app.Bridge?.Server?.Token;
                bool ok = !required ? t == null : value != null ? t == value : !string.IsNullOrEmpty(t) && t != "s3cret-01";
                c.Check(ok, "the host runs with " + what, t == null ? "no token" : value != null ? t : "token of " + t.Length + " characters");
            }
            finally { app.Dispose(); }
        }
    }
}
