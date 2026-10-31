// Bridge tests: the reply envelope, error codes and the argument view.
using System;
using System.Collections.Generic;
using System.Threading;
using HS2Next.Api.Bridge;
using HS2Next.Content;
using Newtonsoft.Json.Linq;

namespace HS2Next.Bridge.Tests
{
    public enum TestMode { Fast, Precise }

    public sealed class EchoDto
    {
        public float Speed = 1f;
        public string Name;
        [BridgeArg("count of things", Required = true)] public int Count;
        [BridgeArg(Name = "renamed", Type = "vec3")] public float[] Position;
        public TestMode Mode = TestMode.Fast;
        public List<string> Tags;
        [BridgeIgnore] public string Hidden = "x";
        public bool? Flag { get; set; }
    }

    public sealed class ResultDto
    {
        public float F01 = 0.1f;
        public float Tiny = 1e-7f;
        public float Big = 8.513037E+08f;
        public float Max = float.MaxValue;
        public double D01 = 0.1;
        public string Text = "日本語 ü \"q\" \n";
        public TestMode Mode = TestMode.Precise;
        public ContentId Part = new ContentId("illusion.hs2", 42);
        [BridgeIgnore] public string Secret = "s";
        [BridgeArg(Name = "renamedField")] public int Other = 7;
        public string Null;
        public RawJson Raw = new RawJson("{\"a\":[1,2.5,\"x\"]}");
        public int[] Ints = { 1, 2, 3 };
    }

    public static class EnvelopeGroup
    {
        public static void Run(BridgeChecks t)
        {
            using (var s = Kit.NewServer(start: false))
            {
                Kit.RegisterEcho(s);
                s.Commands.Register(new CommandSpec("test.dto") { Gate = ReadyGate.None }, ctx => new ResultDto());
                s.Commands.Register(new CommandSpec("test.dict") { Gate = ReadyGate.None }, ctx => new BridgeReply().With("b", 2).With("a", 1).With("nested", new Dictionary<string, object> { ["Keep"] = "Case" }));
                s.Commands.Register(new CommandSpec("test.null") { Gate = ReadyGate.None }, ctx => null);
                s.Commands.Register(new CommandSpec("test.list") { Gate = ReadyGate.None }, ctx => new List<int> { 1, 2 });
                s.Commands.Register(new CommandSpec("test.raw") { Gate = ReadyGate.None }, ctx => new RawJson("{\"x\":1,\"y\":{\"z\":[true]}}"));
                s.Commands.Register(new CommandSpec("test.clash") { Gate = ReadyGate.None }, ctx => new BridgeReply().With("ok", false).With("cmd", "other").With("v", 1));
                s.Commands.Register(new CommandSpec("test.warn") { Gate = ReadyGate.None }, ctx => { ctx.Warn("w1"); ctx.Warn("w2"); return new BridgeReply().With("v", 1); });

                var r = Kit.Exec(s, "{\"cmd\":\"test.echo\",\"a\":1}");
                t.Check(Kit.Ok(r), "echo ok");
                t.Equal("test.echo", (string)r?["cmd"], "cmd echoed");
                t.Equal(1, (int?)r?["a"] ?? -1, "result field merged");
                t.Check(r?["id"] == null, "no id when none given");
                var props = new List<string>();
                foreach (var p in r.Properties()) props.Add(p.Name);
                t.Equal("ok", props[0], "ok first");
                t.Equal("cmd", props[1], "cmd second");

                r = Kit.Exec(s, "{\"cmd\":\"test.echo\",\"id\":17}");
                t.Equal("17", Kit.Str(r, "id"), "numeric id echoed");
                r = Kit.Exec(s, "{\"cmd\":\"test.echo\",\"id\":\"abc\"}");
                t.Equal("abc", Kit.Str(r, "id"), "string id echoed");
                r = Kit.Exec(s, "{\"cmd\":\"test.echo\",\"id\":{\"k\":[1,\"2\"]}}");
                t.Equal("{\"k\":[1,\"2\"]}", Kit.Str(r, "id"), "object id echoed");
                t.Check(r?["warnings"] == null, "envelope fields are not unknown arguments");

                var text = s.Execute("{\"cmd\":\"test.dto\"}");
                r = JObject.Parse(text);
                t.Check(text.Contains("\"f01\":0.1,"), "float 0.1 written as 0.1: " + text);
                t.Check(text.Contains("\"tiny\":1E-07"), "float 1e-7 round-trip form");
                t.Check(text.Contains("\"big\":851303700.0") || text.Contains("\"big\":8.513037E+08"), "float 8.513037E+08 round-trip form");
                t.Equal(float.MaxValue, (float)(double)r["max"], "float.MaxValue round trip");
                t.Equal(0.1f, (float)(double)r["f01"], "float bits round trip");
                t.Equal(0.1, (double)r["d01"], "double round trip");
                t.Equal("日本語 ü \"q\" \n", (string)r["text"], "non-ASCII text round trip");
                t.Check(text.Contains("日本語"), "non-ASCII written as UTF-8, not escaped");
                t.Equal("Precise", (string)r["mode"], "enum by name");
                t.Equal("illusion.hs2#42", (string)r["part"], "ContentId as guid#id");
                t.Check(r["secret"] == null, "[BridgeIgnore] member left out");
                t.Equal(7, (int?)r["renamedField"] ?? -1, "[BridgeArg(Name)] renames a result member");
                t.Check(!((IDictionary<string, JToken>)r).ContainsKey("null"), "null members left out");
                t.Check(text.Contains("\"raw\":{\"a\":[1,2.5,\"x\"]}"), "nested RawJson verbatim");
                t.Equal("[1,2,3]", Kit.Str(r, "ints"), "int array");

                r = Kit.Exec(s, "{\"cmd\":\"test.dict\"}");
                t.Equal("b,a,nested", string.Join(",", Names(r, 2)), "dictionary members in insertion order");
                t.Equal("Case", Kit.Str(r, "nested.Keep"), "dictionary keys are not camel-cased");

                r = Kit.Exec(s, "{\"cmd\":\"test.null\"}");
                t.Equal(2, r?.Count ?? -1, "null result: ok and cmd only");
                r = Kit.Exec(s, "{\"cmd\":\"test.list\"}");
                t.Equal("[1,2]", Kit.Str(r, "result"), "non-object result under result");
                r = Kit.Exec(s, "{\"cmd\":\"test.raw\"}");
                t.Equal(1, (int?)r?["x"] ?? -1, "top-level RawJson merged");
                t.Equal("[true]", Kit.Str(r, "y.z"), "top-level RawJson nested value");
                r = Kit.Exec(s, "{\"cmd\":\"test.clash\"}");
                t.Check(Kit.Ok(r) && (string)r["cmd"] == "test.clash" && (int)r["v"] == 1, "envelope fields win over result fields");
                t.Check(Kit.Str(r, "warnings")?.Contains("clashes") == true, "clash reported as a warning");
                r = Kit.Exec(s, "{\"cmd\":\"test.warn\"}");
                t.Equal("[\"w1\",\"w2\"]", Kit.Str(r, "warnings"), "ctx.Warn lines in warnings[]");
                var last = new List<string>(Names(r, 0));
                t.Equal("warnings", last[last.Count - 1], "warnings last");

                // an empty body is ping (in-process: empty string)
                r = JObject.Parse(s.SubmitAsync("").Result);
                t.Equal("ping", (string)r["cmd"], "empty body = ping");
                t.Equal(BridgeProtocol.Version, (int)r["protocol"], "ping protocol");
            }
        }

        static IEnumerable<string> Names(JObject o, int skip)
        {
            int i = 0;
            if (o == null) yield break;
            foreach (var p in o.Properties()) { if (i++ >= skip) yield return p.Name; }
        }
    }

    public static class ErrorsGroup
    {
        public static void Run(BridgeChecks t)
        {
            using (var s = Kit.NewServer(start: false))
            {
                var calls = Kit.RegisterEcho(s);
                s.Commands.Register(new CommandSpec("test.fail") { Gate = ReadyGate.None }, ctx =>
                    throw new BridgeException(BridgeCodes.NotFound, "no such node", new BridgeErrorDetail
                    {
                        Arg = "key", Expected = "node", Candidates = new[] { "a", "b" }, Phase = "3.4",
                        Extra = new Dictionary<string, object> { ["n"] = 3 },
                    }));
                s.Commands.Register(new CommandSpec("test.unsupported") { Gate = ReadyGate.None }, ctx => throw new BridgeException(BridgeError.Unsupported("IK comes later", "3.4")));
                s.Commands.Register(new CommandSpec("test.throw") { Gate = ReadyGate.None }, ctx => throw new InvalidOperationException("boom"));
                s.Commands.Register(new CommandSpec("lab.throw") { Gate = ReadyGate.None, Owner = "stp3.lab" }, ctx => throw new InvalidOperationException("plugin boom"));
                s.Commands.Register(new CommandSpec("test.required").Arg("key", ArgTypes.Node, required: true).WithGate(ReadyGate.None), ctx => "x");

                var r = Kit.Exec(s, "{\"cmd\":\"test.ecko\"}");
                t.Equal(BridgeCodes.UnknownCommand, Kit.Code(r), "unknown command");
                t.Check(Kit.Str(r, "detail.candidates")?.Contains("test.echo") == true, "unknown command: candidates name test.echo");
                t.Equal("cmd", Kit.Str(r, "detail.arg"), "unknown command: detail.arg = cmd");
                t.Check(((string)r?["error"])?.Contains("did you mean") == true, "unknown command: readable hint");

                r = Kit.Exec(s, "{\"cmd\":\"test.fail\"}");
                t.Equal(BridgeCodes.NotFound, Kit.Code(r), "BridgeException code");
                t.Equal("no such node", (string)r?["error"], "BridgeException message");
                t.Equal("key", Kit.Str(r, "detail.arg"), "detail.arg");
                t.Equal("node", Kit.Str(r, "detail.expected"), "detail.expected");
                t.Equal("[\"a\",\"b\"]", Kit.Str(r, "detail.candidates"), "detail.candidates");
                t.Equal("3.4", Kit.Str(r, "detail.phase"), "detail.phase");
                t.Equal("3", Kit.Str(r, "detail.n"), "detail extra member");
                t.Check(r?["ok"]?.Type == JTokenType.Boolean && !(bool)r["ok"], "ok false");
                r = Kit.Exec(s, "{\"cmd\":\"test.unsupported\"}");
                t.Check(Kit.Code(r) == BridgeCodes.Unsupported && Kit.Str(r, "detail.phase") == "3.4", "unsupported with phase");
                r = Kit.Exec(s, "{\"cmd\":\"test.throw\"}");
                t.Equal(BridgeCodes.Internal, Kit.Code(r), "plain exception of a core command = internal");
                t.Check(((string)r?["error"])?.Contains("InvalidOperationException: boom") == true, "internal names the exception");
                r = Kit.Exec(s, "{\"cmd\":\"lab.throw\"}");
                t.Equal(BridgeCodes.PluginFault, Kit.Code(r), "plain exception of a plugin command = plugin_fault");
                t.Equal("stp3.lab", Kit.Str(r, "detail.owner"), "plugin_fault names the owner");
                r = Kit.Exec(s, "{\"cmd\":\"test.required\"}");
                t.Check(Kit.Code(r) == BridgeCodes.BadArgs && Kit.Str(r, "detail.arg") == "key", "declared required argument missing = bad_args before the handler");
                r = Kit.Exec(s, "{\"cmd\":\"test.required\",\"key\":null}");
                t.Equal(BridgeCodes.BadArgs, Kit.Code(r), "required argument given as null counts as missing");

                // envelope values
                foreach (var bad in new[] { "{\"cmd\":\"test.echo\",\"waitFrames\":-1}", "{\"cmd\":\"test.echo\",\"waitFrames\":\"2\"}", "{\"cmd\":\"test.echo\",\"waitFrames\":1.5}",
                                            "{\"cmd\":\"test.echo\",\"strict\":\"yes\"}", "{\"cmd\":\"test.echo\",\"undo\":0}" })
                {
                    r = Kit.Exec(s, bad);
                    t.Equal(BridgeCodes.BadArgs, Kit.Code(r), "bad envelope value: " + bad);
                }
                foreach (var bad in new[] { "{\"cmd\":\"test.echo\",\"timeout\":0}", "{\"cmd\":\"test.echo\",\"timeout\":601}", "{\"cmd\":\"test.echo\",\"timeout\":\"5\"}" })
                {
                    r = JObject.Parse(s.SubmitAsync(bad).Result);   // answered at submission, no pump needed
                    t.Check(Kit.Code(r) == BridgeCodes.BadArgs && Kit.Str(r, "detail.arg") == "timeout", "bad timeout answered at once: " + bad);
                }
                r = JObject.Parse(s.SubmitAsync("{\"x\":1}").Result);
                t.Check(Kit.Code(r) == BridgeCodes.BadArgs && Kit.Str(r, "detail.arg") == "cmd", "missing cmd = bad_args");
                r = JObject.Parse(s.SubmitAsync("{\"cmd\":5}").Result);
                t.Equal(BridgeCodes.BadArgs, Kit.Code(r), "non-string cmd = bad_args");
                foreach (var bad in new[] { "{\"cmd\":", "[1,2]", "\"ping\"", "{\"cmd\":\"ping\"} x", "{\"cmd\":\"ping\",\"cmd\":\"help\"}", "{'cmd':'ping'}x" })
                {
                    r = JObject.Parse(s.SubmitAsync(bad).Result);
                    t.Equal(BridgeCodes.BadJson, Kit.Code(r), "bad_json: " + bad);
                }
                r = JObject.Parse(s.SubmitAsync("﻿{\"cmd\":\"ping\"}").Result);
                t.Check(Kit.Ok(r), "a UTF-8 byte-order mark is accepted");
                r = JObject.Parse(s.SubmitAsync("{\"cmd\":\"ping\" /* comment */}").Result);
                t.Check(Kit.Ok(r), "comments are ignored");
            }

            // readiness gate
            using (var s = Kit.NewServer(o => { o.RequiredStages = new[] { "catalog", "studioDb" }; o.OptionalStages = new[] { "scene" }; }, start: false))
            {
                var calls = Kit.RegisterEcho(s, "test.gated", sp => sp.Gate = ReadyGate.App);
                Kit.RegisterEcho(s, "test.needsScene", sp => { sp.Gate = ReadyGate.None; sp.Require("scene"); });
                var r = Kit.Exec(s, "{\"cmd\":\"test.gated\"}");
                t.Equal(BridgeCodes.NotReady, Kit.Code(r), "gated command before readiness = not_ready");
                t.Equal("false", Kit.Str(r, "detail.ready.catalog"), "not_ready lists the stages");
                t.Equal(0, calls[0], "gated handler not run");
                r = Kit.Exec(s, "{\"cmd\":\"no.such\"}");
                t.Equal(BridgeCodes.NotReady, Kit.Code(r), "unknown command before readiness = not_ready");
                r = Kit.Exec(s, "{\"cmd\":\"ping\"}");
                t.Check(Kit.Ok(r) && Kit.Str(r, "appReady") == "false", "ping works before readiness and reports appReady false");
                t.Equal("{\"catalog\":false,\"studioDb\":false,\"scene\":false}", Kit.Str(r, "ready"), "ping.ready in declaration order");
                t.Check(Kit.Str(r, "studio") == "false" && Kit.Str(r, "version") == "1", "ping carries HS2's studio (= appReady) and version 1");
                t.Check(r["readyDetail"] == null, "no readyDetail without details");
                s.Readiness.SetDetail("catalog", "scanning zipmods 1200/5934");
                r = Kit.Exec(s, "{\"cmd\":\"ping\"}");
                t.Equal("scanning zipmods 1200/5934", Kit.Str(r, "readyDetail.catalog"), "ping.readyDetail");
                t.Equal("scanning zipmods 1200/5934", s.Readiness.Detail("catalog"), "Readiness.Detail");
                // not_ready is answered at submission (off the main thread), without a pump
                var pre = s.SubmitAsync("{\"cmd\":\"test.gated\"}");
                t.Check(pre.IsCompleted && Kit.Code(JObject.Parse(pre.Result)) == BridgeCodes.NotReady, "not_ready answered at submission, no pump needed");
                t.Equal("scanning zipmods 1200/5934", Kit.Str(JObject.Parse(pre.Result), "detail.readyDetail.catalog"), "not_ready detail carries readyDetail");
                s.Readiness.SetDetail("catalog", null);
                s.Readiness.Set("catalog", true);
                r = Kit.Exec(s, "{\"cmd\":\"test.gated\"}");
                t.Equal(BridgeCodes.NotReady, Kit.Code(r), "one required stage still missing");
                s.Readiness.Set("studioDb", true);
                r = Kit.Exec(s, "{\"cmd\":\"test.gated\"}");
                t.Check(Kit.Ok(r) && calls[0] == 1, "gated command runs once every required stage is ready");
                r = Kit.Exec(s, "{\"cmd\":\"no.such\"}");
                t.Equal(BridgeCodes.UnknownCommand, Kit.Code(r), "unknown command after readiness = unknown_command");
                r = Kit.Exec(s, "{\"cmd\":\"test.needsScene\"}");
                t.Check(Kit.Code(r) == BridgeCodes.NotReady && ((string)r["error"]).Contains("scene"), "Requires(scene) gates on an optional stage");
                s.Readiness.Set("scene", true);
                t.Check(Kit.Ok(Kit.Exec(s, "{\"cmd\":\"test.needsScene\"}")), "Requires(scene) runs once scene is ready");
                s.Readiness.Declare("plugins", true);
                t.Check(!s.Readiness.AppReady, "a newly declared required stage makes the app not ready");
                s.Readiness.Set("adhoc", true);
                t.Check(!s.Readiness.AppReady && s.Readiness.IsReady("adhoc"), "Set of an undeclared stage declares it as optional");
            }

            // busy: queue limit without a pump
            using (var s = Kit.NewServer(o => o.QueueLimit = 2, start: false))
            {
                Kit.RegisterEcho(s);
                var a = s.SubmitAsync("{\"cmd\":\"test.echo\"}");
                var b = s.SubmitAsync("{\"cmd\":\"test.echo\"}");
                var c = s.SubmitAsync("{\"cmd\":\"test.echo\"}");
                t.Check(c.IsCompleted && Kit.Code(JObject.Parse(c.Result)) == BridgeCodes.Busy, "third request with queue limit 2 = busy at once");
                t.Check(!a.IsCompleted && !b.IsCompleted, "queued requests wait for the pump");
                s.ManualFrame();
                t.Check(a.IsCompleted && b.IsCompleted && Kit.Ok(JObject.Parse(b.Result)), "both queued requests answered by one pump");
            }
        }
    }

    public static class ArgsGroup
    {
        public static void Run(BridgeChecks t)
        {
            using (var s = Kit.NewServer(start: false))
            {
                ICommandArgs seen = null;
                EchoDto bound = null;
                Exception bindError = null;
                s.Commands.Register(CommandSpec.For<EchoDto>("test.args", "args").WithGate(ReadyGate.None).Arg("extra", ArgTypes.Object), ctx =>
                {
                    seen = ctx.Args;
                    bindError = null;
                    try { bound = ctx.Args.Bind<EchoDto>(); }
                    catch (Exception e) { bindError = e; bound = null; }
                    return null;
                });
                var counter = new int[1];
                s.Commands.Register(new CommandSpec("test.side").Arg("v", ArgTypes.Int).WithGate(ReadyGate.None), ctx => { counter[0]++; return null; });

                var r = Kit.Exec(s, "{\"cmd\":\"test.args\",\"count\":3,\"speed\":2,\"name\":\"n\",\"renamed\":[1,2,3.5],\"mode\":\"precise\",\"tags\":[\"a\",\"b\"],\"flag\":true," +
                                    "\"extra\":{\"i\":5,\"f\":0.25,\"s\":\"x\",\"b\":false,\"n\":null,\"o\":{\"p\":1},\"arr\":[1,2]}}");
                t.Check(Kit.Ok(r) && r["warnings"] == null, "declared arguments: no warnings");
                t.Check(bound != null && bindError == null, "Bind succeeded");
                if (bound != null)
                {
                    t.Equal(3, bound.Count, "Bind int");
                    t.Equal(2f, bound.Speed, "Bind float from an integer");
                    t.Equal("n", bound.Name, "Bind string");
                    t.Equal("1,2,3.5", string.Join(",", bound.Position ?? new float[0]), "Bind renamed float[]");
                    t.Equal(TestMode.Precise, bound.Mode, "Bind enum by name, ignoring case");
                    t.Equal("a,b", string.Join(",", bound.Tags ?? new List<string>()), "Bind List<string>");
                    t.Equal(true, bound.Flag, "Bind nullable property");
                    t.Equal("x", bound.Hidden, "[BridgeIgnore] member keeps its initial value");
                }
                t.Equal("cmd,count,speed,name,renamed,mode,tags,flag,extra", string.Join(",", seen.Names), "Names in request order");
                t.Check(seen.Has("count") && !seen.Has("nope"), "Has");
                var ex = seen.Nested("extra");
                t.Check(ex != null, "Nested view");
                if (ex != null)
                {
                    t.Equal(5, ex.Get<int>("i"), "nested int");
                    t.Equal(0.25f, ex.Get<float>("f"), "nested float");
                    t.Equal(0.25, ex.Get<double>("f"), "nested double");
                    t.Equal("x", ex.Get<string>("s"), "nested string");
                    t.Equal(false, ex.Get("b", true), "nested bool");
                    t.Check(!ex.Has("n") && ex.Kind("n") == ArgKind.Null, "explicit null counts as absent, Kind Null");
                    t.Equal(ArgKind.Missing, ex.Kind("zz"), "Kind Missing");
                    t.Equal(ArgKind.Object, ex.Kind("o"), "Kind Object");
                    t.Equal(ArgKind.Integer, ex.Kind("i"), "Kind Integer");
                    t.Equal(ArgKind.Float, ex.Kind("f"), "Kind Float");
                    t.Equal(9, ex.Get("missing", 9), "Get fallback");
                    t.Equal("{\"p\":1}", ex.RawJson("o"), "RawJson of a member");
                    t.Equal("{\"p\":1}", ex.Get<RawJson>("o")?.Text, "Get<RawJson>");
                    t.Check(ex.RawJson("zz") == null, "RawJson of an absent member = null");
                    t.Equal("1,2", string.Join(",", ex.Get<int[]>("arr")), "int[]");
                    t.Equal("1,2", string.Join(",", ex.Get<List<long>>("arr")), "List<long>");
                    var dict = ex.Get<Dictionary<string, object>>("o");
                    t.Check(dict != null && dict.ContainsKey("p") && (long)dict["p"] == 1, "Dictionary<string, object>");
                    var plain = ex.Get<object>("o") as Dictionary<string, object>;
                    t.Check(plain != null && (long)plain["p"] == 1, "Get<object> gives plain values");
                    t.Check(ex.Unknown.Count == 0, "nested views report no unknown arguments");
                    t.Check(ex.TryGet<int>("i", out var iv) && iv == 5 && !ex.TryGet<int>("zz", out _), "TryGet");
                    var e1 = t.Throws<BridgeException>(() => ex.Get<int>("f"), "int from 0.25 = bad_args");
                    t.Check(e1?.Code == BridgeCodes.BadArgs && e1.Error.Detail?.Arg == "extra.f" && e1.Error.Detail.Expected == "int", "bad_args names the nested path and the type");
                    t.Throws<BridgeException>(() => ex.Get<bool>("i"), "bool from a number = bad_args");
                    t.Throws<BridgeException>(() => ex.Get<int>("s"), "int from a string = bad_args");
                    t.Throws<BridgeException>(() => ex.Get<TestMode>("s"), "enum from an unknown name = bad_args");
                    t.Throws<BridgeException>(() => ex.Get<int[]>("o"), "array from an object = bad_args");
                    t.Throws<BridgeException>(() => ex.Require<int>("zz"), "Require of a missing argument = bad_args");
                    t.Throws<BridgeException>(() => seen.Nested("count"), "Nested of a number = bad_args");
                    t.Equal("5", ex.Get<string>("i"), "string from a number (invariant)");
                }
                r = Kit.Exec(s, "{\"cmd\":\"test.args\"}");
                t.Equal(BridgeCodes.BadArgs, Kit.Code(r), "required DTO member missing = bad_args (spec check)");

                r = Kit.Exec(s, "{\"cmd\":\"test.args\",\"count\":1,\"mode\":1}");
                t.Check(bound != null && bound.Mode == TestMode.Precise, "enum from a defined number");
                r = Kit.Exec(s, "{\"cmd\":\"test.args\",\"count\":3.0}");
                t.Check(bound != null && bound.Count == 3, "int from an integral float (3.0)");
                r = Kit.Exec(s, "{\"cmd\":\"test.args\",\"count\":3000000000}");
                t.Check(bound == null && bindError is BridgeException be && be.Code == BridgeCodes.BadArgs, "int out of range = bad_args");
                r = Kit.Exec(s, "{\"cmd\":\"test.args\",\"count\":1,\"speed\":1e39}");
                t.Check(bound == null && bindError is BridgeException, "float beyond float32 = bad_args");
                r = Kit.Exec(s, "{\"cmd\":\"test.args\",\"count\":1,\"renamed\":[1,\"a\"]}");
                t.Check(bindError is BridgeException b2 && b2.Error.Detail?.Arg == "renamed[1]", "element errors name the index: " + (bindError?.Message ?? "none"));

                // unknown arguments
                r = Kit.Exec(s, "{\"cmd\":\"test.args\",\"count\":1,\"cuont\":2,\"zzz\":3}");
                t.Check(Kit.Ok(r), "unknown arguments do not fail without strict");
                var w = Kit.Str(r, "warnings") ?? "";
                t.Check(w.Contains("unknown argument 'cuont'") && w.Contains("did you mean count"), "warning with a candidate: " + w);
                t.Check(w.Contains("'zzz'"), "every unknown argument warned");
                t.Equal("cuont,zzz", string.Join(",", seen.Unknown), "ICommandArgs.Unknown");
                r = Kit.Exec(s, "{\"cmd\":\"test.side\",\"v\":1,\"vv\":2,\"strict\":true}");
                t.Check(Kit.Code(r) == BridgeCodes.BadArgs && Kit.Str(r, "detail.arg") == "vv", "strict: unknown argument = bad_args");
                t.Check(Kit.Str(r, "detail.candidates")?.Contains("v") == true, "strict: candidates");
                t.Equal(0, counter[0], "strict: the handler did not run");
                r = Kit.Exec(s, "{\"cmd\":\"test.side\",\"v\":1,\"strict\":true}");
                t.Check(Kit.Ok(r) && counter[0] == 1, "strict with declared arguments only runs");
                r = Kit.Exec(s, "{\"cmd\":\"test.side\",\"strict\":false,\"q\":1}");
                t.Check(Kit.Ok(r) && counter[0] == 2 && Kit.Str(r, "warnings") != null, "strict false = warnings");
            }
            // AllowExtraArgs
            using (var s = Kit.NewServer(start: false))
            {
                Kit.RegisterEcho(s);
                var r = Kit.Exec(s, "{\"cmd\":\"test.echo\",\"anything\":1,\"strict\":true}");
                t.Check(Kit.Ok(r) && r["warnings"] == null, "AllowExtraArgs: no warning and allowed under strict");
            }
        }
    }
}

namespace HS2Next.Bridge.Tests
{
    public static class NumbersGroup
    {
        public sealed class Odd
        {
            public float NanF = float.NaN;
            public double PosInf = double.PositiveInfinity;
            public float? NullableNan = float.NaN;
            public float[] Arr = { 1f, float.NegativeInfinity };
            public float Fine = 2.5f;
            public double D = 0.30000000000000004;
            public float F = 0.3f;
        }

        public static void Run(BridgeChecks t)
        {
            using (var s = Kit.NewServer(start: false))
            {
                var afterRan = new List<string>();
                s.Commands.Register(new CommandSpec("n.odd") { Gate = ReadyGate.None }, ctx => new Odd());
                s.Commands.Register(new CommandSpec("n.after") { Gate = ReadyGate.None }, ctx =>
                {
                    ctx.AfterReply(() => afterRan.Add("after@" + ctx.Frames.Frame));
                    return new BridgeReply().With("frame", ctx.Frames.Frame);
                });
                string text = s.Execute("{\"cmd\":\"n.odd\"}");
                t.Check(text.Contains("\"nanF\":null") && text.Contains("\"posInf\":null") && text.Contains("\"nullableNan\":null") && text.Contains("\"arr\":[1.0,null]"),
                    "NaN and infinities written as null: " + text);
                t.Check(text.Contains("\"d\":0.30000000000000004") && text.Contains("\"f\":0.3"), "double and float keep their own round-trip forms");
                var task = s.SubmitAsync("{\"cmd\":\"n.after\"}");
                int f = s.ManualFrame();
                t.Check(task.IsCompleted && afterRan.Count == 0, "AfterReply has not run when the reply leaves");
                s.ManualFrame();
                t.Equal("after@" + (f + 1), string.Join(",", afterRan), "AfterReply ran at the first pump after the reply");
                afterRan.Clear();
                task = s.SubmitAsync("{\"cmd\":\"batch\",\"cmds\":[{\"cmd\":\"n.after\"},{\"cmd\":\"n.after\"}]}");
                f = s.ManualFrame();
                s.ManualFrame();
                t.Check(task.IsCompleted && afterRan.Count == 2 && afterRan[0] == "after@" + (f + 1), "AfterReply of batch members after the batch reply");
            }
            t.Check(BridgeCodes.IsKnown(BridgeCodes.Unavailable) && BridgeCodes.All.Count == 13, "code unavailable is in the stable list");
            var root = OutputRoot.For(System.IO.Path.GetTempPath());
            t.Check(root is IOutputRoot && root.Root != null, "OutputRoot.For");
        }
    }
}
